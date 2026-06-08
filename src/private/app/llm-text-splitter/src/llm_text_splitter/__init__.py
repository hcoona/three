"""This module provides a text splitter that uses LLMs to identify topic boundaries in a document."""  # noqa: E501

from typing import cast

import openai
from pydantic import BaseModel, Field

__all__ = ["SegmentedTopic", "SegmentedTopicList", "split_text"]

_SYSTEM_PROMPT = """Please identify several topic boundaries for the following document and each topic consists of several consecutive utterances. You should not split the utterances each into a standalone topic or all into a single topic. Don't break in the middle of a piece of source code or a list."""  # noqa: E501
_DEFAULT_MODEL = "gpt-4o-mini"


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


def split_text(
    utterances: list[str],
    openai_client: openai.OpenAI,
    model_name: str = _DEFAULT_MODEL,
) -> SegmentedTopicList:
    """Split the utterances into segments."""
    document = "\n".join([f"{i}: {u}" for i, u in enumerate(utterances)])

    completion = openai_client.beta.chat.completions.parse(
        model=model_name,
        messages=[
            {"role": "developer", "content": _SYSTEM_PROMPT},
            {"role": "user", "content": document},
        ],
        response_format=SegmentedTopicList,
    )

    segmented_topic_list = cast(
        "SegmentedTopicList", completion.choices[0].message.parsed
    )

    # Check the indices is starting from 0 and ending with the last utterance,
    # all indices are consecutive. The merged indices should be range from 0 to the last utterance.  # noqa: E501
    #
    # Generate the error message but not interrupt the program. The error message  # noqa: E501
    # should be helpful for debugging.
    #
    # First, merge all parsed topics into a single list of indices.
    merged_indices = []
    for topic in segmented_topic_list.topics:
        merged_indices.extend(topic.indices)
    # Then, check if the merged indices are consecutive from 0 to len(utterances) - 1.  # noqa: E501
    # 1. Length of merged indices should be equal to the length of utterances.
    # 2. The merged indices should be equal to range(0, len(utterances)).
    if len(merged_indices) != len(utterances):
        raise ValueError(  # noqa: TRY003
            f"[ERROR] The length of merged indices ({len(merged_indices)}) is not equal to the length of utterances ({len(utterances)}).\n"  # noqa: E501, EM102
        )
    if sorted(merged_indices) != list(range(len(utterances))):
        raise ValueError(  # noqa: TRY003
            f"[ERROR] The merged indices ({sorted(merged_indices)}) are not equal to range(0, {len(utterances)}).\n"  # noqa: E501, EM102
        )

    return segmented_topic_list
