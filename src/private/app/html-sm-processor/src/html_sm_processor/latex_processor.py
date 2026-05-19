"""This module provides functions to convert LaTeX to PNG."""

import logging
import pathlib

import matplotlib.pyplot as plt
import niquests as requests

from .constants import _DEFAULT_DPI


def init_matplotlib(use_tex: bool = False) -> None:
    """
    Initialize Matplotlib with a non-interactive backend.
    This is necessary for rendering images without displaying them.
    """
    if use_tex:
        import matplotlib

        matplotlib.use("pgf")

        plt.rcParams.update(
            {
                "font.family": "serif",
                "font.serif": ["Times New Roman"],
                "font.cursive": [],
                "pgf.preamble": r"\usepackage{amsmath}",
            }
        )


def matplotlib_render_math_to_png(
    latex_string: str,
    output_path: pathlib.Path,
    is_inline: bool | None = None,
    use_tex: bool = False,
) -> bool:
    """
    Renders a LaTeX string to a PNG image using Matplotlib.
    Adjusts figure size to tightly bound the rendered math.

    :param latex_string: The LaTeX string to render.
    :param output_path: The path to save the rendered PNG image.
    :param is_inline: If True, render as inline math; if False, render as block math.
                      If None, auto-detect based on LaTeX string. Detection failure defaults to inline.
    :return: True if rendering was successful, False otherwise.
    """
    if not latex_string.strip():
        logging.warning("Empty LaTeX string provided for rendering.")
        return False

    if latex_string.startswith(r"\(") and latex_string.endswith(r"\)"):
        latex_string = "$" + latex_string[2:-2].strip() + "$"
        is_inline = True
    elif latex_string.startswith(r"\[") and latex_string.endswith(r"\]"):
        latex_string = "$" + latex_string[2:-2].strip() + "$"
        is_inline = False
    elif latex_string.startswith("$$") and latex_string.endswith("$$"):
        latex_string = "$" + latex_string[2:-2].strip() + "$"
        is_inline = False
    elif latex_string.startswith("$") and latex_string.endswith("$"):
        is_inline = True
    else:
        latex_string = "$" + latex_string.strip() + "$"
        if is_inline is None:
            # Default to inline if not specified
            is_inline = True

    try:
        plt.rcParams["mathtext.fontset"] = (
            "cm"  # Use Computer Modern fonts for consistency
        )
        plt.rcParams["text.usetex"] = use_tex
        # Start with a tiny figure, it will be adjusted
        fig, ax = plt.subplots(figsize=(0.01, 0.01))

        # Render text and turn off axis
        text_obj = ax.text(
            0, 0, latex_string, fontsize=10, ha="left", va="bottom"
        )
        ax.axis("off")

        # Calculate bounding box and set figure size to content
        fig.canvas.draw()  # Important to draw canvas before getting extent
        bbox = text_obj.get_window_extent().transformed(
            fig.dpi_scale_trans.inverted()
        )

        # Add a small padding
        pad_inches = 0.02 if is_inline else 0.1  # Smaller padding for inline
        fig.set_size_inches(
            bbox.width + 2 * pad_inches, bbox.height + 2 * pad_inches
        )

        # After resizing, the text needs to be repositioned to the new bottom-left (plus padding)
        text_obj.set_position((pad_inches, pad_inches))

        fig.savefig(
            output_path,
            dpi=_DEFAULT_DPI,
            transparent=True,
            bbox_inches="tight",
            pad_inches=0.01,
        )
        plt.close(fig)
        logging.info(f"Rendered math to {output_path}")
        return True
    except Exception as e:
        logging.error(
            f"Failed to render math string '{latex_string[:50]}...' to PNG: {e}"
        )
        if "fig" in locals() and plt.fignum_exists(fig.number):  # type: ignore
            plt.close(fig)  # Ensure figure is closed on error if it was created
        return False


def webtex_render_math_to_png(
    latex_string: str, output_path: pathlib.Path
) -> bool:
    """
    Renders a LaTeX string to a PNG image using WebTeX.

    :param latex_string: The LaTeX string to render.
    :param output_path: The path to save the rendered PNG image.
    :return: True if rendering was successful, False otherwise.
    """
    if latex_string.startswith(r"\(") and latex_string.endswith(r"\)"):
        latex_string = latex_string[2:-2].strip()
    elif latex_string.startswith(r"\[") and latex_string.endswith(r"\]"):
        latex_string = latex_string[2:-2].strip()
    elif latex_string.startswith("$$") and latex_string.endswith("$$"):
        latex_string = latex_string[2:-2].strip()
    elif latex_string.startswith("$") and latex_string.endswith("$"):
        latex_string = latex_string[1:-1].strip()
    else:
        latex_string = latex_string.strip()

    response = requests.get(
        r"https://latex.codecogs.com/png.latex?\dpi{300}" + latex_string
    )
    if response.status_code == 200 and response.content is not None:
        with open(output_path, "wb") as f:
            f.write(response.content)
        logging.info(f"Rendered math with webtex to {output_path}")
        return True
    else:
        logging.error(
            f"Failed to render math string '{latex_string[:50]}...' to PNG: {response.status_code}"
        )
        return False
