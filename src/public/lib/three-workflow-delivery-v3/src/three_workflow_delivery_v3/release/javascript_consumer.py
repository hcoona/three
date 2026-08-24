"""Tree-sitter admission scanner for the supported JavaScript subset."""

from __future__ import annotations

import re
from collections.abc import Callable, Iterator
from dataclasses import dataclass
from importlib.metadata import PackageNotFoundError, version
from typing import Literal, NoReturn

import tree_sitter_javascript
import tree_sitter_typescript
from tree_sitter import Language, Node, Parser

from .consumer_policy import (
    CONSUMER_PACKAGE,
    JAVASCRIPT_AST_DEPTH_LIMIT,
    JAVASCRIPT_AST_NODE_LIMIT,
    JAVASCRIPT_SOURCE_BYTE_LIMIT,
    NODE_DEPENDENCY_FIELDS,
    TREE_SITTER_JAVASCRIPT_VERSION,
    TREE_SITTER_TYPESCRIPT_VERSION,
    TREE_SITTER_VERSION,
)

JavaScriptLanguage = Literal["javascript", "typescript"]
ManagerReference = Callable[[str, tuple[str, ...] | None], bool]
Nodes = tuple[Node, ...]
_ShellOption = Literal["absent", "disabled", "enabled", "unknown"]
UNSUPPORTED_DIAGNOSTIC = "relevant unsupported JavaScript consumer flow"
_ABSENT, _POSSIBLE, _PRESENT = 0, 1, 2

_LANGUAGES = {
    "javascript": Language(tree_sitter_javascript.language()),
    "typescript": Language(tree_sitter_typescript.language_typescript()),
}
_VERSIONS = {
    "tree-sitter": TREE_SITTER_VERSION,
    "tree-sitter-javascript": TREE_SITTER_JAVASCRIPT_VERSION,
    "tree-sitter-typescript": TREE_SITTER_TYPESCRIPT_VERSION,
}
_PACKAGE_BYTES = CONSUMER_PACKAGE.encode()
_PACKAGE = re.compile(
    rb"(?<![A-Za-z0-9_])"
    + re.escape(_PACKAGE_BYTES)
    + rb"(?=$|[@/\s\"'`,;\])}])"
)
_APIS = {"exec", "execSync", "spawn", "spawnSync", "execFile", "execFileSync"}
_CP_MODULES = {"child_process", "node:child_process"}
_MODULE_MODULES = {"module", "node:module"}
_MEMBERS = {"member_expression", "subscript_expression"}
_CALLS = {"call_expression", "new_expression"}
_ASSIGNMENTS = {"assignment_expression", "augmented_assignment_expression"}
_ASSIGNMENT_PATTERNS = {"assignment_pattern", "object_assignment_pattern"}
_LITERALS = {"string", "string_fragment", "template_string"}
_NAMES = {"identifier", "property_identifier"} | {
    "shorthand_property_identifier",
    "shorthand_property_identifier_pattern",
}
_REFERENCES = {"identifier", "shorthand_property_identifier"}
_MODULE_NODES = {"import_statement", "export_statement", "import_alias"}
_FUNCTION_NODES = {
    "function_declaration",
    "function_expression",
} | {"generator_function", "generator_function_declaration", "arrow_function"}
_BARRIER_NODES = _FUNCTION_NODES - {"function_declaration"}
_BARRIER_NODES |= {"class", "class_declaration", "internal_module"}
_BARRIER_NODES |= {"method_definition", "module"}
_TYPE_ONLY = (
    {"type_alias_declaration", "interface_declaration"}
    | {"ambient_declaration", "declare_statement"}
    | {"type_annotation", "type_arguments", "type_parameters"}
    | {"predefined_type", "literal_type", "type_query"}
    | {"lookup_type", "index_type_query"}
)
_RUNTIME_SKIP = _TYPE_ONLY | {"comment"}
_WRAPPERS = {
    "parenthesized_expression",
    "as_expression",
    "satisfies_expression",
} | {"type_assertion", "non_null_expression", "instantiation_expression"}
_BUILTINS = (
    {"require": "loader", "import": "loader"}
    | {"eval": "dynamic", "Function": "dynamic"}
    | {
        "process": "process",
        "globalThis": "globalThis",
        "module": "commonjs-module",
    }
    | {"Object": "Object", "Reflect": "Reflect"}
)
_OBJECT_NAMESPACES = {"Object", "Reflect"}
_INERT_TAGS = {
    None,
    "commonjs-module",
    "other",
    "Object",
    "Reflect",
    "globalThis",
}
_SIMPLE_ESCAPES = dict(zip("bfnrtv", "\b\f\n\r\t\v", strict=True))
_HIGH, _LOW, _SURROGATE_END, _MAX_CODE_POINT = 0xD800, 0xDC00, 0xE000, 0x10FFFF
_ESCAPE = re.compile(
    r"""\\(?:0[0-7]{1,2}|[1-3][0-7]{0,2}|[4-7][0-7]?"""
    r"""|0(?![0-9])|[bfnrtv'"\\/`]"""
    r"""|x[0-9A-Fa-f]{2}"""
    r"""|u[Dd][89AaBb][0-9A-Fa-f]{2}\\u[Dd][CcDdEeFf][0-9A-Fa-f]{2}"""
    r"""|u[0-9A-Fa-f]{4}|u\{[0-9A-Fa-f]{1,6}\}"""
    r"""|\r\n|[\n\r\u2028\u2029]|[^0-9xu\r\n\u2028\u2029])"""
)
_IDENTIFIER_ESCAPE = re.compile(
    r"""\\(?:u[Dd][89AaBb][0-9A-Fa-f]{2}"""
    r"""\\u[Dd][CcDdEeFf][0-9A-Fa-f]{2}"""
    r"""|u[0-9A-Fa-f]{4}|u\{[0-9A-Fa-f]{1,6}\})"""
)
_PROJECTIONS = {
    ("import-meta", "resolve"): "resolve",
    ("import-meta", "url"): "other",
    ("module-namespace", "createRequire"): "factory",
    ("module-namespace", "require"): "other",
    ("commonjs-module", "createRequire"): "other",
    ("commonjs-module", "require"): "loader",
    ("process", "getBuiltinModule"): "builtin",
    ("globalThis", "eval"): "dynamic",
    ("globalThis", "Function"): "dynamic",
    ("loader", "resolve"): "resolve",
    ("Object", "assign"): "writer",
    ("Object", "defineProperty"): "writer",
    ("Object", "defineProperties"): "writer",
    ("Reflect", "set"): "writer",
    ("Reflect", "defineProperty"): "writer",
    ("Reflect", "deleteProperty"): "writer",
}


@dataclass(frozen=True, slots=True)
class _Symbol:
    kind: Literal["const", "mutable"]
    initializer: Node | None = None
    member: str | None = None
    tag: str | None = None


def _field(node: Node | None, name: str) -> Node | None:
    return node.child_by_field_name(name) if node is not None else None


def _children(node: Node) -> tuple[Node, ...]:
    return tuple(c for c in node.named_children if c.type != "comment")


def _unwrap(node: Node) -> Node:
    while node.type in _WRAPPERS or node.type == "sequence_expression":
        children = _children(node)
        if not children:
            return node
        node = (
            children[-1]
            if node.type == "sequence_expression"
            else _field(node, "expression") or children[0]
        )
    return node


def _walk(node: Node) -> Iterator[Node]:
    stack = [node]
    while stack:
        current = stack.pop()
        yield current
        stack.extend(reversed(_children(current)))


def _type_only(node: Node) -> bool:
    tokens = [child.type for child in node.children if child.type != "comment"]
    specifiers = tuple(n for n in _walk(node) if n.type == "import_specifier")
    return tokens[1:2] == ["type"] or (
        bool(specifiers)
        and all(
            any(c.type == "type" for c in item.children) for item in specifiers
        )
    )


def _is_import_meta(node: Node) -> bool:
    node = _unwrap(node)
    return node.type == "meta_property" and tuple(
        child.type for child in node.children if child.type != "comment"
    ) == ("import", ".", "meta")


def _runtime(node: Node, *, expression: bool = False) -> Iterator[Node]:
    stack = [node]
    while stack:
        current = _unwrap(stack.pop()) if expression else stack.pop()
        if current.type in _RUNTIME_SKIP or (
            current.type in _MODULE_NODES and _type_only(current)
        ):
            continue
        yield current
        children = _children(current)
        if expression and current.type == "ternary_expression":
            children = (
                _field(current, "consequence"),
                _field(current, "alternative"),
            )
        elif expression and current.type in _ASSIGNMENTS:
            children = (_field(current, "right"),)
        stack.extend(child for child in reversed(children) if child is not None)


def _escape_value(raw: str, *, allow_legacy_octal: bool = False) -> str | None:  # noqa: PLR0911
    escaped = raw[1:]
    if escaped in _SIMPLE_ESCAPES:
        return _SIMPLE_ESCAPES[escaped]
    if escaped in {"\n", "\r", "\r\n", "\u2028", "\u2029"}:
        return ""
    if escaped == "0":
        return "\0"
    if escaped.isdigit():
        return (
            chr(int(escaped, 8))
            if allow_legacy_octal
            and all(character in "01234567" for character in escaped)
            else None
        )
    if not escaped.startswith(("x", "u")):
        return escaped
    if "\\u" in escaped:
        high = int(escaped[1:5], 16)
        low = int(escaped[7:], 16)
        code_point = 0x10000 + (high - _HIGH) * 0x400 + low - _LOW
        return chr(code_point)
    digits = (
        escaped.removeprefix("u{").removesuffix("}")
        if escaped.startswith("u{")
        else escaped[1:]
    )
    code_point = int(digits, 16)
    if code_point > _MAX_CODE_POINT or _HIGH <= code_point < _SURROGATE_END:
        return None
    return chr(code_point)


def _decoded_escapes(
    text: str,
    pattern: re.Pattern[str],
    *,
    allow_legacy_octal: bool = False,
) -> str | None:
    decoded: list[str] = []
    offset = 0
    for match in pattern.finditer(text):
        prefix = text[offset : match.start()]
        value = _escape_value(
            match.group(), allow_legacy_octal=allow_legacy_octal
        )
        if "\\" in prefix or value is None:
            return None
        decoded.extend((prefix, value))
        offset = match.end()
    suffix = text[offset:]
    if "\\" in suffix:
        return None
    return "".join((*decoded, suffix))


def _decoded_literal(
    node: Node,
    source: bytes,
    *,
    allow_legacy_octal: bool,
) -> str | None:
    raw = source[node.start_byte : node.end_byte]
    if node.type != "string_fragment":
        if raw[:1] == b"`" and any(
            child.type == "template_substitution" for child in _children(node)
        ):
            return None
        raw = raw[1:-1]
    return _decoded_escapes(
        raw.decode(),
        _ESCAPE,
        allow_legacy_octal=allow_legacy_octal,
    )


def _decoded_identifier(raw: bytes) -> str:
    text = raw.decode()
    if "\\" not in text:
        return text
    decoded = _decoded_escapes(text, _IDENTIFIER_ESCAPE)
    return text if decoded is None else decoded


def _is_package(value: str | None) -> bool:
    return bool(value and _PACKAGE.search(value.encode()))


def _pnpm_outcome(target: str, relevance: int) -> bool | None:
    if target == "dependency":
        return {0: False, 1: None, 2: True}[relevance]
    return None if target == "unknown" and relevance else False


def _reject(message: str) -> NoReturn:
    raise ValueError(message)


def _parse(source: bytes, language: JavaScriptLanguage) -> tuple[Node, int]:
    selected = _LANGUAGES.get(language)
    if selected is None:
        _reject(f"unsupported JavaScript language: {language}")
    root = Parser(selected).parse(source).root_node
    if root.has_error or root.is_missing:
        _reject("JavaScript parse tree contains errors or missing nodes")
    return root, root.descendant_count


def _validated_root(source: bytes, language: JavaScriptLanguage) -> Node:
    if len(source) > JAVASCRIPT_SOURCE_BYTE_LIMIT:
        _reject(
            f"JavaScript source exceeds {JAVASCRIPT_SOURCE_BYTE_LIMIT} bytes"
        )
    try:
        versions_match = all(
            version(name) == expected for name, expected in _VERSIONS.items()
        )
    except PackageNotFoundError:
        versions_match = False
    if not versions_match:
        _reject("unsupported Tree-sitter parser version")
    try:
        source.decode()
    except UnicodeDecodeError as error:
        message = "JavaScript source is not valid UTF-8"
        raise ValueError(message) from error
    root, count = _parse(source, language)
    if count > JAVASCRIPT_AST_NODE_LIMIT:
        _reject(
            f"JavaScript AST node count exceeds {JAVASCRIPT_AST_NODE_LIMIT}"
        )
    stack = [(root, 1)]
    while stack:
        node, depth = stack.pop()
        if depth > JAVASCRIPT_AST_DEPTH_LIMIT:
            _reject(
                f"JavaScript AST depth exceeds {JAVASCRIPT_AST_DEPTH_LIMIT}"
            )
        stack.extend((child, depth + 1) for child in _children(node))
    return root


class _Scanner:
    def __init__(
        self,
        source: bytes,
        root: Node,
        manager_reference: ManagerReference,
        *,
        pnpmfile: bool,
        commonjs_globals: bool,
    ) -> None:
        self.source, self.root = source, root
        self.manager_reference = manager_reference
        self.pnpmfile, self.commonjs_globals = pnpmfile, commonjs_globals
        self.allow_legacy_octal = commonjs_globals
        self.symbols: dict[str, list[_Symbol]] = {}
        self.tags: dict[str, str] = {}
        self.invalid: set[str] = set()
        self.calls, self.assignments = list[Node](), list[Node]()
        self.literals: dict[int, str | None] = {}
        self.array_literals: dict[int, tuple[str, ...] | None] = {}
        self.package_relevance: dict[int, int] = {}
        self.pnpm_dependencies: dict[int, bool] = {}
        self.manager_results: dict[
            tuple[str, tuple[str, ...] | None], bool
        ] = {}
        self.shell_manager_results: dict[tuple[str, tuple[str, ...]], bool] = {}
        self.package_cache_enabled = False
        self.direct_parameters: set[str] = set()
        self.unsafe_parameters: set[str] = set()
        self.possible_sensitive: set[str] = set()
        self.unsupported_aliases: set[str] = set()
        self.default_initializers: dict[str, list[Node]] = {}
        self.consumer = self.file_has_package = False

    def _name(self, node: Node | None) -> str | None:
        if node is None or node.type not in _NAMES:
            return None
        return _decoded_identifier(self.source[node.start_byte : node.end_byte])

    def _literal(self, node: Node) -> str | None:
        node = _unwrap(node)
        if node.type not in _LITERALS:
            return None
        if node.id not in self.literals:
            self.literals[node.id] = _decoded_literal(
                node,
                self.source,
                allow_legacy_octal=self.allow_legacy_octal,
            )
        return self.literals[node.id]

    def _arguments(self, node: Node) -> Nodes:
        arguments = _field(node, "arguments")
        if arguments is None:
            return ()
        return tuple(
            c for c in _children(arguments) if c.type not in _TYPE_ONLY
        )

    def _manager(
        self, executable: str, arguments: tuple[str, ...] | None
    ) -> bool:
        key = (executable, arguments)
        if key not in self.manager_results:
            self.manager_results[key] = self.manager_reference(
                executable, arguments
            )
        return self.manager_results[key]

    def _shell_manager(
        self, executable: str, arguments: tuple[str, ...]
    ) -> bool:
        key = (executable, arguments)
        if key not in self.shell_manager_results:
            command = " ".join((executable, *arguments))
            self.shell_manager_results[key] = self._manager(command, None)
        return self.shell_manager_results[key]

    def _bound_names(self, node: Node | None) -> tuple[str, ...]:
        found: list[str] = []
        stack = [node] if node is not None else []
        while stack:
            current = stack.pop()
            if (name := self._name(current)) is not None:
                found.append(name)
                continue
            target = None
            for field_name in ("left", "pattern", "argument", "value"):
                if (target := _field(current, field_name)) is not None:
                    break
            if target is not None:
                stack.append(target)
            elif current.type in {
                "object_pattern",
                "array_pattern",
                "formal_parameters",
                "rest_pattern",
            }:
                stack.extend(reversed(_children(current)))
        return tuple(found)

    def _add(self, name: str, symbol: _Symbol) -> None:
        self.symbols.setdefault(name, []).append(symbol)

    def _index_parameters(self, node: Node) -> None:
        parameters = _field(node, "parameters") or _field(node, "parameter")
        if parameters is None:
            return
        direct = (
            (parameters,)
            if parameters.type == "identifier"
            else _children(parameters)
        )
        self.direct_parameters.update(
            name
            for item in direct
            if item.type == "identifier"
            and (name := self._name(item)) is not None
        )
        for item in direct:
            self._declare_pattern(item, None, "mutable")

    def _index_loop_or_catch(self, node: Node) -> None:
        target = _field(node, "parameter") or _field(node, "left")
        for name in self._bound_names(target):
            self._add(name, _Symbol("mutable"))
        if target is not None:
            self._declare_pattern(target, None, "mutable", declare=False)
        if (
            node.type != "for_in_statement"
            or target is None
            or _unwrap(target).type
            in {"lexical_declaration", "variable_declaration"}
        ):
            return
        roots = {
            name
            for item in _runtime(target)
            if item.type in _MEMBERS
            and (name := self._root_name(item)) is not None
        }
        self.invalid.update(roots)
        self.unsafe_parameters.update(roots)

    def _declare_default_pattern(self, node: Node, *, declare: bool) -> None:
        left = _field(node, "left")
        default = _field(node, "right")
        name = self._name(left)
        if name is None:
            if left is not None:
                self._declare_pattern(left, None, "mutable", declare=declare)
            return
        if declare:
            self._add(name, _Symbol("mutable"))
        if default is not None:
            self.default_initializers.setdefault(name, []).append(default)

    def _declare_object_pattern(
        self,
        node: Node,
        init: Node | None,
        kind: Literal["const", "mutable"],
        *,
        declare: bool,
    ) -> None:
        for child in _children(node):
            value = _field(child, "value") or child
            if value.type in _ASSIGNMENT_PATTERNS:
                self._declare_pattern(value, init, kind, declare=declare)
                continue
            if value.type in {"array_pattern", "object_pattern"}:
                self._declare_pattern(value, None, "mutable", declare=declare)
                continue
            names = self._bound_names(value)
            prop = self._name(_field(child, "key") or child)
            for item in names:
                child_kind = (
                    "const"
                    if kind == "const" and len(names) == 1 and prop
                    else "mutable"
                )
                if declare:
                    self._add(item, _Symbol(child_kind, init, prop))

    def _declare_pattern(
        self,
        node: Node,
        init: Node | None,
        kind: Literal["const", "mutable"],
        *,
        declare: bool = True,
    ) -> None:
        if (name := self._name(node)) is not None:
            if declare:
                self._add(name, _Symbol(kind, init))
            return
        if node.type in _ASSIGNMENT_PATTERNS:
            self._declare_default_pattern(node, declare=declare)
            return
        if node.type == "object_pattern":
            self._declare_object_pattern(node, init, kind, declare=declare)
            return
        if node.type == "array_pattern":
            for child in _children(node):
                self._declare_pattern(child, init, "mutable", declare=declare)
            return
        for item in self._bound_names(node):
            if declare:
                self._add(item, _Symbol("mutable", init))

    def _module_specifier(self, node: Node) -> str | None:
        for item in _walk(node):
            if (source := _field(item, "source")) is not None:
                return self._literal(source)
        return None

    def _import_tag(self, module: str | None, name: str | None) -> str | None:
        if module in _CP_MODULES:
            if name in _APIS:
                return f"api:{name}"
            return "cp" if name == "default" else "unknown-child"
        if module in _MODULE_MODULES:
            return {
                "default": "module-namespace",
                "createRequire": "factory",
            }.get(name or "")
        return None

    def _index_import(self, node: Node) -> None:
        if _type_only(node):
            return
        module = self._module_specifier(node)
        self.consumer |= _is_package(module)
        for item in _walk(node):
            name = imported = None
            if item.type in {
                "namespace_import",
                "import_require_clause",
                "import_alias",
            }:
                target = _field(item, "name") or next(
                    iter(_children(item)), None
                )
                name, imported = self._name(target), "default"
            elif item.type == "import_specifier":
                if any(child.type == "type" for child in item.children):
                    continue
                imported = self._name(_field(item, "name"))
                name = self._name(_field(item, "alias")) or imported
            elif item.type == "import_clause":
                direct = next(
                    (c for c in _children(item) if c.type == "identifier"), None
                )
                name, imported = self._name(direct), "default"
            if name is not None:
                self._add(
                    name,
                    _Symbol("const", tag=self._import_tag(module, imported)),
                )

    def _root_name(self, node: Node | None) -> str | None:
        while node is not None:
            node = _unwrap(node)
            if (name := self._name(node)) is not None:
                return name
            member = self._member(node)
            node = member[0] if member else None
        return None

    def _index(self) -> None:  # noqa: C901, PLR0912
        for node in _runtime(self.root):
            if node.type in _MODULE_NODES and not (
                node.parent and node.parent.type in _MODULE_NODES
            ):
                self._index_import(node)
            elif node.type in {"variable_declaration", "lexical_declaration"}:
                kind = (
                    "const"
                    if any(c.type == "const" for c in node.children)
                    else "mutable"
                )
                for declarator in _children(node):
                    if declarator.type == "variable_declarator":
                        self._declare_pattern(
                            _field(declarator, "name") or declarator,
                            _field(declarator, "value"),
                            kind,
                        )
            elif node.type in _FUNCTION_NODES:
                name = self._name(_field(node, "name"))
                if name is not None:
                    self._add(name, _Symbol("mutable"))
                self._index_parameters(node)
            elif node.type in {
                "class",
                "class_declaration",
                "enum_declaration",
                "internal_module",
                "module",
            }:
                if (name := self._name(_field(node, "name"))) is not None:
                    self._add(name, _Symbol("mutable"))
            elif node.type in {"catch_clause", "for_in_statement"}:
                self._index_loop_or_catch(node)
            elif node.type in _CALLS:
                self.calls.append(node)
            elif (
                node.type in _ASSIGNMENTS
                or node.type == "update_expression"
                or (
                    node.type == "unary_expression"
                    and any(child.type == "delete" for child in node.children)
                )
            ):
                self.assignments.append(node)
                target = _field(node, "left") or _field(node, "argument")
                assigned = self._bound_names(target)
                self.invalid.update(assigned)
                self.unsafe_parameters.update(assigned)
                if target is not None:
                    self._declare_pattern(
                        target, None, "mutable", declare=False
                    )
                if (name := self._root_name(target)) is not None:
                    self.invalid.add(name)
                    self.unsafe_parameters.add(name)
        self.invalid.update(
            name
            for name, declarations in self.symbols.items()
            if len(declarations) != 1 or declarations[0].kind != "const"
        )
        self._derive_tags()
        self._mark_escapes()
        self._derive_tags()
        self.file_has_package = self._syntax_package(self.root) != _ABSENT
        self._cache_possible_sensitive()
        self.package_cache_enabled = True

    def _record(self, name: str | None) -> _Symbol | None:
        declarations = self.symbols.get(name or "", ())
        if name in self.invalid or len(declarations) != 1:
            return None
        return declarations[0]

    def _constant_nodes(self, node: Node) -> tuple[Node, ...]:
        node = _unwrap(node)
        result = [node]
        record = self._record(self._name(node))
        if (
            not record
            or record.initializer is None
            or record.member is not None
        ):
            return tuple(result)
        initializer = _unwrap(record.initializer)
        result.append(initializer)
        target = self._record(self._name(initializer))
        if target and target.initializer and target.member is None:
            result.append(_unwrap(target.initializer))
        return tuple(result)

    def _exact_string(self, node: Node) -> str | None:
        for candidate in self._constant_nodes(node):
            if (value := self._literal(candidate)) is not None:
                return value
        return None

    def _array_literal_uncached(self, node: Node) -> tuple[str, ...] | None:
        node = _unwrap(node)
        if node.type != "array":
            return None
        values: list[str] = []
        expect_value = True
        for child in node.children:
            if child.type == "comment":
                continue
            if not child.is_named:
                if child.type == ",":
                    if expect_value:
                        return None
                    expect_value = True
                continue
            value = (
                None
                if child.type == "spread_element"
                else self._exact_string(child)
            )
            if value is None:
                return None
            values.append(value)
            expect_value = False
        return tuple(values)

    def _array_literal(self, node: Node) -> tuple[str, ...] | None:
        node = _unwrap(node)
        if node.id not in self.array_literals:
            self.array_literals[node.id] = self._array_literal_uncached(node)
        return self.array_literals[node.id]

    def _exact_array(self, node: Node) -> tuple[str, ...] | None:
        for candidate in self._constant_nodes(node):
            if (value := self._array_literal(candidate)) is not None:
                return value
        return None

    def _member(self, node: Node | None) -> tuple[Node, str | None] | None:
        if node is None or (node := _unwrap(node)).type not in _MEMBERS:
            return None
        obj = _field(node, "object")
        if obj is None:
            return None
        prop = _field(node, "property") or _field(node, "index")
        if prop is None:
            return obj, None
        value = (
            self._name(_unwrap(prop))
            if node.type == "member_expression"
            else self._exact_string(prop)
        )
        return obj, value

    def _property_name(self, node: Node | None) -> str | None:
        if node is None:
            return None
        node = _unwrap(node)
        if (name := self._name(node)) is not None:
            return name
        if (value := self._literal(node)) is not None:
            return value
        if node.type != "computed_property_name":
            return None
        children = _children(node)
        return self._exact_string(children[0]) if len(children) == 1 else None

    def _shell_value(self, node: Node | None) -> _ShellOption:
        if node is None:
            return "unknown"
        node = _unwrap(node)
        if node.type == "true":
            return "enabled"
        if node.type == "false":
            return "disabled"
        shell = self._exact_string(node)
        if shell is None:
            return "unknown"
        return "enabled" if shell else "disabled"

    def _shell_option(self, node: Node) -> _ShellOption:
        state: _ShellOption = "absent"
        for child in _children(_unwrap(node)):
            if child.type == "spread_element":
                state = "unknown"
                continue
            if child.type != "pair":
                name = self._property_name(_field(child, "name"))
                getter = child.type == "method_definition" and any(
                    item.type == "get" for item in child.children
                )
                if self._name(child) == "shell" or (
                    getter and name in {None, "shell"}
                ):
                    state = "unknown"
                continue
            prop = self._property_name(_field(child, "key"))
            if prop is None:
                state = "unknown"
                continue
            if prop != "shell":
                continue
            state = self._shell_value(_field(child, "value"))
        return state

    def _project(self, tag: str | None, prop: str | None) -> str | None:
        if tag == "cp":
            return f"api:{prop}" if prop in _APIS else "unknown-child"
        return _PROJECTIONS.get((tag, prop)) if tag and prop else None

    def _atom_tag(self, node: Node) -> str | None:
        node = _unwrap(node)
        if _is_import_meta(node):
            return "import-meta"
        name = self._name(node) or (
            node.type if node.type == "import" else None
        )
        enabled = self.commonjs_globals or name not in {"module", "require"}
        if enabled and name in _BUILTINS and not self.symbols.get(name or ""):
            return _BUILTINS[name]
        return self.tags.get(name or "")

    def _load_result(  # noqa: PLR0911
        self, tag: str | None, arguments: Nodes
    ) -> str | None:
        if tag == "factory":
            return "loader"
        if tag == "other":
            return "other"
        if tag not in {"loader", "builtin", "resolve"} or not arguments:
            return None
        module = self._exact_string(arguments[0])
        if module is None:
            return "unknown-child" if tag == "builtin" else "unknown-loader"
        if tag == "builtin":
            return "cp" if module in _CP_MODULES else "other"
        if tag == "resolve":
            return "other"
        if module in _CP_MODULES:
            return "cp"
        if module in _MODULE_MODULES:
            return "module-namespace"
        return "other"

    def _callee_tag(self, node: Node) -> str | None:
        node = _unwrap(node)
        if (tag := self._atom_tag(node)) is not None:
            return tag
        member = self._member(node)
        if member is None:
            return None
        obj, prop = member
        base = self._atom_tag(obj)
        if base is None and _unwrap(obj).type == "call_expression":
            call = _unwrap(obj)
            function = _field(call, "function")
            base = self._load_result(
                self._callee_tag(function) if function is not None else None,
                self._arguments(call),
            )
        return self._project(base, prop)

    def _tag(self, node: Node | None) -> str | None:
        if node is None:
            return None
        node = _unwrap(node)
        if (tag := self._callee_tag(node)) is not None:
            return tag
        if node.type != "call_expression":
            return None
        function = _field(node, "function")
        return self._load_result(
            self._callee_tag(function) if function is not None else None,
            self._arguments(node),
        )

    def _derive_tags(self) -> None:
        self.tags = {
            name: items[0].tag
            for name, items in self.symbols.items()
            if self._record(name) is not None and items[0].tag is not None
        }
        for aliases in (False, False, True):
            updates: dict[str, str] = {}
            for name in self.symbols:
                record = self._record(name)
                if (
                    record is None
                    or record.initializer is None
                    or name in self.tags
                ):
                    continue
                init = _unwrap(record.initializer)
                member = self._member(init)
                rooted = bool(
                    self._name(init)
                    or (member and self._name(_unwrap(member[0])))
                )
                if rooted != aliases:
                    continue
                tag = self._tag(init)
                if record.member:
                    tag = self._project(tag, record.member)
                if tag is not None:
                    updates[name] = (
                        "writer-alias" if aliases and tag == "writer" else tag
                    )
            self.tags.update(updates)

    def _composition(self, node: Node) -> str | None:
        node = _unwrap(node)
        if node.type not in {"binary_expression", "template_string"}:
            return None
        parts: list[str] = []
        for child in _children(node):
            value = self._composition(child)
            if child.type == "template_substitution":
                values = _children(child)
                value = (
                    self._exact_string(values[0]) if len(values) == 1 else None
                )
            value = value if value is not None else self._exact_string(child)
            if value is None:
                return None
            parts.append(value)
        return "".join(parts)

    def _syntax_package_uncached(self, node: Node) -> int:
        possible = spread = False
        for item in _runtime(node, expression=True):
            reference = (kind := item.type) in _REFERENCES
            spread |= kind == "spread_element"
            string = kind in _LITERALS or reference
            array = kind == "array" or reference
            literal = self._exact_string(item) if string else None
            values = self._exact_array(item) if array else None
            composition = self._composition(item)
            resolved = (literal, composition, *(values or ()))
            if any(map(_is_package, resolved)):
                return _POSSIBLE if spread else _PRESENT
            exact = any(
                value is not None for value in (literal, values, composition)
            )
            raw = self.source[item.start_byte : item.end_byte]
            malformed = (
                kind in _LITERALS and literal is None and _PACKAGE_BYTES in raw
            )
            member = self._member(item) if kind in _MEMBERS else None
            sensitive_member = kind in _MEMBERS and (
                (member is not None and _is_import_meta(member[0]))
                or self._sensitive_callee(item)
            )
            tracked = reference and self._name(item) in self.symbols
            uncertain = kind in _CALLS or tracked or sensitive_member
            possible |= malformed or (
                not exact and self.file_has_package and uncertain
            )
        return _POSSIBLE if possible else _ABSENT

    def _syntax_package(self, node: Node) -> int:
        key = self._constant_nodes(node)[-1].id
        if self.package_cache_enabled and key in self.package_relevance:
            return self.package_relevance[key]
        relevance = self._syntax_package_uncached(node)
        if self.package_cache_enabled:
            self.package_relevance[key] = relevance
        return relevance

    def _arguments_package(self, args: Nodes) -> int:
        return max(map(self._syntax_package, args), default=_ABSENT)

    def _sensitive_default_names(self, roots: frozenset[str]) -> set[str]:
        return {
            name
            for name, defaults in self.default_initializers.items()
            if any(
                self._contains_sensitive_identity(default, roots)
                for default in defaults
            )
        }

    def _cache_possible_sensitive(self) -> None:
        aliases: dict[str, str] = {}
        possible = set(_BUILTINS) - _OBJECT_NAMESPACES - {"import"}
        if not self.commonjs_globals:
            possible.difference_update({"module", "require"})
        if self.pnpmfile:
            possible.update(_OBJECT_NAMESPACES)
        for name, symbols in self.symbols.items():
            initializer = symbols[0].initializer if len(symbols) == 1 else None
            if initializer is not None:
                source = self._name(_unwrap(initializer))
                if source is not None:
                    aliases[name] = source
            if any(
                symbol.tag not in _INERT_TAGS
                or (
                    symbol.initializer is not None
                    and self._tag(symbol.initializer) not in _INERT_TAGS
                )
                for symbol in symbols
            ):
                possible.add(name)
        roots = frozenset(possible)
        for name, symbols in self.symbols.items():
            initializer = symbols[0].initializer if len(symbols) == 1 else None
            if initializer is not None and self._contains_sensitive_identity(
                initializer, roots
            ):
                possible.add(name)
        possible.update(self._sensitive_default_names(frozenset(possible)))
        self.unsupported_aliases = {
            name
            for name, source in aliases.items()
            if source in aliases and source in possible
        }
        self.possible_sensitive = possible

    def _sensitive_callee(self, node: Node) -> bool:
        node = _unwrap(node)
        tag = self._tag(node)
        if tag in {"writer", "writer-alias"}:
            return self.pnpmfile
        if tag not in _INERT_TAGS:
            return True
        member = self._member(node)
        if tag == "other" or (member and self._tag(member[0]) == "other"):
            return False
        return self._contains_sensitive_identity(node)

    def _mark_escapes(self) -> None:
        for call in self.calls:
            callee = _field(call, "function") or _field(call, "constructor")
            args = self._arguments(call)
            tag = self._tag(callee)
            if tag and not tag.startswith("unknown") and tag != "writer-alias":
                continue
            names = {
                name
                for argument in args
                for item in _runtime(argument)
                if item.type in _REFERENCES
                and (name := self._name(item)) is not None
            }
            escaped = {
                name
                for name in names
                if name in self.symbols and len(self.symbols[name]) == 1
            }
            self.invalid.update(escaped)
            self.unsafe_parameters.update(names)
            if (member := self._member(callee)) is not None and (
                name := self._root_name(member[0])
            ) is not None:
                self.invalid.add(name)
                self.unsafe_parameters.add(name)

    def _contains_sensitive_identity(
        self,
        node: Node,
        names: frozenset[str] | None = None,
    ) -> bool:
        roots = self.possible_sensitive if names is None else names
        for item in _runtime(node, expression=True):
            tag = self._tag(item)
            if tag not in _INERT_TAGS and (
                tag not in {"writer", "writer-alias"} or self.pnpmfile
            ):
                return True
            member = self._member(item)
            if member and member[1] is None and _is_import_meta(member[0]):
                return True
            if (
                member
                and self._tag(member[0]) in _OBJECT_NAMESPACES
                and member[1] is None
            ):
                return True
            if (
                item.type in _REFERENCES
                and self._name(item) in roots
                and not (
                    tag in _OBJECT_NAMESPACES
                    and (parent := self._member(item.parent))
                    and parent[0].id == item.id
                    and parent[1] is not None
                )
            ):
                return True
        return False

    def _sensitive_escape(self) -> bool:
        for node in _runtime(self.root):
            candidates: tuple[Node, ...] = ()
            if node.type in {"array", "object"}:
                candidates = (node,)
            elif node.type == "return_statement":
                candidates = _children(node)[:1]
            elif node.type in _ASSIGNMENTS or (
                node.type == "variable_declarator"
                and node.parent
                and not any(c.type == "const" for c in node.parent.children)
            ):
                value = _field(node, "right") or _field(node, "value")
                candidates = (value,) if value is not None else ()
            elif node.type in _CALLS:
                callee = _field(node, "function") or _field(node, "constructor")
                tag = self._tag(callee)
                if (
                    tag is None
                    or tag.startswith("unknown")
                    or tag == "writer-alias"
                ):
                    candidates = self._arguments(node)
            if any(
                self._contains_sensitive_identity(item) for item in candidates
            ):
                return True
        return False

    def _pnpm_target(self, node: Node) -> str:
        member = self._member(node)
        if member is None or member[1] is None:
            return "unknown"
        if member[1] not in NODE_DEPENDENCY_FIELDS:
            return "metadata"
        return (
            "dependency"
            if _unwrap(member[0]).type == "identifier"
            else "unknown"
        )

    def _pnpm_dependency_syntax_uncached(self, node: Node) -> bool:
        return any(
            (member := self._member(resolved)) is not None
            and member[1] in NODE_DEPENDENCY_FIELDS
            for candidate in self._constant_nodes(node)
            for item in _runtime(candidate, expression=True)
            for resolved in self._constant_nodes(item)
        )

    def _pnpm_dependency_syntax(self, node: Node) -> bool:
        key = self._constant_nodes(node)[-1].id
        if key not in self.pnpm_dependencies:
            self.pnpm_dependencies[key] = self._pnpm_dependency_syntax_uncached(
                node
            )
        return self.pnpm_dependencies[key]

    def _pnpm_metadata_root(self, node: Node) -> bool:
        node = _unwrap(node)
        name = self._name(node)
        if name is None:
            return False
        symbols = self.symbols.get(name, ())
        if not symbols:
            return name not in self.unsafe_parameters
        symbol = symbols[0]
        if len(symbols) != 1:
            return False
        if symbol.initializer is None:
            return (
                name in self.direct_parameters
                and name not in self.unsafe_parameters
            )
        if symbol.kind != "const" or name in self.invalid:
            return False
        source = self._name(_unwrap(symbol.initializer))
        roots = self.symbols.get(source or "", ())
        return source is not None and (
            (not roots and source not in self.unsafe_parameters)
            or (
                len(roots) == 1
                and source in self.direct_parameters
                and source not in self.unsafe_parameters
            )
        )

    def _pnpm_spread_candidate_target(self, node: Node) -> str:
        target = self._pnpm_target(node)
        if target != "metadata":
            return target
        member = self._member(node)
        return (
            "metadata"
            if member is not None and self._pnpm_metadata_root(member[0])
            else "unknown"
        )

    def _pnpm_spread_target(self, node: Node) -> str:
        arrays = tuple(
            candidate
            for candidate in reversed(self._constant_nodes(node))
            if candidate.type == "array"
        )
        if not arrays:
            return "unknown"
        destination = next(
            (
                child
                for child in arrays[0].children
                if child.type not in {"[", "comment"}
            ),
            None,
        )
        if destination is None or destination.type in {
            ",",
            "]",
            "spread_element",
        }:
            return "unknown"
        if self._pnpm_dependency_syntax(destination):
            return "dependency"
        known = {
            target
            for candidate in self._constant_nodes(destination)
            if (target := self._pnpm_spread_candidate_target(candidate))
            != "unknown"
        }
        return known.pop() if len(known) == 1 else "unknown"

    def _pnpm_spread(self, node: Node) -> bool:
        operand = next(iter(_children(node)), None)
        if operand is None:
            return False
        relevance = max(
            (
                self._syntax_package(candidate)
                for candidate in self._constant_nodes(operand)
            ),
            default=_ABSENT,
        )
        return relevance != _ABSENT and self._pnpm_spread_target(operand) != (
            "metadata"
        )

    def _pnpm_assignment(self, mutation: Node) -> bool | None:
        left = _field(mutation, "left") or _field(mutation, "argument")
        right = _field(mutation, "right")
        dependency_syntax = left is not None and self._pnpm_dependency_syntax(
            left
        )
        member = self._member(left)
        if member is None:
            relevance = self._arguments_package(
                tuple(node for node in (left, right) if node is not None)
            )
            return None if dependency_syntax and relevance != _ABSENT else False
        obj, key = member
        target = (
            "dependency"
            if key in NODE_DEPENDENCY_FIELDS
            else self._pnpm_target(obj)
        )
        key_node = _field(_unwrap(left), "index") if left else None
        extra = (obj,) if target == "unknown" else ()
        relevance = self._arguments_package(
            tuple(node for node in (key_node, right) if node is not None)
            + extra
        )
        if target != "dependency" and dependency_syntax:
            relevance = max(
                relevance,
                self._arguments_package(
                    tuple(node for node in (left, right) if node is not None)
                ),
            )
            if relevance != _ABSENT:
                return None
        return _pnpm_outcome(target, relevance)

    def _pnpm(self) -> tuple[bool, bool]:
        unsupported = False
        for mutation in self.assignments:
            outcome = self._pnpm_assignment(mutation)
            if outcome is True:
                return True, False
            unsupported |= outcome is None
        for call in self.calls:
            tag = self._tag(_field(call, "function"))
            if tag not in {"writer", "writer-alias"}:
                continue
            args = self._arguments(call)
            spreads = (arg for arg in args if arg.type == "spread_element")
            unsupported |= any(map(self._pnpm_spread, spreads))
            if args and args[0].type == "spread_element":
                continue
            if tag == "writer-alias":
                unsupported |= self._arguments_package(args[1:]) != _ABSENT
                continue
            target = self._pnpm_target(args[0]) if args else "unknown"
            destination = self._arguments_package(args[:1])
            if target == "metadata" and destination != _ABSENT:
                target = "unknown"
            relevance = self._arguments_package(
                args[1:] + (args[:1] if target == "unknown" else ())
            )
            outcome = _pnpm_outcome(target, relevance)
            if outcome is True:
                return True, False
            unsupported |= outcome is None
        return False, unsupported

    def _scan_structured_api(self, args: Nodes) -> bool | None:  # noqa: C901, PLR0911
        if not args:
            return False
        if len(args) < 2:  # noqa: PLR2004
            return None if self._arguments_package(args) != _ABSENT else False
        executable = self._exact_string(args[0])
        second = _unwrap(args[1])
        options = second if second.type == "object" else None
        argv = () if options is not None else self._exact_array(args[1])
        if (
            options is None
            and len(args) > 2  # noqa: PLR2004
            and _unwrap(args[2]).type == "object"
        ):
            options = _unwrap(args[2])
        if (
            executable is not None
            and argv is not None
            and self._manager(executable, argv)
        ):
            return True
        shell = (
            self._shell_option(options)
            if options is not None
            else "unknown"
            if len(args) > 2  # noqa: PLR2004
            else "absent"
        )
        shell_command = bool(executable and self._manager(executable, None))
        if (
            shell in {"enabled", "unknown"}
            and executable is not None
            and argv is not None
        ):
            shell_command |= self._shell_manager(executable, argv)
        relevant = self._arguments_package(args[:2]) != _ABSENT
        possible_shell_command = shell_command
        if (
            shell in {"enabled", "unknown"}
            and relevant
            and executable is not None
            and argv is None
        ):
            possible_shell_command |= self._manager(
                f"{executable} {CONSUMER_PACKAGE}", None
            )
        if shell == "enabled":
            if shell_command:
                return True
            if executable is None and self._syntax_package(args[0]) != _ABSENT:
                return None
            if argv is not None and executable is not None:
                return False
        if (
            shell == "unknown"
            and relevant
            and (executable is None or possible_shell_command)
        ):
            return None
        if argv is not None and executable is not None:
            return False
        possible_manager = (
            executable is None
            or self._manager(executable, ("install", CONSUMER_PACKAGE))
            or possible_shell_command
        )
        return None if relevant and possible_manager else False

    def _scan_api(self, call: Node, api: str) -> bool | None:
        args = self._arguments(call)
        if api not in {"exec", "execSync"}:
            return self._scan_structured_api(args)
        if not args:
            return False
        command = self._exact_string(args[0])
        if command is not None:
            return self._manager(command, None)
        return None if self._syntax_package(args[0]) != _ABSENT else False

    def _scan_call(self, call: Node) -> bool | None:  # noqa: C901, PLR0911
        callee = _field(call, "function") or _field(call, "constructor")
        args = self._arguments(call)
        tag = self._tag(callee)
        package = self._arguments_package(args) != _ABSENT
        if tag in {"writer", "writer-alias"}:
            return False
        if tag == "factory" and len(args) == 1:
            argument = _unwrap(args[0])
            member = self._member(argument)
            if (
                argument.type == "member_expression"
                and member is not None
                and member[1] == "url"
                and _is_import_meta(member[0])
            ):
                return False
        if package and any(
            item.type in _REFERENCES
            and self._name(item) in self.unsupported_aliases
            for node in ((callee,) if callee is not None else ()) + args
            for item in _runtime(node)
        ):
            return None
        if call.type == "new_expression":
            sensitive = callee is not None and self._sensitive_callee(callee)
            return None if package and sensitive else False
        if tag in {"loader", "resolve", "builtin"}:
            if not args:
                return False
            module = self._exact_string(args[0])
            if module is not None:
                return tag != "builtin" and _is_package(module)
            return None if self._syntax_package(args[0]) != _ABSENT else False
        if tag and tag.startswith("api:"):
            return self._scan_api(call, tag.removeprefix("api:"))
        if package and (
            tag in {"dynamic", "unknown-child", "unknown-loader"}
            or (callee is not None and self._sensitive_callee(callee))
        ):
            return None
        return False

    def _barrier(self, node: Node) -> bool:
        current = node.parent
        while current is not None:
            if current.type in _BARRIER_NODES or (
                current.type == "function_declaration"
                and any(child.type == "async" for child in current.children)
            ):
                return True
            current = current.parent
        return False

    def scan(self) -> bool:
        self._index()
        escaped = self.file_has_package and self._sensitive_escape()
        pnpm_consumer, unsupported = (
            self._pnpm() if self.pnpmfile else (False, False)
        )
        found = self.consumer or pnpm_consumer
        for call in self.calls:
            outcome = self._scan_call(call)
            relevant = self._arguments_package(self._arguments(call)) != _ABSENT
            if self._barrier(call) and relevant and outcome is not False:
                outcome = None
            found |= outcome is True
            unsupported |= outcome is None
        if escaped or (unsupported and not found):
            raise ValueError(UNSUPPORTED_DIAGNOSTIC)
        return found


def scan_javascript_consumer(
    source: bytes,
    *,
    language: JavaScriptLanguage,
    manager_reference: ManagerReference,
    pnpmfile: bool = False,
    commonjs_globals: bool = True,
) -> bool:
    """Return whether source contains a supported package consumer."""
    root = _validated_root(source, language)
    return _Scanner(
        source,
        root,
        manager_reference,
        pnpmfile=pnpmfile,
        commonjs_globals=commonjs_globals,
    ).scan()
