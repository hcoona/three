# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# ruff: noqa: ANN401, S101

"""Campaign-bound transport; old normal campaign state is never imported."""

from contextlib import contextmanager
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from base_operator import Operator as BaseOperator
from base_operator import OperatorDeadlineExceeded
from campaign import EXPIRY, Campaign, gate, read
from source_checks import verify as verify_source


class Operator(BaseOperator):
    """Enforce the retained caller lifetime and effects."""

    def __init__(self, directory: Any, target: Any) -> None:
        """Load or construct the reserved caller state."""
        directory = Path(directory)
        assert directory.name == "operation"
        self.attempt_id = directory.parent.name
        self.scenario_id = directory.parents[2].name
        self.campaign = Campaign(directory.parents[4])
        with self.campaign.locked():
            state, _, attempt = self.campaign.active(
                self.scenario_id, self.attempt_id, target
            )
            self.started = attempt["started"]
            self.mode = attempt["mode"]
            self.original_deadline = attempt["deadline"]
            if attempt.get("operator_constructed"):
                assert (directory / "ledger.json").exists(), (
                    "original ledger missing"
                )
            else:
                assert "construction" not in attempt, (
                    "construction failure/uncertainty is spent"
                )
                assert not directory.exists(), "construction directory exists"
                attempt["construction"] = {
                    "state": "started",
                    "at": datetime.now(UTC).isoformat(),
                }
                self.campaign.save(state)
            self._initializing = not (directory / "ledger.json").exists()
            try:
                binding = read(directory.parent / "execution-binding.json")
                verify_source(target, binding["tree"])
                super().__init__(directory, target)
                self._initializing = False
                assert self.ledger["started"] == self.started
                assert self.ledger["mode"] == self.mode
                assert self.ledger["deadline"] == self.original_deadline
            except BaseException as error:
                if not attempt.get("operator_constructed"):
                    attempt["construction"].update(
                        state="failed",
                        failed_at=datetime.now(UTC).isoformat(),
                        exception_type=type(error).__name__,
                    )
                    self.campaign.save(state)
                raise
            if not attempt.get("operator_constructed"):
                attempt["construction"].update(
                    state="completed",
                    completed_at=datetime.now(UTC).isoformat(),
                )
                attempt["operator_constructed"] = True
                self.campaign.save(state)

    def save(self) -> None:
        """Persist the original ledger durably."""
        if self._initializing:
            self.ledger.update(
                started=self.started,
                mode=self.mode,
                deadline=self.original_deadline,
            )
        assert self.ledger["started"] == self.started
        assert self.ledger["mode"] == self.mode
        super().save()

    def remaining(self) -> Any:
        """Return the original remaining lifetime or stop."""
        remaining = super().remaining()
        hard = min(EXPIRY, datetime.fromisoformat(self.original_deadline))
        left = (hard - datetime.now(UTC)).total_seconds()
        if left <= 0:
            msg = "original deadline or Governance expired"
            raise OperatorDeadlineExceeded(msg)
        return min(remaining, left)

    @contextmanager
    def deadline(self) -> Any:
        """Validate the deadline caller boundary."""
        if getattr(self, "_campaign_lock_active", False):
            with super().deadline():
                yield
            return
        with self.campaign.locked():
            self.campaign.active(self.scenario_id, self.attempt_id, self.target)
            current = read(self.ledger_path)
            assert current["target"] == self.target
            assert current["started"] == self.started
            assert current["mode"] == self.mode
            assert current["deadline"] == self.original_deadline
            self.ledger = current
            self._campaign_lock_active = True
            try:
                with super().deadline():
                    yield
            finally:
                self._campaign_lock_active = False

    def _request(
        self, category: Any, name: Any, url: Any, **kwargs: Any
    ) -> Any:
        """Validate the request caller boundary."""
        assert self._campaign_lock_active
        self.campaign.active(self.scenario_id, self.attempt_id, self.target)
        if category == "dispatch":
            assert not any(
                r["category"] == "dispatch" for r in self.ledger["requests"]
            )
        if category == "approval":
            assert "preparation_audit_completed" in self.ledger
            directory = self.directory.parent
            binding = read(directory / "execution-binding.json")
            proof = gate(
                directory,
                "independent-preapproval-gate.json",
                binding,
                "preapproval",
                run=self.ledger["run"],
            )
            expected = (
                ["upload", "upload"]
                if self.mode == "stop-after-wheel"
                else ["already-present", "upload"]
            )
            assert proof["dispositions"] == expected
            self.campaign.reserve_upload(
                self.scenario_id, self.attempt_id, self.target
            )
        return super()._request(category, name, url, **kwargs)
