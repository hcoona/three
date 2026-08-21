# LLM Text Splitter

This project provides a text splitter based on LLMs.

It will feed the text to LLM with the prompt inspired by the paper _Uncovering the Potential of ChatGPT for Discourse Analysis in Dialogue: An Empirical Study_ and _Improving Long Document Topic Segmentation Models With Enhanced Coherence Modeling_.

1. Split the text into utterances and mark them with indices starting from 0.
2. Ask LLM to identify the topic boundaries.
3. Validate the output and retry if necessary.
4. Ask the LLM to rate the results, and if the rating suggests that the groupings are too aggregated or dispersed, retry the generation using additional prompts.

## Getting Started

Fill the `packages/llm-text-splitter/.env` file with

1. `LITELLM_API_BASE`
2. `LITELLM_API_KEY`

Launch web browser to ask the agent do segmentation task:

```bash
uv run --package llm-text-splitter streamlit run packages/llm-text-splitter/app2.py
```

## Notes

Are there NLP techniques for splitting input text into natural paragraphs, or
is NLP unnecessary when splitting on line breaks is sufficient?

The input may be either Chinese or English.

The input can have three forms:

---

Case 1

Paragraph 1

Paragraph 2

---

Case 2

Paragraph 1, line 1
Paragraph 1, line 2

Paragraph 2, line 1
Paragraph 2, line 2
Paragraph 2, line 3

---

Case 3

Paragraph 1
Paragraph 2
