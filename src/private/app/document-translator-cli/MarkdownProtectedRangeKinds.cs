namespace Hcoona.DocumentTranslatorCli;

internal static class MarkdownProtectedRangeKinds
{
    public const string FencedCodeBlock = "fenced-code-block";
    public const string IndentedCodeBlock = "indented-code-block";
    public const string InlineCode = "inline-code";
    public const string YamlFrontMatter = "yaml-front-matter";
    public const string RawHtmlBlock = "raw-html-block";
    public const string HtmlComment = "html-comment";
    public const string InlineHtmlTag = "inline-html-tag";
    public const string InlineHtmlEnclosureText = "inline-html-enclosure-text";
    public const string LinkDestination = "link-destination";
    public const string LinkTitle = "link-title";
    public const string ReferenceLabel = "reference-label";
    public const string ReferenceDefinition = "reference-definition";
    public const string FootnoteDefinition = "footnote-definition";
    public const string FootnoteReference = "footnote-reference";
    public const string Autolink = "autolink";
    public const string UrlLiteral = "url-literal";
    public const string EmailLiteral = "email-literal";
    public const string UriFragment = "uri-fragment";
    public const string MarkdownStructuralSyntax = "markdown-structural-syntax";
    public const string EscapedMarkdownDelimiter = "escaped-markdown-delimiter";
    public const string MachineToken = "machine-token";
}
