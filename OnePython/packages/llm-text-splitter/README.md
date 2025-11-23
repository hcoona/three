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

有什么 NLP 的方法可以将输入的文本切分自然段吗，还是说这不需要 NLP，按照换行切分就行？

我的输入可能是中文也可能是英文。

输入可能有  3 种情况：

-----

情况1

段落1

段落2

-----

情况2

段落1内容1
段落1内容2

段落2内容1
段落2内容2
段落2内容3

-----

情况 3

段落1
段落2
