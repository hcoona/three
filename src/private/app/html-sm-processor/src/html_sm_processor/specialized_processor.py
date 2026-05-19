"""This module provides specialized functions to process HTML."""

from typing import Literal

from bs4 import BeautifulSoup, Tag


def d2l_ai_extract_document(soup: BeautifulSoup) -> Tag:
    """
    Extract the main content of the document from the soup.
    """
    document_div = soup.find("div", class_="document")
    if not isinstance(document_div, Tag):
        raise ValueError("No document found in the soup.")

    for headerlink in soup.find_all("a", class_="headerlink"):
        headerlink.decompose()

    n = soup.find("div", class_="d2l-tabs")
    n.decompose() if n else None
    n = soup.find(id="SageMaker_Studio_Lab")
    if n and n.parent:
        n.parent.decompose()
    n = soup.find(string=" Open the notebook in SageMaker Studio Lab")
    n.decompose() if n else None

    n = soup.find("div", class_="side-doc-outline")
    n.decompose() if n else None
    for el in soup.find_all("a", string="Discussions"):
        el.decompose()

    return document_div


def d2l_ai_filter_mdl_tab(
    document: Tag, tab_name: Literal["mxnet", "pytorch", "tensorflow", "paddle"]
) -> None:
    """
    Filter out the mdl-tab elements from the soup.
    """
    for bar in document.find_all("div", class_="mdl-tabs__tab-bar"):
        bar.decompose()

    for panel in document.find_all(
        "div",
        class_="mdl-tabs__panel",
        id=lambda id: id and not id.startswith(tab_name),
    ):
        panel.decompose()
