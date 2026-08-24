"""Entrypoint for the HTML SM Processor package."""

import argparse
import base64
import hashlib
import html
import logging
import pathlib
import urllib.parse

import niquests as requests
from bs4 import BeautifulSoup, Tag
from html_sm_processor.latex_processor import (
    init_matplotlib,
    matplotlib_render_math_to_png,
    webtex_render_math_to_png,
)
from html_sm_processor.specialized_processor import (
    d2l_ai_extract_document,
    d2l_ai_filter_mdl_tab,
)
from html_sm_processor.svg_processor import (
    convert_svg_base64,
    convert_svg_relative_path,
)


def _get_decoded_math_tag_text(tag: Tag) -> str:
    """Get the decoded text from a tag.

    :param tag: The tag to decode.
    :return: The decoded text.
    """
    return html.unescape(tag.get_text(separator="", strip=True)).replace(
        "\n", " "
    )


def _get_png_size(filepath: str) -> tuple[int, int]:
    """Get the size of a PNG file.

    :param filepath: The path to the PNG file.
    :return: A tuple containing the width and height of the PNG file.
    """
    with open(filepath, "rb") as f:  # noqa: PTH123
        # https://www.w3.org/TR/PNG/#5PNG-file-signature
        signature = f.read(8)
        if signature != b"\x89PNG\r\n\x1a\n":
            raise ValueError("Not a valid PNG file")  # noqa: EM101, TRY003

        # https://www.w3.org/TR/png/#11IHDR
        #
        # The IHDR chunk shall be the first chunk in the PNG datastream. It contains:  # noqa: E501
        #
        # Width	4 bytes
        # Height	4 bytes
        # Bit depth	1 byte
        # Color type	1 byte
        # Compression method	1 byte
        # Filter method	1 byte
        # Interlace method	1 byte
        f.read(4)  # Skip the length of the chunk
        chunk_type = f.read(4)
        if chunk_type != b"IHDR":
            raise ValueError("IHDR chunk not found")  # noqa: EM101, TRY003

        width = int.from_bytes(f.read(4), "big")
        height = int.from_bytes(f.read(4), "big")

        return width, height


def _get_str_attr(tag: Tag, name: str) -> str | None:
    value = tag.get(name)
    return value if isinstance(value, str) else None


def main():  # noqa: ANN201, C901, PLR0912, PLR0915
    """Process the HTML file for SuperMemo.

    1. Specialize functions for D2L AI document.
    2. Convert SVG images to PNG.
    3. Render math into PNG.
        1. `<span class="math">`: Inlined math.
        2. `<div class="math">`: Standalone math, need to extract text excluding `<span>` inside.

    TODO(shuaizhang): Fix relative links in the HTML file.
    """  # noqa: E501
    parser = argparse.ArgumentParser(
        description="Process HTML files: convert SVG/Math to PNG and remove specific divs."  # noqa: E501
    )
    parser.add_argument("input", type=str, help="Local file or URL.")
    parser.add_argument(
        "--output_directory",
        type=pathlib.Path,
        default=pathlib.Path("out"),
        help="Path to save the processed HTML file and images.",
    )
    parser.add_argument(
        "--use-tex",
        action="store_true",
        help="Use TeX for rendering math. Default is to use mathlibplot.",
    )
    args = parser.parse_args()

    is_d2l_ai = False
    if args.input.startswith("http://") or args.input.startswith("https://"):
        response = requests.get(args.input)
        response.raise_for_status()
        html_content = response.text
        if html_content is None:
            raise ValueError(f"No HTML content returned from {args.input}")  # noqa: EM102, TRY003

        output_filename = urllib.parse.urlsplit(args.input).path.split("/")[-1]

        if args.input.startswith("https://zh.d2l.ai/"):
            is_d2l_ai = True
    else:
        with open(args.input, encoding="utf-8") as f:  # noqa: PTH123
            html_content = f.read()

        output_filename = pathlib.Path(args.input).name

    input_sha256 = hashlib.sha256(html_content.encode("utf-8")).hexdigest()
    output_filename_without_extension = output_filename.split(".")[0]
    unique_prefix = f"{input_sha256[:8]}_{output_filename_without_extension}"

    output_directory = args.output_directory
    output_directory.mkdir(parents=True, exist_ok=True)

    init_matplotlib(use_tex=args.use_tex)

    soup = BeautifulSoup(html_content, "lxml")

    if is_d2l_ai:
        document = d2l_ai_extract_document(soup=soup)

        d2l_ai_filter_mdl_tab(document=document, tab_name="pytorch")
    else:
        document = soup

    svg_url_images = document.find_all(
        "img", src=lambda src: isinstance(src, str) and src.endswith(".svg")
    )
    for svg_url_image in svg_url_images:
        if not isinstance(svg_url_image, Tag):
            continue
        svg_url = _get_str_attr(svg_url_image, "src")
        if svg_url is None:
            continue
        data = convert_svg_relative_path(
            root_path=args.input, relative_path=svg_url
        )
        if data is None:
            continue

        filename_with_png_extension = svg_url.split("/")[-1].replace(
            ".svg", ".png"
        )
        (output_directory / filename_with_png_extension).write_bytes(data)
        svg_url_image["src"] = filename_with_png_extension

    svg_unique_image_counter = 0
    for svg_data_image in document.find_all(
        "img",
        src=lambda src: isinstance(src, str)
        and src.startswith("data:image/svg+xml;base64"),
    ):
        if not isinstance(svg_data_image, Tag):
            continue
        svg_unique_image_counter += 1

        svg_data = _get_str_attr(svg_data_image, "src")
        if svg_data is None:
            continue
        data = convert_svg_base64(data_base64=svg_data.split(",")[1])
        if data is None:
            continue

        filename_with_png_extension = (
            f"{unique_prefix}_svg_{svg_unique_image_counter}.png"
        )
        (output_directory / filename_with_png_extension).write_bytes(data)
        svg_data_image["src"] = filename_with_png_extension

    inline_math_tags = soup.find_all("span", class_="math")
    for i, math_tag in enumerate(inline_math_tags):
        if not isinstance(math_tag, Tag):
            continue
        latex_string = _get_decoded_math_tag_text(math_tag)
        filename = f"inline_math_{i}.png"
        output_path = output_directory / filename

        if not matplotlib_render_math_to_png(
            latex_string=latex_string,
            output_path=output_path,
            is_inline=True,
            use_tex=args.use_tex,
        ):
            logging.warning(f"Failed to render inline math: {latex_string}")  # noqa: G004
            if not webtex_render_math_to_png(
                latex_string=latex_string,
                output_path=output_path,
            ):
                logging.warning(
                    f"Failed to render inline math with webtex: {latex_string}"  # noqa: G004
                )
                continue

        width, height = _get_png_size(str(output_path))
        png_base64 = base64.b64encode(output_path.read_bytes()).decode("utf-8")

        math_tag.replace_with(
            soup.new_tag(
                "img",
                attrs={
                    "data-latex": latex_string,
                    "width": str(width),
                    "height": str(height),
                    "style": f"BACKGROUND-REPEAT: no-repeat; BACKGROUND-IMAGE: url(data:image/png;base64,{png_base64});",  # noqa: E501
                },
            )
        )
        output_path.unlink()

    standalone_math_tags = soup.find_all("div", class_="math")
    for i, math_tag in enumerate(standalone_math_tags):
        if not isinstance(math_tag, Tag):
            continue
        eqno_span = math_tag.find("span", class_="eqno")
        if eqno_span is not None:
            eqno_span.extract()

        latex_string = _get_decoded_math_tag_text(math_tag)
        filename = f"{unique_prefix}_math_standalone_{i}.png"
        output_path = output_directory / filename

        if not matplotlib_render_math_to_png(
            latex_string=latex_string,
            output_path=output_path,
            is_inline=False,
            use_tex=args.use_tex,
        ):
            logging.warning(f"Failed to render standalone math: {latex_string}")  # noqa: G004
            if not webtex_render_math_to_png(
                latex_string=latex_string,
                output_path=output_path,
            ):
                logging.warning(
                    f"Failed to render standalone math with webtex: {latex_string}"  # noqa: E501, G004
                )
                continue

        new_math_tag = soup.new_tag(math_tag.name or "div")
        new_math_tag.attrs = math_tag.attrs.copy()
        if eqno_span is not None:
            new_math_tag.append(eqno_span)
        new_math_tag.append(soup.new_tag("img", src=filename, alt=latex_string))
        math_tag.replace_with(new_math_tag)

    with open(  # noqa: PTH123
        output_directory / output_filename, mode="w", encoding="utf-8"
    ) as f:
        f.write(str(document))


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)

    main()
