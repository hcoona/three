namespace Hcoona.DocumentTranslatorCli;

internal enum MarkdownFailureKind
{
    InvalidUtf8,
    ParseError,
    UnsupportedSyntax,
    UnreliableSourceSpan,
    SegmentSizeViolation,
    ReconstructionChanged,
    StructuralChanged,
}
