import asyncio
import logging
import os
import time

import agents
import cohere
import openai
import psycopg
import streamlit as st
from agents import (
    set_default_openai_api,
    set_tracing_disabled,
)
from dotenv import load_dotenv
from pgvector.psycopg import register_vector_async
from psycopg import sql
from psycopg.rows import dict_row
from rag_postgresql_openai.agents.query_rewriter_agent import (
    RewrittenQueries,
    get_query_rewriter_agent,
)

_TOP_N_VECTOR_SEARCH = 10
_TOP_N_BM25_SEARCH = 10
_TOP_N_RRF = 30
_TOP_N_RERANK = 15


async def search_embedding(
    aconn: psycopg.AsyncConnection,
    query_embedding: list[float],
    top_k: int = 10,
    probe: int = 10,
) -> list[dict]:
    """
    This function performs a vector search in the PostgreSQL database using the provided query embedding.

    :param aconn: The PostgreSQL asynchronous connection object.
    :param query_embedding: The embedding of the query to search for.
    :param probe: The number of nearest neighbors to return.
    :return: A list of dictionaries containing the search results.
    """
    async with aconn.cursor(row_factory=dict_row) as cursor:
        await cursor.execute(
            sql.SQL("SET LOCAL vchordrq.probes = {};").format(sql.Literal(probe or ""))
        )
        await cursor.execute(
            """
            SELECT n.id, n.content, n.metadata FROM node n
            JOIN node_embedding ne ON n.id = ne.node_id
            ORDER BY ne.embedding <=> %s::vector
            LIMIT %s
            """,
            (query_embedding, top_k),
        )
        return await cursor.fetchall()


async def search_bm25(
    aconn: psycopg.AsyncConnection,
    query: str,
    top_k: int = 4,
) -> list[dict]:
    """
    This function performs a BM25 search in the PostgreSQL database using the provided query.

    :param aconn: The PostgreSQL asynchronous connection object.
    :param query: The query to search for.
    :param top_k: The number of nearest neighbors to return.
    :return: A list of dictionaries containing the search results.
    """
    async with aconn.cursor(row_factory=dict_row) as cursor:
        await cursor.execute(
            """
            SELECT
                n.id, n.content, n.metadata,
                ne.embedding <&> to_bm25query('node_embedding_bm25_bm25vector_idx', tokenize(%s, 'en_default')) AS rank
            FROM node n
            JOIN node_embedding_bm25 ne ON n.id = ne.node_id
            ORDER BY rank
            LIMIT %s
            """,
            (query, top_k),
        )
        return await cursor.fetchall()


def weighted_rrf(
    node_ids_list: list[list[int]], weights: list[float], k: int = 60
) -> dict[int, float]:
    """
    This function performs a weighted reciprocal rank fusion (RRF) on the provided node IDs and weights.

    :param node_ids_list: A list of lists containing node IDs for each search result.
    :param weights: A list of weights corresponding to each list of node IDs.
    :param k: The constant used in the RRF formula.
    :return: A list of dictionaries containing the fused results.
    """
    scores = {}
    for channel_index, node_ids in enumerate(node_ids_list):
        weight = weights[channel_index]
        for rank, node_id in enumerate(node_ids):
            if node_id not in scores:
                scores[node_id] = 0.0
            scores[node_id] += weight / (k + rank)
    return scores


async def rag_main(question: str) -> str:
    """
    This function is the main entry point for the RAG system.
    It takes a question as input and returns an answer based on the provided documents.

    TODO(shuaizhang): Triage and split the question into multiple sub-questions.

    1. Rewrite & expand the question.
    2. Vector search the database with the origin question.
    3. BM25 search the database with the rewritten and expanded question.
    4. Fuse & re-rank the results from the vector search and BM25 search.
    5. Use the re-ranked results to generate an answer using OpenAI's API.
    """

    openai_client = openai.AsyncOpenAI(
        api_key=os.getenv("LITELLM_API_KEY"),
        base_url=os.getenv("LITELLM_API_BASE"),
    )

    cohere_client = cohere.AsyncClientV2(
        api_key=os.getenv("LITELLM_API_KEY"),
        base_url=os.getenv("LITELLM_API_BASE"),
    )

    embedding_response = await openai_client.embeddings.create(
        model=os.getenv("EMBEDDING_MODEL"),
        input=question,
    )
    query_embedding = embedding_response.data[0].embedding

    url = f"postgresql://{os.getenv('POSTGRES_USER')}:{os.getenv('POSTGRES_PASSWORD')}@localhost:5432/{os.getenv('POSTGRES_DB')}"

    query_rewriter_agent = get_query_rewriter_agent(
        model="gpt-4o-mini",
        openai_client=openai_client,
        language="English",
    )

    with st.spinner("Rewriting question..."):
        start = time.monotonic_ns()
        result = await agents.Runner.run(
            query_rewriter_agent,
            question,
        )

    with st.expander(
        f"Rewritten questions ({(time.monotonic_ns() - start) / 1_000_000}ms):"
    ):
        st.markdown(
            "\n".join(
                ["1. " + q for q in result.final_output_as(RewrittenQueries).queries]
            )
        )

    aconn = await psycopg.AsyncConnection.connect(url)
    async with aconn:
        await register_vector_async(aconn)

        with st.spinner("Vector searching..."):
            start = time.monotonic_ns()

            embedding_nodes = await search_embedding(
                aconn=aconn,
                query_embedding=query_embedding,
                top_k=_TOP_N_VECTOR_SEARCH,
                probe=10,
            )

        with st.expander(
            f"Vector search results ({(time.monotonic_ns() - start) / 1_000_000}ms):"
        ):
            st.table(
                {
                    "Index": [i for i in range(len(embedding_nodes))],
                    "Document": [node["content"] for node in embedding_nodes],
                    "Metadata": [node["metadata"] for node in embedding_nodes],
                }
            )

        # Perform BM25 search with each of the rewritten question.

        with st.spinner("BM25 searching..."):
            start = time.monotonic_ns()

            bm25_nodes_list = []
            for q in result.final_output.queries:
                bm25_nodes = await search_bm25(
                    aconn=aconn,
                    query=q,
                    top_k=_TOP_N_BM25_SEARCH,
                )
                bm25_nodes_list.append(bm25_nodes)

        bm25_deduplicated_flatten_nodes = []
        for bm25_nodes in bm25_nodes_list:
            node_ids = set()
            bm25_deduplicated_flatten_nodes.extend(
                [
                    node
                    for node in bm25_nodes
                    if node["id"] not in node_ids and not node_ids.add(node["id"])
                ]
            )

        with st.expander(
            f"BM25 search results ({(time.monotonic_ns() - start) / 1_000_000}ms):"
        ):
            st.table(
                {
                    "Index": [i for i in range(len(bm25_deduplicated_flatten_nodes))],
                    "Document": [
                        node["content"] for node in bm25_deduplicated_flatten_nodes
                    ],
                    "Metadata": [
                        node["metadata"] for node in bm25_deduplicated_flatten_nodes
                    ],
                }
            )

    node_ids_list: list[list[int]] = [[node["id"] for node in embedding_nodes]] + [
        [node["id"] for node in bm25_nodes] for bm25_nodes in bm25_nodes_list
    ]

    total_node_count = sum(len(node_ids) for node_ids in node_ids_list)
    weights = [
        (total_node_count / len(node_ids)) if node_ids else 0.0
        for node_ids in node_ids_list
    ]

    rrf_scores = weighted_rrf(
        node_ids_list=node_ids_list,
        weights=weights,
    )

    id_to_node_map: dict[int, dict] = {}
    for node in embedding_nodes + bm25_deduplicated_flatten_nodes:
        id_to_node_map[node["id"]] = node

    rrf_nodes = sorted(
        id_to_node_map.values(),
        key=lambda node: rrf_scores.get(node["id"], 0.0),
        reverse=True,
    )[:_TOP_N_RRF]

    with st.expander("RRF results:"):
        st.table(
            {
                "Index": [i for i in range(len(rrf_nodes))],
                "Document": [node["content"] for node in rrf_nodes],
                "Metadata": [node["metadata"] for node in rrf_nodes],
            }
        )

    # jina-reranker-v2-base-multilingual context is only 1024 tokens.
    #
    # Alibaba-NLP/gte-multilingual-reranker-base support text lengths up to 8192 tokens.
    #
    # Rerank the documents using Cohere's API.
    with st.spinner("Re-ranking..."):
        start = time.monotonic_ns()
        rerank_response: cohere.V2RerankResponse = await cohere_client.rerank(
            model=os.getenv("RERANKING_MODEL"),
            query=question,
            documents=[node["content"] for node in rrf_nodes],
            top_n=_TOP_N_RERANK,
            return_documents=False,
        )

    with st.expander(
        f"Re-ranked documents ({(time.monotonic_ns() - start) / 1_000_000} ms):"
    ):
        st.table(
            {
                "Index": [r.index for r in rerank_response.results],
                "Document": [
                    rrf_nodes[r.index]["content"] for r in rerank_response.results
                ],
                "Metadata": [
                    rrf_nodes[r.index]["metadata"] for r in rerank_response.results
                ],
                "Score": [r.relevance_score for r in rerank_response.results],
            }
        )

    # Use the re-ranked documents to generate an answer.
    # Write the document content and metadata to the prompt.
    with st.spinner("Generating answer..."):
        start = time.monotonic_ns()
        formatted_documents = "\n\n-----\n\n".join(
            [
                f"{rrf_nodes[rerank_result.index]['content']}\nMetadata: {rrf_nodes[rerank_result.index]['metadata']}"
                for rerank_result in rerank_response.results
            ]
        )
        answer_response = await openai_client.chat.completions.create(
            model="gpt-4o-mini",
            messages=[
                {
                    "role": "user",
                    "content": formatted_documents
                    + "\n\n-----\n\n"
                    + """Please answer the question in details **only based on the content of the provided documents**. Do not use any pre-trained knowledge or external information.

Your answer must follow these rules:
- Format your response in **Markdown**;
- For **every statement, fact, or paragraph** you write, **immediately cite its source** at the end, in the form: (use document name, which is the combination of chapter title, section title, and subsection title)；
  - If your answer has multiple parts, each part **must** have its own citation directly following it;
  - Do **NOT** combine sources at the end—**each statement or claim must be followed by its specific source**;
- If the answer is **not found in the documents**, respond with: **"I don't know."**

Make sure to read all documents carefully before answering.\n\n"""
                    + f"Question: {question}",
                }
            ],
            temperature=0,
        )

    st.write(f"Answer generated in {(time.monotonic_ns() - start) / 1_000_000} ms.")
    return answer_response.choices[0].message.content.strip()


load_dotenv()

set_default_openai_api("chat_completions")
set_tracing_disabled(disabled=True)

logging.basicConfig(level=logging.INFO)
logging.getLogger("httpx").setLevel(logging.WARNING)

st.set_page_config(
    page_title="Q&A with provided documents, backed by PostgreSQL",
    page_icon="🦙",
    layout="centered",
    menu_items=None,
)

st.title("Q&A with provided documents, backed by PostgreSQL")
st.write(
    "This is a demo of a question-answering system that uses PostgreSQL as the backend. "
    "You can ask questions and get answers based on the provided documents."
)

if prompt := st.chat_input(
    placeholder="Type your question here...",
    key="query_input",
):
    question = prompt

    with st.chat_message("user"):
        st.markdown(question)

    with st.chat_message("assistant"):
        st.markdown("Thinking...")

    answer = asyncio.run(rag_main(question=question))

    with st.chat_message("assistant"):
        st.markdown(answer)
