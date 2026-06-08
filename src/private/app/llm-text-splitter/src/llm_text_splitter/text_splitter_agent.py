"""This module provides a text splitter that uses LLMs to identify topic boundaries in a document."""  # noqa: E501

from agents import Agent, OpenAIChatCompletionsModel
from openai import AsyncOpenAI
from pydantic import BaseModel, Field

_PROMPT = """Please segment the following document into coherent topical sections. Each section should contain several consecutive utterances that discuss a related subject. Do not create topics that consist of only a single utterance, and do not group the entire document into just one topic. When segmenting, make sure not to break in the middle of a source code block or a list — these should remain intact within a single topic."""  # noqa: E501


class SegmentedTopic(BaseModel):
    """Data model for segmented topic."""

    topic: str = Field(description="Topic description.")
    indices: list[int] = Field(
        description="Index of the consecutive utterances within the topic, even if there is only one topic."  # noqa: E501
    )


class SegmentedTopicList(BaseModel):
    """Data model for a list of segmented topics."""

    topics: list[SegmentedTopic] = Field(
        description="A list of segmented topics."
    )


def get_text_splitter_agent(model: str, openai_client: AsyncOpenAI) -> Agent:
    """Get the text splitter agent."""
    return Agent(
        name="TextSplitterAgent",
        instructions=_PROMPT,
        model=OpenAIChatCompletionsModel(
            model=model,
            openai_client=openai_client,
        ),
        output_type=SegmentedTopicList,
    )
