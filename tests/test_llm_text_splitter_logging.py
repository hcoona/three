"""Regression tests for LLM text splitter logging."""

from __future__ import annotations

import asyncio
import logging
import sys
import types
from pathlib import Path
from typing import Any


class _OpenAIStub:
    """Placeholder OpenAI client type for import-time annotations."""


class _AgentStub:
    """Placeholder agents.Agent implementation."""

    def __init__(self, *_args: object, **_kwargs: object) -> None:
        pass


class _OpenAIChatCompletionsModelStub:
    """Placeholder agents model implementation."""

    def __init__(self, *_args: object, **_kwargs: object) -> None:
        pass


class _RunnerStub:
    """Placeholder agents.Runner implementation patched by the test."""


class _BaseModelStub:
    """Minimal pydantic.BaseModel replacement for this isolated test."""

    def __init__(self, **kwargs: object) -> None:
        for key, value in kwargs.items():
            setattr(self, key, value)

    def __repr__(self) -> str:
        return f"{type(self).__name__}({self.__dict__!r})"

    __str__ = __repr__


def _field_stub(*_args: object, **_kwargs: object) -> None:
    return None


openai_stub = types.ModuleType("openai")
openai_stub.OpenAI = _OpenAIStub
openai_stub.AsyncOpenAI = _OpenAIStub
sys.modules.setdefault("openai", openai_stub)

agents_stub = types.ModuleType("agents")
agents_stub.Agent = _AgentStub
agents_stub.OpenAIChatCompletionsModel = _OpenAIChatCompletionsModelStub
agents_stub.Runner = _RunnerStub
agents_stub.TResponseInputItem = dict[str, str]
sys.modules.setdefault("agents", agents_stub)

pydantic_stub = types.ModuleType("pydantic")
pydantic_stub.BaseModel = _BaseModelStub
pydantic_stub.Field = _field_stub
sys.modules.setdefault("pydantic", pydantic_stub)

REPO_ROOT = Path(__file__).parents[1]
LLM_TEXT_SPLITTER_SRC = REPO_ROOT / "src/private/app/llm-text-splitter/src"
sys.path.insert(0, str(LLM_TEXT_SPLITTER_SRC))

from llm_text_splitter import text_splitter_manager as manager  # noqa: E402
from llm_text_splitter.text_splitter_agent import SegmentedTopic  # noqa: E402


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
    """split_text must not write user-provided payload text to logs."""
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
        raising=False,
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
