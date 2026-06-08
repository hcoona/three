import os  # noqa: D100

import openai
import streamlit as st
from dotenv import load_dotenv
from llm_text_splitter import split_text

load_dotenv()

openai_client = openai.OpenAI(
    api_key=os.getenv("LITELLM_API_KEY"),
    base_url=os.getenv("LITELLM_API_BASE"),
)

st.title("LLM Text Splitter")
st.write("This is a simple app to demonstrate the LLM Text Splitter package.")

st.header("Input Text")

with st.form(key="text"):
    model_option = st.selectbox(
        label="Select the model",
        options=[
            "gpt-4o-mini",
            "gpt-4o",
            "gpt-4.1-nano",
            "gpt-4.1-mini",
            "gpt-4.1",
        ],
        index=0,
        help="The model to use for text splitting. The default is gpt-4o-mini.",
    )

    text = st.text_area(
        key="text_input",
        label="Input Text",
        placeholder=(
            "Input your text here. The text must be pre-split into utterances."
        ),
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

    split_text_result = split_text(
        utterances=utterances,
        openai_client=openai_client,
        model_name=model_option,
    )

    for topic in split_text_result.topics:
        st.subheader(f"{topic.topic}: [{','.join(map(str, topic.indices))}]")
        for i in topic.indices:
            st.write(utterances[i])
