"""Regression tests for LLM text splitter logging."""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from llm_text_splitter import text_splitter_manager as manager
from llm_text_splitter.text_splitter_agent import SegmentedTopic


class _FakeRunResult:
    """Minimal Runner.run result used by split_text."""

    def __init__(
        self,
        final_output: object,
        *,
        next_input_content: str = "sensitive follow-up stays internal",
    ) -> None:
        self.final_output = final_output
        self._next_input_content = next_input_content

    def to_input_list(self) -> list[dict[str, str]]:
        return [
            {
                "content": self._next_input_content,
                "role": "user",
            },
        ]


def test_split_text_does_not_log_sensitive_input(
    caplog: Any,
    monkeypatch: Any,
) -> None:
    """Ensure split_text does not log user-provided payload text."""
    sensitive_payload = "clientSecret=super-secret-value"
    sensitive_topic = f"Topic echoes {sensitive_payload}"
    sensitive_feedback_retry = f"Retry feedback echoes {sensitive_payload}"
    sensitive_feedback_final = f"Final feedback echoes {sensitive_payload}"
    sensitive_next_input = f"Next input echoes {sensitive_payload}"
    calls = {"text-splitter": 0, "evaluator": 0}

    def fake_text_splitter_agent(model: str, openai_client: object) -> str:
        assert openai_client is not None
        return f"text-splitter:{model}"

    def fake_evaluator_agent(model: str, openai_client: object) -> str:
        assert openai_client is not None
        return f"evaluator:{model}"

    async def fake_run(agent: str, **kwargs: Any) -> _FakeRunResult:
        assert kwargs["input"]
        if agent.startswith("text-splitter:"):
            calls["text-splitter"] += 1
            return _FakeRunResult(
                manager.SegmentedTopicList(
                    topics=[
                        SegmentedTopic(
                            topic=sensitive_topic,
                            indices=[0],
                        ),
                    ],
                ),
                next_input_content=sensitive_next_input,
            )
        calls["evaluator"] += 1
        if calls["evaluator"] == 1:
            return _FakeRunResult(
                manager.EvaluationResult(
                    score=0.1,
                    feedback=sensitive_feedback_retry,
                ),
            )
        return _FakeRunResult(
            manager.EvaluationResult(
                score=1.0,
                feedback=sensitive_feedback_final,
            ),
        )

    monkeypatch.setattr(
        manager,
        "get_text_splitter_agent",
        fake_text_splitter_agent,
    )
    monkeypatch.setattr(
        manager,
        "get_evaluator_agent",
        fake_evaluator_agent,
    )
    monkeypatch.setattr(
        manager.Runner,
        "run",
        staticmethod(fake_run),
    )
    caplog.set_level(logging.INFO, logger=manager.__name__)

    result = asyncio.run(
        manager.split_text(sensitive_payload, object(), attempts_max=2),
    )

    assert result is not None
    assert calls == {"text-splitter": 2, "evaluator": 2}
    assert "Input item count" in caplog.text
    assert sensitive_payload not in caplog.text
    assert sensitive_topic not in caplog.text
    assert sensitive_feedback_retry not in caplog.text
    assert sensitive_feedback_final not in caplog.text
    assert sensitive_next_input not in caplog.text
    assert "super-secret-value" not in caplog.text
