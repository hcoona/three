"""A small XML element tree with a deterministic serializer.

The forward converter builds MusicXML with these elements instead of
``xml.etree`` so that the output bytes depend only on the tree: attributes
keep their insertion order, indentation is fixed, and text escapes carriage
returns, which an XML parser would otherwise normalize to line feeds.
"""

from __future__ import annotations

from dataclasses import dataclass, field

_INDENT = "  "
_TEXT_ESCAPES = str.maketrans(
    {"&": "&amp;", "<": "&lt;", ">": "&gt;", "\r": "&#13;"}
)
_ATTRIBUTE_ESCAPES = str.maketrans(
    {
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "\t": "&#9;",
        "\n": "&#10;",
        "\r": "&#13;",
    },
)


@dataclass(slots=True)
class Element:
    """An XML element with ordered attributes and either text or children."""

    tag: str
    attributes: dict[str, str] = field(default_factory=dict)
    text: str | None = None
    children: list[Element] = field(default_factory=list)

    def add(
        self,
        tag: str,
        text: str | None = None,
        attributes: dict[str, str] | None = None,
    ) -> Element:
        """Append a new child element and return it."""
        child = Element(tag, dict(attributes or {}), text)
        self.children.append(child)
        return child

    def append(self, child: Element) -> Element:
        """Append an existing element and return it."""
        self.children.append(child)
        return child


def _write(element: Element, depth: int, out: list[str]) -> None:
    indent = _INDENT * depth
    attributes = "".join(
        f' {name}="{value.translate(_ATTRIBUTE_ESCAPES)}"'
        for name, value in element.attributes.items()
    )
    if element.children:
        out.append(f"{indent}<{element.tag}{attributes}>\n")
        for child in element.children:
            _write(child, depth + 1, out)
        out.append(f"{indent}</{element.tag}>\n")
    elif element.text is not None:
        text = element.text.translate(_TEXT_ESCAPES)
        out.append(
            f"{indent}<{element.tag}{attributes}>{text}</{element.tag}>\n"
        )
    else:
        out.append(f"{indent}<{element.tag}{attributes}/>\n")


def serialize(root: Element, doctype: str) -> bytes:
    """Return a UTF-8 XML document with a declaration and a DOCTYPE.

    Args:
        root: The root element.
        doctype: The full ``<!DOCTYPE ...>`` declaration.

    Returns:
        The encoded document, which ends with a line feed.
    """
    out = ['<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n']
    out.append(f"{doctype}\n")
    _write(root, 0, out)
    return "".join(out).encode("utf-8")
