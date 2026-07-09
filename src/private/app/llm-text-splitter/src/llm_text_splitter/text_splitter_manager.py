"""This module provides a text splitter that uses Agents to identify topic boundaries in a document."""  # noqa: E501

import logging

from agents import (
    Runner,
    TResponseInputItem,
)
from openai import AsyncOpenAI

from .text_splitter_agent import (
    SegmentedTopicList,
    get_text_splitter_agent,
)
from .text_splitter_evaluation_agent import (
    EvaluationResult,
    get_evaluator_agent,
)

_MODELS = [
    # gpt-4.1-nano is too weak to do the segmentation.
    "gpt-4o-mini",
    "gpt-4.1-mini",
]


def _validate_indices(
    utterances: list[str], segmented_topics: SegmentedTopicList
) -> str | None:
    """Validate the indices of the segmented topics."""
    # Check the indices is starting from 0 and ending with the last utterance,
    # all indices are consecutive. The merged indices should be range from 0 to the last utterance.  # noqa: E501
    #
    # Generate the error message but not interrupt the program. The error message  # noqa: E501
    # should be helpful for debugging.
    #
    # First, merge all parsed topics into a single list of indices.
    merged_indices = []
    for topic in segmented_topics.topics:
        merged_indices.extend(topic.indices)

    # Then, check if the merged indices are consecutive from 0 to len(utterances) - 1.  # noqa: E501
    # 1. Length of merged indices should be equal to the length of utterances.
    # 2. The merged indices should be equal to range(0, len(utterances)).
    if len(merged_indices) != len(utterances):
        return "Ensure the length of the indices is equal to the length of utterances."  # noqa: E501
    if sorted(merged_indices) != list(range(len(utterances))):
        return f"Ensure the indices are consecutively starting from 0 and ending with {len(utterances) - 1}."  # noqa: E501

    return None


async def split_text(
    text: str, openai_client: AsyncOpenAI, attempts_max: int = 3
) -> SegmentedTopicList | None:
    """Split the text into segments using the text splitter agent."""
    logger = logging.getLogger(__name__)

    utterances = [par.strip() for par in text.split("\n") if par.strip()]
    document = "\n".join([f"{i}: {u}" for i, u in enumerate(utterances)])

    final_output: SegmentedTopicList | None = None
    good_flag = False

    evaluator_agent = get_evaluator_agent(
        model=_MODELS[0],
        openai_client=openai_client,
    )

    for model_index in range(len(_MODELS)):
        text_splitter_agent = get_text_splitter_agent(
            model=_MODELS[model_index],
            openai_client=openai_client,
        )

        input_items: list[TResponseInputItem] = [
            {"content": document, "role": "user"},
        ]

        for i in range(attempts_max):
            logger.info(
                "Round %s - Model: %s - Input item count: %s",
                i,
                _MODELS[model_index],
                len(input_items),
            )

            text_splitter_run_result = await Runner.run(
                text_splitter_agent, input=input_items
            )

            segmented_topics = text_splitter_run_result.final_output
            logger.info(
                "Round %s - Model: %s - Segmentation topic count: %s",
                i,
                _MODELS[model_index],
                len(segmented_topics.topics),
            )

            validation_result = _validate_indices(utterances, segmented_topics)
            if validation_result:
                input_items.append(
                    {
                        "content": f"Feedback: {validation_result}",
                        "role": "user",
                    }
                )
                logger.info(
                    "Round %s - Model: %s - Validation failed: %s",
                    i,
                    _MODELS[model_index],
                    validation_result,
                )
                continue

            input_items = text_splitter_run_result.to_input_list()
            final_output = segmented_topics

            logger.info(
                "Round %s - Model: %s - Message: Evaluating segmentation...",
                i,
                _MODELS[model_index],
            )

            evaluation_run_result = await Runner.run(
                evaluator_agent,
                input=input_items,
            )

            evaluation_result: EvaluationResult = (
                evaluation_run_result.final_output
            )
            logger.info(
                "Round %s - Model: %s - Evaluation score: %s",
                i,
                _MODELS[model_index],
                evaluation_result.score,
            )
            if evaluation_result.score > 0.9:  # noqa: PLR2004
                good_flag = True
                break

            input_items.append(
                {
                    "content": f"Feedback: {evaluation_result.feedback}",
                    "role": "user",
                }
            )

        if good_flag:
            break

        # If the segmentation is still not good enough, try the next model.

    if not good_flag:
        return None

    return final_output
