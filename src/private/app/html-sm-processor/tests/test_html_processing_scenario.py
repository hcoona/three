"""Scenario tests for preparing HTML pages for SuperMemo."""

from pathlib import Path

from bs4 import BeautifulSoup
from html_sm_processor.latex_processor import matplotlib_render_math_to_png
from html_sm_processor.specialized_processor import (
    d2l_ai_extract_document,
    d2l_ai_filter_mdl_tab,
)

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

# A trimmed d2l.ai chapter page with framework tabs and site chrome.
D2L_PAGE = """
<html><body>
<div class="side-doc-outline">Outline</div>
<div class="document">
  <h1>Linear Regression<a class="headerlink" href="#lr">¶</a></h1>
  <div class="d2l-tabs">Framework picker</div>
  <p> Open the notebook in SageMaker Studio Lab</p>
  <div><a id="SageMaker_Studio_Lab">Studio Lab</a></div>
  <p>We minimize
    <span class="math">\\(\\sum_i (y_i - \\hat{y}_i)^2\\)</span>.</p>
  <div class="mdl-tabs">
    <div class="mdl-tabs__tab-bar">mxnet pytorch tensorflow</div>
    <div class="mdl-tabs__panel" id="mxnet-1-0">from mxnet import np</div>
    <div class="mdl-tabs__panel" id="pytorch-1-1">import torch</div>
    <div class="mdl-tabs__panel" id="tensorflow-1-2">import tensorflow</div>
    <div class="mdl-tabs__panel" id="paddle-1-3">import paddle</div>
  </div>
  <a href="https://discuss.d2l.ai/t/258">Discussions</a>
</div>
</body></html>
"""


def test_d2l_page_keeps_only_the_lesson_and_selected_framework() -> None:
    """Strip d2l.ai site chrome and keep only the PyTorch code panel."""
    soup = BeautifulSoup(D2L_PAGE, "lxml")

    document = d2l_ai_extract_document(soup=soup)
    d2l_ai_filter_mdl_tab(document=document, tab_name="pytorch")

    text = document.get_text(" ", strip=True)
    assert "Linear Regression" in text
    assert "We minimize" in text
    assert "import torch" in text
    for removed in (
        "¶",
        "Framework picker",
        "Studio Lab",
        "mxnet",
        "tensorflow",
        "paddle",
        "Discussions",
        "Outline",
    ):
        assert removed not in str(soup)


def test_inline_math_renders_to_png_without_network(tmp_path: Path) -> None:
    """Render d2l.ai inline math locally with Matplotlib mathtext."""
    soup = BeautifulSoup(D2L_PAGE, "lxml")
    math = soup.find("span", class_="math")
    assert math is not None
    output = tmp_path / "inline_math_0.png"

    assert matplotlib_render_math_to_png(
        latex_string=math.get_text(), output_path=output, is_inline=True
    )
    assert output.read_bytes().startswith(PNG_SIGNATURE)
