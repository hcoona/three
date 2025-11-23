"""This module provides a query rewriter agent that uses LLMs to rewrite queries for better performance."""

from agents import Agent, OpenAIChatCompletionsModel
from openai import AsyncOpenAI
from pydantic import BaseModel, Field

_PROMPT = """Rewrite the following user question into a concise keyword-style search query that captures the core meaning and includes relevant synonyms or related terms. The output should be suitable for full-text search (e.g., BM25-based retrieval). The output must be in {} language."""


class RewrittenQueries(BaseModel):
    """Data model for rewritten queries."""

    queries: list[str] = Field(
        description="A list of rewritten queries. The number of queries should be less than or equal to 5.",
    )


def get_query_rewriter_agent(model: str, openai_client: AsyncOpenAI, language) -> Agent:
    """Get the query rewriter agent."""
    return Agent(
        name="UserQuestionRewriterAgent",
        instructions=_PROMPT.format(language),
        model=OpenAIChatCompletionsModel(
            model=model,
            openai_client=openai_client,
        ),
        output_type=RewrittenQueries,
    )
