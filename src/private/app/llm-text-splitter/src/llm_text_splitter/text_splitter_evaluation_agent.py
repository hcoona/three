"""This module provides a evaluator that uses LLMs to evaluate the segmentation of a document."""  # noqa: E501

from agents import Agent, OpenAIChatCompletionsModel
from openai import AsyncOpenAI
from pydantic import BaseModel, Field

_PROMPT = """The following document has been segmented into topics by another language model. Based on the original content and the segmented result, identify only the concrete suggestions for improvement. Do not give general comments, or praise. Focus strictly on actionable feedback, such as issues with topic coherence, segmentation granularity, or structural breaks (e.g., splitting code blocks or lists).

Return only the suggestions for how to revise the segmentation, phrased as direct instructions.
"""  # noqa: E501


class EvaluationResult(BaseModel):
    """Data model for evaluation result."""

    score: float = Field(
        description="Score of the evaluation. 0=poor, 1=excellent."
    )
    feedback: str = Field(
        description="Feedback for improving the segmentation."
    )


def get_evaluator_agent(model: str, openai_client: AsyncOpenAI) -> Agent:
    """Get the evaluator agent."""
    return Agent(
        name="TextSplitterEvaluatorAgent",
        instructions=_PROMPT,
        model=OpenAIChatCompletionsModel(
            model=model,
            openai_client=openai_client,
        ),
        output_type=EvaluationResult,
    )
