import asyncio
import os

import openai
import streamlit as st
from agents import (
    set_default_openai_api,
    set_tracing_disabled,
)
from dotenv import load_dotenv
from llm_text_splitter.text_splitter_manager import split_text

load_dotenv()

set_default_openai_api("chat_completions")
set_tracing_disabled(disabled=True)

openai_client = openai.AsyncOpenAI(
    api_key=os.getenv("LITELLM_API_KEY"),
    base_url=os.getenv("LITELLM_API_BASE"),
)

st.title("LLM Text Splitter (Agentic Version)")
st.write("This is a simple app to demonstrate the LLM Text Splitter package.")

st.header("Input Text")

with st.form(key="text"):
    text = st.text_area(
        key="text_input",
        label="Input Text",
        placeholder="Input your text here. The text must be pre-splitted into utterances.",
        height=400,
    )

    utterances = [par.strip() for par in text.split("\n") if par.strip()]
    if len(utterances) == 0:
        st.warning("Please enter some text.")
    elif len(utterances) == 1:
        st.warning("Please enter more than one utterance.")

    submit_button = st.form_submit_button(label="Submit")

if submit_button:
    st.write("Processing...")

    st.header("Output Text")

    split_text_result = asyncio.run(
        split_text(
            text=text,
            openai_client=openai_client,
        )
    )

    for topic in split_text_result.topics:
        st.subheader(f"{topic.topic}: [{','.join(map(str, topic.indices))}]")
        for i in topic.indices:
            st.write(utterances[i])
