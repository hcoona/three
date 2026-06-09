using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;
using System.Text.RegularExpressions;

namespace Hcoona.DocumentTranslatorCli;

internal sealed partial class MarkdownDocumentParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private const char Utf8BomCharacter = '\uFEFF';
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private readonly MarkdownPipeline pipeline;

    public MarkdownDocumentParser(MarkdownPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        this.pipeline = pipeline;
    }

    public static MarkdownDocumentParser CreateV1() =>
        new(MarkdownParserFactory.CreateV1Pipeline());

    public MarkdownParseResult Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        bool hasUtf8Bom = markdown.Length > 0 && markdown[0] == Utf8BomCharacter;
        string sourceText = hasUtf8Bom ? markdown[1..] : markdown;
        if (ContainsUnpairedSurrogate(sourceText))
        {
            return CreateFailure(
                MarkdownFailureKind.InvalidUtf8,
                "Input must be valid UTF-8.",
                sourceText: string.Empty,
                new MarkdownSourceMetadata(hasUtf8Bom, HasFinalNewline(sourceText), []));
        }

        MarkdownSourceMetadata sourceMetadata = CreateSourceMetadata(sourceText, hasUtf8Bom);
        if (StartsWithJsonFrontMatter(sourceText))
        {
            return CreateFailure(
                MarkdownFailureKind.UnsupportedSyntax,
                "JSON front matter is not supported in Markdown-aware translation.",
                sourceText,
                sourceMetadata);
        }

        MarkdownDocument document = Markdown.Parse(sourceText, pipeline);
        return CreateSuccess(document, sourceText, sourceMetadata);
    }

    public MarkdownParseResult Parse(ReadOnlySpan<byte> input)
    {
        bool hasUtf8Bom = input.StartsWith(Utf8Bom);
        ReadOnlySpan<byte> markdownBytes = hasUtf8Bom ? input[Utf8Bom.Length..] : input;
        string markdown;
        try
        {
            markdown = StrictUtf8.GetString(markdownBytes);
        }
        catch (DecoderFallbackException)
        {
            return CreateFailure(
                MarkdownFailureKind.InvalidUtf8,
                "Input must be valid UTF-8.",
                sourceText: string.Empty,
                new MarkdownSourceMetadata(hasUtf8Bom, HasFinalNewline(markdownBytes), []));
        }

        MarkdownSourceMetadata sourceMetadata = CreateSourceMetadata(markdown, hasUtf8Bom);
        if (StartsWithJsonFrontMatter(markdown))
        {
            return CreateFailure(
                MarkdownFailureKind.UnsupportedSyntax,
                "JSON front matter is not supported in Markdown-aware translation.",
                markdown,
                sourceMetadata);
        }

        MarkdownDocument document = Markdown.Parse(markdown, pipeline);
        return CreateSuccess(document, markdown, sourceMetadata);
    }

    private static MarkdownParseResult CreateSuccess(
        MarkdownDocument document,
        string sourceText,
        MarkdownSourceMetadata sourceMetadata)
    {
        MarkdownProtectedRangeCollectionResult collectionResult =
            MarkdownProtectedRangeCollector.Collect(document, sourceText);
        if (collectionResult.Diagnostics.Count > 0)
        {
            return new MarkdownParseResult(
                null,
                collectionResult.Diagnostics,
                sourceText,
                sourceMetadata,
                [],
                []);
        }

        IReadOnlyList<ProtectedSlice> validationBoundarySlices = collectionResult.ProtectedSlices
            .Where(IsValidationBoundarySlice)
            .ToArray();

        return new MarkdownParseResult(
            document,
            [],
            sourceText,
            sourceMetadata,
            collectionResult.ProtectedSlices,
            validationBoundarySlices);
    }

    private static bool IsValidationBoundarySlice(ProtectedSlice slice) =>
        slice.Kind switch
        {
            MarkdownProtectedRangeKinds.FencedCodeBlock
                or MarkdownProtectedRangeKinds.IndentedCodeBlock
                or MarkdownProtectedRangeKinds.InlineCode
                or MarkdownProtectedRangeKinds.YamlFrontMatter
                or MarkdownProtectedRangeKinds.HtmlComment
                or MarkdownProtectedRangeKinds.MachineToken
                or MarkdownProtectedRangeKinds.RawHtmlBlock => true,
            _ => false,
        };

    private static MarkdownParseResult CreateFailure(
        MarkdownFailureKind kind,
        string message,
        string sourceText,
        MarkdownSourceMetadata sourceMetadata) =>
        new(
            null,
            [new MarkdownDiagnostic(kind, message)],
            sourceText,
            sourceMetadata,
            [],
            []);

    private static MarkdownSourceMetadata CreateSourceMetadata(
        string sourceText,
        bool hasUtf8Bom) =>
        new(hasUtf8Bom, HasFinalNewline(sourceText), CollectLineEndings(sourceText));

    private static bool StartsWithJsonFrontMatter(string sourceText) =>
        sourceText.Length > 0 && sourceText[0] == '{';

    private static bool ContainsUnpairedSurrogate(string sourceText)
    {
        for (int index = 0; index < sourceText.Length; index++)
        {
            char current = sourceText[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= sourceText.Length
                    || !char.IsLowSurrogate(sourceText[index + 1]))
                {
                    return true;
                }

                index++;
            }
            else if (char.IsLowSurrogate(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFinalNewline(string sourceText) =>
        sourceText.EndsWith('\n') || sourceText.EndsWith('\r');

    private static bool HasFinalNewline(ReadOnlySpan<byte> sourceBytes) =>
        !sourceBytes.IsEmpty && (sourceBytes[^1] == (byte)'\n' || sourceBytes[^1] == (byte)'\r');

    private static List<MarkdownLineEnding> CollectLineEndings(string sourceText)
    {
        List<MarkdownLineEnding> lineEndings = [];
        for (int index = 0; index < sourceText.Length; index++)
        {
            char current = sourceText[index];
            if (current == '\r')
            {
                int length = index + 1 < sourceText.Length
                    && sourceText[index + 1] == '\n'
                        ? 2
                        : 1;
                lineEndings.Add(new MarkdownLineEnding(
                    new TextRange(index, length),
                    length == 2 ? "\r\n" : "\r"));
                if (length == 2)
                {
                    index++;
                }
            }
            else if (current == '\n')
            {
                lineEndings.Add(new MarkdownLineEnding(new TextRange(index, 1), "\n"));
            }
        }

        return lineEndings;
    }
}

internal sealed record MarkdownProtectedRangeCollectionResult(
    IReadOnlyList<ProtectedSlice> ProtectedSlices,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics);

internal static partial class MarkdownProtectedRangeCollector
{
    public static MarkdownProtectedRangeCollectionResult Collect(
        MarkdownDocument document,
        string sourceText)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceText);

        List<ProtectedSlice> slices = [];
        List<MarkdownDiagnostic> diagnostics = [];
        List<SourceSpan> machineTokenCandidateSpans = [];
        List<InlineHtmlTagContext> inlineHtmlTagContexts = [];
        CollectBlockSlices(document, sourceText, slices, diagnostics);
        CollectInlineSlices(
            document,
            sourceText,
            slices,
            diagnostics,
            machineTokenCandidateSpans,
            inlineHtmlTagContexts);
        CollectInlineHtmlEnclosureText(sourceText, slices, diagnostics, inlineHtmlTagContexts);
        CollectReferenceDefinitions(sourceText, slices, diagnostics);
        CollectFootnotes(sourceText, slices, diagnostics);
        CollectRegexMatches(
            sourceText,
            slices,
            diagnostics,
            AutolinkRegex(),
            MarkdownProtectedRangeKinds.Autolink);
        CollectUrlLiterals(sourceText, slices, diagnostics);
        CollectRegexMatches(
            sourceText,
            slices,
            diagnostics,
            EmailLiteralRegex(),
            MarkdownProtectedRangeKinds.EmailLiteral);
        CollectUriFragments(sourceText, slices, diagnostics);
        CollectEscapedMarkdownDelimiters(sourceText, slices, diagnostics);
        CollectMarkdownStructuralSyntax(document, sourceText, slices, diagnostics);
        CollectEarlyMachineTokens(sourceText, slices, diagnostics, machineTokenCandidateSpans);

        if (diagnostics.Count > 0)
        {
            return new MarkdownProtectedRangeCollectionResult([], diagnostics);
        }

        ProtectedSlice[] protectedSlices = slices
            .Where(slice => slice.SourceRange.IsWithin(sourceText))
            .OrderBy(slice => slice.SourceRange.Start)
            .ThenBy(slice => slice.SourceRange.Length)
            .ThenBy(slice => slice.Kind, StringComparer.Ordinal)
            .Select((slice, index) => slice with { SliceId = $"{slice.Kind}-{index}" })
            .ToArray();

        return new MarkdownProtectedRangeCollectionResult(protectedSlices, []);
    }

    private static void CollectBlockSlices(
        MarkdownObject markdownObject,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (MarkdownObject descendant in markdownObject.Descendants())
        {
            switch (descendant)
            {
                case YamlFrontMatterBlock yamlFrontMatterBlock:
                    AddRequiredSpan(
                        slices,
                        diagnostics,
                        MarkdownProtectedRangeKinds.YamlFrontMatter,
                        yamlFrontMatterBlock.Span,
                        sourceText);
                    break;
                case FencedCodeBlock fencedCodeBlock:
                    AddRequiredSpan(
                        slices,
                        diagnostics,
                        MarkdownProtectedRangeKinds.FencedCodeBlock,
                        fencedCodeBlock.Span,
                        sourceText);
                    break;
                case CodeBlock codeBlock:
                    AddRequiredSpan(
                        slices,
                        diagnostics,
                        MarkdownProtectedRangeKinds.IndentedCodeBlock,
                        codeBlock.Span,
                        sourceText);
                    break;
                case HtmlBlock htmlBlock:
                    string originalText = GetOriginalText(htmlBlock.Span, sourceText);
                    AddRequiredSpan(
                        slices,
                        diagnostics,
                        originalText.TrimStart().StartsWith("<!--", StringComparison.Ordinal)
                            ? MarkdownProtectedRangeKinds.HtmlComment
                            : MarkdownProtectedRangeKinds.RawHtmlBlock,
                        htmlBlock.Span,
                        sourceText);
                    break;
            }
        }
    }

    private static void CollectInlineSlices(
        MarkdownObject markdownObject,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        List<SourceSpan> machineTokenCandidateSpans,
        List<InlineHtmlTagContext> inlineHtmlTagContexts)
    {
        foreach (Inline inline in markdownObject.Descendants().OfType<Inline>())
        {
            switch (inline)
            {
                case CodeInline codeInline:
                    AddRequiredSpan(
                        slices,
                        diagnostics,
                        MarkdownProtectedRangeKinds.InlineCode,
                        codeInline.Span,
                        sourceText);
                    break;
                case HtmlInline htmlInline:
                    if (!TryCreateTextRange(htmlInline.Span, sourceText, out TextRange htmlRange))
                    {
                        AddUnreliableSourceSpanDiagnostic(
                            diagnostics,
                            MarkdownProtectedRangeKinds.InlineHtmlTag,
                            htmlInline.Span);
                        break;
                    }

                    ReadOnlySpan<char> htmlText = sourceText.AsSpan(
                        htmlRange.Start,
                        htmlRange.Length);
                    string htmlKind = IsInlineHtmlComment(htmlText)
                        ? MarkdownProtectedRangeKinds.HtmlComment
                        : MarkdownProtectedRangeKinds.InlineHtmlTag;
                    AddRequiredRange(
                        slices,
                        diagnostics,
                        htmlKind,
                        htmlRange.Start,
                        htmlRange.Length,
                        sourceText);
                    if (!IsNonPairedRawInlineHtml(htmlText))
                    {
                        AddInlineHtmlTagContext(
                            inlineHtmlTagContexts,
                            diagnostics,
                            htmlInline,
                            sourceText);
                    }

                    break;
                case LinkInline linkInline:
                    if (linkInline.IsAutoLink)
                    {
                        AddRequiredSpan(
                            slices,
                            diagnostics,
                            MarkdownProtectedRangeKinds.Autolink,
                            linkInline.Span,
                            sourceText);
                    }

                    AddOptionalSpan(
                        slices,
                        diagnostics,
                        MarkdownProtectedRangeKinds.LinkDestination,
                        linkInline.UrlSpan,
                        sourceText);
                    AddOptionalSpan(
                        slices,
                        diagnostics,
                        MarkdownProtectedRangeKinds.LinkTitle,
                        linkInline.TitleSpan,
                        sourceText);
                    if (linkInline.Reference is not null)
                    {
                        AddOptionalSpan(
                            slices,
                            diagnostics,
                            MarkdownProtectedRangeKinds.ReferenceLabel,
                            linkInline.LabelSpan,
                            sourceText);
                    }

                    CollectLinkInlineStructuralSyntax(sourceText, slices, diagnostics, linkInline);
                    break;
                case LiteralInline literalInline:
                    machineTokenCandidateSpans.Add(literalInline.Span);
                    break;
            }
        }
    }

    private static void CollectEarlyMachineTokens(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        List<SourceSpan> machineTokenCandidateSpans)
    {
        foreach (SourceSpan span in machineTokenCandidateSpans)
        {
            AddEarlyMachineTokens(slices, diagnostics, span, sourceText);
        }
    }

    private static void AddEarlyMachineTokens(
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        SourceSpan span,
        string sourceText)
    {
        if (span.Start < 0 || span.End < span.Start)
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.MachineToken,
                span);
            return;
        }

        TextRange range = new(span.Start, checked(span.End - span.Start + 1));
        if (!range.IsWithin(sourceText))
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.MachineToken,
                span);
            return;
        }

        foreach (ProtectedSlice machineToken in
            MarkdownTokenProtector.ScanEarlyMachineTokens(sourceText, range))
        {
            if (!OverlapsExistingProtectedRange(machineToken.SourceRange, slices))
            {
                slices.Add(machineToken);
            }
        }
    }

    private static void CollectLinkInlineStructuralSyntax(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        LinkInline linkInline)
    {
        if (linkInline.IsAutoLink
            || !TryCreateTextRange(linkInline.Span, sourceText, out TextRange linkRange))
        {
            return;
        }

        int linkEnd = linkRange.End;
        int openBracket = FindLinkOpeningBracket(sourceText, linkRange, linkInline.IsImage);
        if (openBracket < 0)
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                linkInline.Span);
            return;
        }

        if (linkInline.IsImage)
        {
            AddStructuralSyntaxRange(sourceText, slices, diagnostics, openBracket - 1, 1);
        }

        AddStructuralSyntaxRange(sourceText, slices, diagnostics, openBracket, 1);
        List<TextRange> childInlineSkipRanges = GetLinkDisplayTextDelimiterSkipRanges(
            linkInline,
            sourceText);
        int closeBracket = FindMatchingSquareBracket(
            sourceText,
            openBracket,
            linkEnd,
            childInlineSkipRanges);
        if (closeBracket < 0)
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                linkInline.Span);
            return;
        }

        AddStructuralSyntaxRange(sourceText, slices, diagnostics, closeBracket, 1);
        int suffixStart = closeBracket + 1;
        if (suffixStart >= linkEnd)
        {
            return;
        }

        if (sourceText[suffixStart] == '(')
        {
            AddStructuralSyntaxRange(sourceText, slices, diagnostics, suffixStart, 1);
            if (sourceText[linkEnd - 1] == ')')
            {
                AddStructuralSyntaxRange(sourceText, slices, diagnostics, linkEnd - 1, 1);
            }

            return;
        }

        if (sourceText[suffixStart] == '[')
        {
            AddStructuralSyntaxRange(sourceText, slices, diagnostics, suffixStart, 1);
            int labelCloseBracket = FindMatchingSquareBracket(
                sourceText,
                suffixStart,
                linkEnd,
                childInlineSkipRanges);
            if (labelCloseBracket >= 0)
            {
                AddStructuralSyntaxRange(sourceText, slices, diagnostics, labelCloseBracket, 1);
            }
        }
    }

    private static List<TextRange> GetLinkDisplayTextDelimiterSkipRanges(
        LinkInline linkInline,
        string sourceText)
    {
        List<TextRange>? ranges = null;
        foreach (Inline inline in linkInline.Descendants().OfType<Inline>())
        {
            if (inline is not CodeInline and not HtmlInline)
            {
                continue;
            }

            if (!TryCreateTextRange(inline.Span, sourceText, out TextRange range))
            {
                continue;
            }

            ranges ??= [];
            ranges.Add(range);
        }

        if (ranges is null)
        {
            return [];
        }

        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return ranges;
    }

    private static int FindLinkOpeningBracket(
        string sourceText,
        TextRange linkRange,
        bool isImage)
    {
        if (isImage)
        {
            if (linkRange.Length >= 2
                && sourceText[linkRange.Start] == '!'
                && sourceText[linkRange.Start + 1] == '[')
            {
                return linkRange.Start + 1;
            }

            for (int position = linkRange.Start + 1; position < linkRange.End; position++)
            {
                if (sourceText[position - 1] == '!' && sourceText[position] == '[')
                {
                    return position;
                }
            }

            return -1;
        }

        return sourceText[linkRange.Start] == '[' ? linkRange.Start : -1;
    }

    private static int FindMatchingSquareBracket(
        string sourceText,
        int openBracket,
        int end,
        List<TextRange> skipRanges)
    {
        int depth = 0;
        for (int position = openBracket; position < end; position++)
        {
            if (TryGetDelimiterSkipRange(position, skipRanges, out TextRange skipRange))
            {
                position = skipRange.End - 1;
                continue;
            }

            if (IsEscapedDelimiter(sourceText, position, openBracket))
            {
                continue;
            }

            if (sourceText[position] == '[')
            {
                depth++;
            }
            else if (sourceText[position] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return position;
                }
            }
        }

        return -1;
    }

    private static bool TryGetDelimiterSkipRange(
        int position,
        List<TextRange> skipRanges,
        out TextRange skipRange)
    {
        foreach (TextRange range in skipRanges)
        {
            if (position < range.Start)
            {
                break;
            }

            if (position >= range.End)
            {
                continue;
            }

            skipRange = range;
            return true;
        }

        skipRange = default;
        return false;
    }

    private static void CollectInlineHtmlEnclosureText(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        IReadOnlyList<InlineHtmlTagContext> inlineHtmlTagContexts)
    {
        List<InlineHtmlTagInfo> inlineHtmlTags = [];
        IEnumerable<InlineHtmlTagContext> orderedContexts =
            inlineHtmlTagContexts.OrderBy(context => context.TagRange.Start);
        foreach (InlineHtmlTagContext context in orderedContexts)
        {
            if (!TryParseInlineHtmlTag(
                sourceText,
                context.TagRange,
                context.ContainerRange,
                out InlineHtmlTagInfo? tagInfo))
            {
                AddUnsupportedInlineHtmlDiagnostic(diagnostics);
                return;
            }

            inlineHtmlTags.Add(tagInfo!);
        }

        IEnumerable<IGrouping<TextRange, InlineHtmlTagInfo>> tagGroups =
            inlineHtmlTags.GroupBy(tag => tag.ContainerRange);
        foreach (IGrouping<TextRange, InlineHtmlTagInfo> tagGroup in tagGroups)
        {
            CollectInlineHtmlEnclosureTextInContainer(sourceText, slices, diagnostics, tagGroup);
            if (diagnostics.Count > 0)
            {
                return;
            }
        }
    }

    private static void CollectInlineHtmlEnclosureTextInContainer(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        IEnumerable<InlineHtmlTagInfo> inlineHtmlTags)
    {
        InlineHtmlTagInfo? openTag = null;
        foreach (InlineHtmlTagInfo tag in inlineHtmlTags)
        {
            bool isSelfClosingTag = tag.IsSelfClosing || IsHtmlVoidElement(tag.Name);
            if (isSelfClosingTag && !tag.IsClosing)
            {
                continue;
            }

            if (tag.IsClosing)
            {
                if (openTag is null
                    || !string.Equals(
                        openTag.Name,
                        tag.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    AddUnsupportedInlineHtmlDiagnostic(diagnostics);
                    return;
                }

                int contentStart = openTag.SourceRange.End;
                int contentLength = tag.SourceRange.Start - contentStart;
                if (contentLength < 0
                    || contentStart < tag.ContainerRange.Start
                    || tag.SourceRange.End > tag.ContainerRange.End
                    || sourceText.AsSpan(contentStart, contentLength).Contains('<')
                    || ContainsParagraphBoundary(sourceText.AsSpan(contentStart, contentLength)))
                {
                    AddUnsupportedInlineHtmlDiagnostic(diagnostics);
                    return;
                }

                if (contentLength > 0)
                {
                    TextRange contentRange = new(contentStart, contentLength);
                    RemoveContainedInlineHtmlEnclosureSlices(slices, contentRange);
                    AddRequiredRange(
                        slices,
                        diagnostics,
                        MarkdownProtectedRangeKinds.InlineHtmlEnclosureText,
                        contentStart,
                        contentLength,
                        sourceText);
                }

                openTag = null;
                continue;
            }

            if (openTag is not null)
            {
                AddUnsupportedInlineHtmlDiagnostic(diagnostics);
                return;
            }

            openTag = tag;
        }

        if (openTag is not null)
        {
            AddUnsupportedInlineHtmlDiagnostic(diagnostics);
        }
    }

    private static void RemoveContainedInlineHtmlEnclosureSlices(
        List<ProtectedSlice> slices,
        TextRange enclosureRange) =>
        slices.RemoveAll(slice =>
            IsNonessentialInsideInlineHtmlEnclosureKind(slice.Kind)
            && RangeContains(enclosureRange, slice.SourceRange));

    private static bool IsNonessentialInsideInlineHtmlEnclosureKind(string kind) =>
        kind is MarkdownProtectedRangeKinds.LinkDestination
            or MarkdownProtectedRangeKinds.LinkTitle
            or MarkdownProtectedRangeKinds.ReferenceLabel
            or MarkdownProtectedRangeKinds.ReferenceDefinition
            or MarkdownProtectedRangeKinds.FootnoteDefinition
            or MarkdownProtectedRangeKinds.FootnoteReference
            or MarkdownProtectedRangeKinds.Autolink
            or MarkdownProtectedRangeKinds.UrlLiteral
            or MarkdownProtectedRangeKinds.EmailLiteral
            or MarkdownProtectedRangeKinds.UriFragment
            or MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
            or MarkdownProtectedRangeKinds.EscapedMarkdownDelimiter;

    private static bool RangeContains(TextRange outer, TextRange inner) =>
        inner.Start >= outer.Start && inner.End <= outer.End;

    private static void AddInlineHtmlTagContext(
        List<InlineHtmlTagContext> inlineHtmlTagContexts,
        List<MarkdownDiagnostic> diagnostics,
        HtmlInline htmlInline,
        string sourceText)
    {
        if (!TryCreateTextRange(htmlInline.Span, sourceText, out TextRange tagRange)
            || !TryGetInlinePairingContainerRange(
                htmlInline,
                tagRange,
                sourceText,
                out TextRange containerRange))
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.InlineHtmlTag,
                htmlInline.Span);
            return;
        }

        inlineHtmlTagContexts.Add(new InlineHtmlTagContext(tagRange, containerRange));
    }

    private static bool TryGetInlinePairingContainerRange(
        Inline inline,
        TextRange tagRange,
        string sourceText,
        out TextRange containerRange)
    {
        LeafBlock? rootLeafBlock = null;
        Inline? parent = inline.Parent as Inline;
        while (parent is not null)
        {
            if (parent is ContainerInline { ParentBlock: LeafBlock leafBlock }
                && parent.Parent is null)
            {
                rootLeafBlock = leafBlock;
                parent = parent.Parent as Inline;
                continue;
            }

            if (parent is LinkInline && parent.Span.Start == inline.Span.Start)
            {
                parent = parent.Parent as Inline;
                continue;
            }

            if (parent is LinkDelimiterInline && IsClosingInlineHtmlTag(sourceText, tagRange))
            {
                parent = parent.Parent as Inline;
                continue;
            }

            return TryCreateTextRange(parent.Span, sourceText, out containerRange);
        }

        SourceSpan span = rootLeafBlock is not null ? rootLeafBlock.Span : inline.Span;
        return TryCreateTextRange(span, sourceText, out containerRange);
    }

    private static bool IsClosingInlineHtmlTag(string sourceText, TextRange tagRange) =>
        tagRange.Length >= 2
            && tagRange.IsWithin(sourceText)
            && sourceText[tagRange.Start] == '<'
            && sourceText[tagRange.Start + 1] == '/';

    private static bool IsHtmlVoidElement(string tagName) =>
        tagName.Equals("area", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("base", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("br", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("col", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("embed", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("hr", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("img", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("input", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("link", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("meta", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("param", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("source", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("track", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("wbr", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsParagraphBoundary(ReadOnlySpan<char> text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                continue;
            }

            int nextLineStart = index + 1;
            if (text[index] == '\r' && nextLineStart < text.Length && text[nextLineStart] == '\n')
            {
                nextLineStart++;
            }

            int scan = nextLineStart;
            while (scan < text.Length && text[scan] is ' ' or '\t')
            {
                scan++;
            }

            if (scan < text.Length && text[scan] is '\r' or '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static void AddUnsupportedInlineHtmlDiagnostic(List<MarkdownDiagnostic> diagnostics) =>
        diagnostics.Add(new MarkdownDiagnostic(
            MarkdownFailureKind.UnsupportedSyntax,
            "Nested, unmatched, or mismatched paired inline HTML is not supported "
                + "in Markdown-aware translation."));

    private static bool IsInlineHtmlComment(ReadOnlySpan<char> htmlText) =>
        htmlText.StartsWith("<!--", StringComparison.Ordinal);

    private static bool IsNonPairedRawInlineHtml(ReadOnlySpan<char> htmlText) =>
        htmlText.Length >= 2 && htmlText[0] == '<' && htmlText[1] is '!' or '?';

    private static bool TryParseInlineHtmlTag(
        string sourceText,
        TextRange range,
        TextRange containerRange,
        out InlineHtmlTagInfo? tagInfo)
    {
        tagInfo = null;
        if (!range.IsWithin(sourceText) || range.Length < 3 || sourceText[range.Start] != '<')
        {
            return false;
        }

        int end = range.End;
        int position = range.Start + 1;
        bool isClosing = position < end && sourceText[position] == '/';
        if (isClosing)
        {
            position++;
        }

        while (position < end && char.IsWhiteSpace(sourceText[position]))
        {
            position++;
        }

        int nameStart = position;
        if (position >= end || !char.IsAsciiLetter(sourceText[position]))
        {
            return false;
        }

        position++;
        while (position < end
            && (char.IsAsciiLetterOrDigit(sourceText[position]) || sourceText[position] == '-'))
        {
            position++;
        }

        string name = sourceText[nameStart..position];
        char quote = '\0';
        for (; position < end; position++)
        {
            char value = sourceText[position];
            if (quote != '\0')
            {
                if (value == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (value is '"' or '\'')
            {
                quote = value;
                continue;
            }

            if (value == '>')
            {
                if (position != end - 1)
                {
                    return false;
                }

                tagInfo = new InlineHtmlTagInfo(
                    name,
                    range,
                    containerRange,
                    isClosing,
                    HasSelfClosingSlashBefore(sourceText, position));
                return true;
            }
        }

        return false;
    }

    private static bool HasSelfClosingSlashBefore(string sourceText, int closeBracketIndex)
    {
        for (int position = closeBracketIndex - 1; position >= 0; position--)
        {
            if (char.IsWhiteSpace(sourceText[position]))
            {
                continue;
            }

            return sourceText[position] == '/';
        }

        return false;
    }

    private static void CollectReferenceDefinitions(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (ReferenceDefinitionRange range in ScanReferenceDefinitions(sourceText))
        {
            TextRange sourceRange = new(range.Start, range.Length);
            if (OverlapsOpaqueProtectedRange(sourceRange, slices))
            {
                continue;
            }

            AddRequiredRange(
                slices,
                diagnostics,
                MarkdownProtectedRangeKinds.ReferenceDefinition,
                range.Start,
                range.Length,
                sourceText);
        }
    }

    private static IEnumerable<ReferenceDefinitionRange> ScanReferenceDefinitions(string sourceText)
    {
        IReadOnlyList<MarkdownSourceLine> lines = SplitSourceLines(sourceText);
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (TryScanReferenceDefinition(
                lines,
                lineIndex,
                sourceText,
                out ReferenceDefinitionRange range))
            {
                yield return range;
            }
        }
    }

    private static void CollectFootnotes(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (ReferenceDefinitionRange range in ScanFootnoteDefinitions(sourceText))
        {
            TextRange sourceRange = new(range.Start, range.Length);
            if (OverlapsOpaqueProtectedRange(sourceRange, slices))
            {
                continue;
            }

            AddRequiredRange(
                slices,
                diagnostics,
                MarkdownProtectedRangeKinds.FootnoteDefinition,
                range.Start,
                range.Length,
                sourceText);
        }

        foreach (ReferenceDefinitionRange range in ScanFootnoteReferences(sourceText))
        {
            TextRange sourceRange = new(range.Start, range.Length);
            if (OverlapsExistingProtectedRange(sourceRange, slices))
            {
                continue;
            }

            AddRequiredRange(
                slices,
                diagnostics,
                MarkdownProtectedRangeKinds.FootnoteReference,
                range.Start,
                range.Length,
                sourceText);
        }
    }

    private static IEnumerable<ReferenceDefinitionRange> ScanFootnoteDefinitions(string sourceText)
    {
        IReadOnlyList<MarkdownSourceLine> lines = SplitSourceLines(sourceText);
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (TryScanFootnoteDefinition(
                lines,
                lineIndex,
                sourceText,
                out ReferenceDefinitionRange range))
            {
                yield return range;
            }
        }
    }

    private static bool TryScanFootnoteDefinition(
        IReadOnlyList<MarkdownSourceLine> lines,
        int lineIndex,
        string sourceText,
        out ReferenceDefinitionRange range)
    {
        range = default;
        MarkdownSourceLine contentLine = GetContainerAwareContentLine(
            sourceText,
            lines,
            lineIndex,
            out _);
        int position = contentLine.Start;
        int indent = CountLeadingSpaces(sourceText, position, contentLine.ContentEnd);
        if (indent > 3)
        {
            return false;
        }

        position += indent;
        if (!TryScanFootnoteLabel(sourceText, position, contentLine.ContentEnd, out int labelEnd)
            || labelEnd + 1 >= contentLine.ContentEnd
            || sourceText[labelEnd + 1] != ':')
        {
            return false;
        }

        range = new ReferenceDefinitionRange(position, labelEnd + 2 - position);
        return true;
    }

    private static IEnumerable<ReferenceDefinitionRange> ScanFootnoteReferences(string sourceText)
    {
        IReadOnlyList<MarkdownSourceLine> lines = SplitSourceLines(sourceText);
        foreach (MarkdownSourceLine line in lines)
        {
            for (int position = line.Start; position < line.ContentEnd - 2; position++)
            {
                if (IsEscapedDelimiter(sourceText, position, line.Start)
                    || !TryScanFootnoteLabel(
                        sourceText,
                        position,
                        line.ContentEnd,
                        out int labelEnd))
                {
                    continue;
                }

                if (labelEnd + 1 < line.ContentEnd && sourceText[labelEnd + 1] == ':')
                {
                    position = labelEnd;
                    continue;
                }

                yield return new ReferenceDefinitionRange(position, labelEnd - position + 1);
                position = labelEnd;
            }
        }
    }

    private static bool TryScanFootnoteLabel(
        string sourceText,
        int labelStart,
        int end,
        out int labelEnd)
    {
        if (labelStart + 2 >= end
            || sourceText[labelStart] != '['
            || sourceText[labelStart + 1] != '^'
            || !TryScanReferenceLabel(sourceText, labelStart, end, out labelEnd))
        {
            labelEnd = 0;
            return false;
        }

        return labelEnd > labelStart + 2;
    }

    private static List<MarkdownSourceLine> SplitSourceLines(string sourceText)
    {
        List<MarkdownSourceLine> lines = [];
        int start = 0;
        for (int index = 0; index < sourceText.Length; index++)
        {
            if (sourceText[index] == '\r')
            {
                int endingLength = index + 1 < sourceText.Length
                    && sourceText[index + 1] == '\n'
                        ? 2
                        : 1;
                lines.Add(new MarkdownSourceLine(start, index, index + endingLength));
                index += endingLength - 1;
                start = index + 1;
            }
            else if (sourceText[index] == '\n')
            {
                lines.Add(new MarkdownSourceLine(start, index, index + 1));
                start = index + 1;
            }
        }

        if (start < sourceText.Length)
        {
            lines.Add(new MarkdownSourceLine(start, sourceText.Length, sourceText.Length));
        }

        return lines;
    }

    private static MarkdownSourceLine GetContainerAwareContentLine(
        string sourceText,
        MarkdownSourceLine line)
    {
        return GetExplicitContainerAwareContentLine(
            sourceText,
            line,
            suppressNonOneOrderedListInterruption: false,
            out _);
    }

    private static MarkdownSourceLine GetContainerAwareContentLine(
        string sourceText,
        IReadOnlyList<MarkdownSourceLine> lines,
        int lineIndex,
        out MarkdownContainerContext containerContext)
    {
        MarkdownContainerContext activeContext = MarkdownContainerContext.Empty;
        for (int currentLineIndex = 0; currentLineIndex <= lineIndex; currentLineIndex++)
        {
            MarkdownSourceLine currentLine = GetExplicitContainerAwareContentLine(
                sourceText,
                lines[currentLineIndex],
                IsNonOneOrderedListParagraphInterruption(
                    sourceText,
                    lines,
                    currentLineIndex,
                    activeContext),
                out MarkdownContainerContext currentContext);
            if (currentContext.Markers.Count == 0
                && activeContext.Markers.Count > 0
                && TryGetContinuationContentLine(
                    sourceText,
                    lines[currentLineIndex],
                    activeContext,
                    out MarkdownSourceLine continuationLine))
            {
                currentLine = continuationLine;
                currentContext = activeContext;
            }

            if (currentLineIndex == lineIndex)
            {
                containerContext = currentContext;
                return currentLine;
            }

            if (currentContext.Markers.Count > 0)
            {
                activeContext = currentContext;
            }
            else if (!IsBlankLine(sourceText, lines[currentLineIndex]))
            {
                activeContext = MarkdownContainerContext.Empty;
            }
        }

        containerContext = MarkdownContainerContext.Empty;
        return lines[lineIndex];
    }

    private static MarkdownSourceLine GetExplicitContainerAwareContentLine(
        string sourceText,
        MarkdownSourceLine line,
        bool suppressNonOneOrderedListInterruption,
        out MarkdownContainerContext containerContext)
    {
        int position = line.Start;
        List<MarkdownContainerMarker> markers = [];
        while (position < line.ContentEnd)
        {
            int containerStart = position;
            int markerStart = position + CountLeadingSpaces(sourceText, position, line.ContentEnd);
            if (markerStart - position > 3 || markerStart >= line.ContentEnd)
            {
                break;
            }

            if (sourceText[markerStart] == '>')
            {
                position = markerStart + 1;
                if (position < line.ContentEnd && sourceText[position] is ' ' or '\t')
                {
                    position++;
                }

                markers.Add(new MarkdownContainerMarker(
                    MarkdownContainerMarkerKind.Blockquote,
                    ContinuationIndent: 0));
                continue;
            }

            if (TryScanListItemMarker(
                sourceText,
                markerStart,
                line.ContentEnd,
                out int markerEnd,
                out bool isOrdered,
                out int orderedStartNumber))
            {
                if (suppressNonOneOrderedListInterruption
                    && isOrdered
                    && orderedStartNumber != 1)
                {
                    break;
                }

                position = markerEnd;
                SkipSpacesAndTabs(sourceText, ref position, line.ContentEnd);
                markers.Add(new MarkdownContainerMarker(
                    MarkdownContainerMarkerKind.ListItem,
                    position - containerStart));
                continue;
            }

            break;
        }

        containerContext = new MarkdownContainerContext(markers.ToArray());
        return line with { Start = position };
    }

    private static bool IsNonOneOrderedListParagraphInterruption(
        string sourceText,
        IReadOnlyList<MarkdownSourceLine> lines,
        int lineIndex,
        MarkdownContainerContext activeContext)
    {
        if (lineIndex <= 0
            || IsBlankLine(sourceText, lines[lineIndex - 1])
            || !TryGetNonOneOrderedListInterruptionCandidateLine(
                sourceText,
                lines[lineIndex],
                activeContext,
                out MarkdownSourceLine candidateLine,
                out MarkdownContainerContext candidateContext))
        {
            return false;
        }

        MarkdownSourceLine previousLine = GetExplicitContainerAwareContentLine(
            sourceText,
            lines[lineIndex - 1],
            suppressNonOneOrderedListInterruption: false,
            out MarkdownContainerContext previousContext);

        if (previousContext.Markers.Count == 0
            && activeContext.Markers.Count > 0
            && TryGetContinuationContentLine(
                sourceText,
                lines[lineIndex - 1],
                activeContext,
                out MarkdownSourceLine previousContinuationLine))
        {
            previousLine = previousContinuationLine;
            previousContext = activeContext;
        }

        return ContainerContextsEqual(candidateContext, previousContext)
            && IsParagraphLikeLine(sourceText, previousLine);
    }

    private static bool ContainerContextsEqual(
        MarkdownContainerContext left,
        MarkdownContainerContext right) =>
        left.Markers.SequenceEqual(right.Markers);

    private static bool TryGetNonOneOrderedListInterruptionCandidateLine(
        string sourceText,
        MarkdownSourceLine line,
        MarkdownContainerContext activeContext,
        out MarkdownSourceLine candidateLine,
        out MarkdownContainerContext candidateContext)
    {
        candidateLine = GetExplicitContainerAwareContentLine(
            sourceText,
            line,
            suppressNonOneOrderedListInterruption: true,
            out candidateContext);
        if (candidateContext.Markers.Count == 0
            && activeContext.Markers.Count > 0
            && TryGetContinuationContentLine(
                sourceText,
                line,
                activeContext,
                out MarkdownSourceLine continuationLine))
        {
            candidateLine = continuationLine;
            candidateContext = activeContext;
        }

        return TryScanInitialOrderedListItemMarker(
                sourceText,
                candidateLine,
                out int orderedStartNumber)
            && orderedStartNumber != 1;
    }

    private static bool TryScanInitialOrderedListItemMarker(
        string sourceText,
        MarkdownSourceLine line,
        out int orderedStartNumber)
    {
        orderedStartNumber = 0;
        int markerStart = line.Start + CountLeadingSpaces(sourceText, line.Start, line.ContentEnd);
        return markerStart - line.Start <= 3
            && TryScanListItemMarker(
                sourceText,
                markerStart,
                line.ContentEnd,
                out _,
                out bool isOrdered,
                out orderedStartNumber)
            && isOrdered;
    }

    private static bool IsParagraphLikeLine(string sourceText, MarkdownSourceLine line)
    {
        int contentStart = line.Start + CountLeadingSpaces(sourceText, line.Start, line.ContentEnd);
        if (contentStart >= line.ContentEnd || contentStart - line.Start > 3)
        {
            return false;
        }

        if (sourceText[contentStart] == '#'
            || IsFencedCodeBlockOpening(sourceText, contentStart, line.ContentEnd))
        {
            return false;
        }

        char delimiter = sourceText[contentStart];
        if (delimiter is '-' or '_' or '*')
        {
            int delimiterCount = 0;
            for (int position = contentStart; position < line.ContentEnd; position++)
            {
                char value = sourceText[position];
                if (value == delimiter)
                {
                    delimiterCount++;
                    continue;
                }

                if (value is not (' ' or '\t'))
                {
                    return true;
                }
            }

            return delimiterCount < 3;
        }

        return true;
    }

    private static bool IsFencedCodeBlockOpening(string sourceText, int start, int lineEnd)
    {
        char delimiter = sourceText[start];
        if (delimiter is not ('`' or '~'))
        {
            return false;
        }

        int count = 0;
        while (start + count < lineEnd && sourceText[start + count] == delimiter)
        {
            count++;
        }

        return count >= 3;
    }

    private static bool TryGetContinuationContentLine(
        string sourceText,
        MarkdownSourceLine line,
        MarkdownContainerContext containerContext,
        out MarkdownSourceLine contentLine)
    {
        contentLine = line;
        int position = line.Start;
        foreach (MarkdownContainerMarker marker in containerContext.Markers)
        {
            if (marker.Kind == MarkdownContainerMarkerKind.Blockquote)
            {
                int markerStart = position
                    + CountLeadingSpaces(sourceText, position, line.ContentEnd);
                if (markerStart - position > 3
                    || markerStart >= line.ContentEnd
                    || sourceText[markerStart] != '>')
                {
                    return false;
                }

                position = markerStart + 1;
                if (position < line.ContentEnd && sourceText[position] is ' ' or '\t')
                {
                    position++;
                }

                continue;
            }

            int indentation = CountLeadingSpaces(sourceText, position, line.ContentEnd);
            if (indentation < marker.ContinuationIndent)
            {
                return false;
            }

            position += marker.ContinuationIndent;
        }

        contentLine = line with { Start = position };
        return true;
    }

    private static bool IsBlankLine(string sourceText, MarkdownSourceLine line)
    {
        for (int position = line.Start; position < line.ContentEnd; position++)
        {
            if (sourceText[position] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static int FindLineContentEnd(string sourceText, int start)
    {
        int position = start;
        while (position < sourceText.Length && sourceText[position] is not ('\r' or '\n'))
        {
            position++;
        }

        return position;
    }

    private static bool TryScanReferenceDefinition(
        IReadOnlyList<MarkdownSourceLine> lines,
        int lineIndex,
        string sourceText,
        out ReferenceDefinitionRange range)
    {
        range = default;
        MarkdownSourceLine line = GetContainerAwareContentLine(
            sourceText,
            lines,
            lineIndex,
            out MarkdownContainerContext containerContext);
        int position = line.Start;
        int indent = CountLeadingSpaces(sourceText, position, line.ContentEnd);
        if (indent > 3)
        {
            return false;
        }

        position += indent;
        if (position >= line.ContentEnd || sourceText[position] != '[')
        {
            return false;
        }

        if (!TryScanReferenceLabel(sourceText, position, line.ContentEnd, out int labelEnd)
            || labelEnd + 1 >= line.ContentEnd
            || sourceText[labelEnd + 1] != ':')
        {
            return false;
        }

        if (sourceText[position + 1] == '^')
        {
            return false;
        }

        position = labelEnd + 2;
        SkipSpacesAndTabs(sourceText, ref position, line.ContentEnd);
        position = SkipLinkDestination(sourceText, position, line.ContentEnd);
        SkipSpacesAndTabs(sourceText, ref position, line.ContentEnd);

        int definitionEnd = line.End;
        if (TryScanReferenceTitle(
            lines,
            lineIndex,
            position,
            sourceText,
            containerContext,
            out int titleEnd))
        {
            definitionEnd = titleEnd;
        }
        else if (position < line.ContentEnd)
        {
            return false;
        }

        range = new ReferenceDefinitionRange(line.Start, definitionEnd - line.Start);
        return true;
    }

    private static bool TryScanReferenceLabel(
        string sourceText,
        int labelStart,
        int end,
        out int labelEnd)
    {
        labelEnd = 0;
        bool hasLabelContent = false;
        int labelCharacterCount = 0;
        for (int position = labelStart + 1; position < end; position++)
        {
            char value = sourceText[position];
            if (value == '\\')
            {
                if (position + 1 >= end)
                {
                    return false;
                }

                hasLabelContent = true;
                labelCharacterCount++;
                if (labelCharacterCount > MaxReferenceLabelLength)
                {
                    return false;
                }

                position++;
                continue;
            }

            if (value == '[')
            {
                return false;
            }

            if (value == ']')
            {
                labelEnd = position;
                return hasLabelContent;
            }

            hasLabelContent = true;
            labelCharacterCount++;
            if (labelCharacterCount > MaxReferenceLabelLength)
            {
                return false;
            }
        }

        return false;
    }

    private static int CountLeadingSpaces(string sourceText, int start, int end)
    {
        int count = 0;
        while (start + count < end && sourceText[start + count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static void SkipSpacesAndTabs(string sourceText, ref int position, int end)
    {
        while (position < end && sourceText[position] is ' ' or '\t')
        {
            position++;
        }
    }

    private static int SkipLinkDestination(string sourceText, int position, int end)
    {
        if (position >= end)
        {
            return position;
        }

        if (sourceText[position] == '<')
        {
            int angleStart = position;
            position++;
            while (position < end)
            {
                char value = sourceText[position];
                if (value == '\\')
                {
                    if (position + 1 >= end)
                    {
                        return angleStart;
                    }

                    position += 2;
                    continue;
                }

                if (value == '<')
                {
                    return angleStart;
                }

                if (value == '>')
                {
                    return position + 1;
                }

                position++;
            }

            return angleStart;
        }

        int start = position;
        int parenthesisDepth = 0;
        while (position < end && !char.IsWhiteSpace(sourceText[position]))
        {
            char value = sourceText[position];
            if (value == '(' && !IsEscapedDelimiter(sourceText, position, start))
            {
                parenthesisDepth++;
            }
            else if (value == ')' && !IsEscapedDelimiter(sourceText, position, start))
            {
                if (parenthesisDepth == 0)
                {
                    return start;
                }

                parenthesisDepth--;
            }

            position++;
        }

        return parenthesisDepth == 0 ? position : start;
    }

    private static bool TryScanReferenceTitle(
        IReadOnlyList<MarkdownSourceLine> lines,
        int lineIndex,
        int position,
        string sourceText,
        MarkdownContainerContext containerContext,
        out int titleEnd)
    {
        titleEnd = 0;
        MarkdownSourceLine line = lines[lineIndex];
        SkipSpacesAndTabs(sourceText, ref position, line.ContentEnd);
        if (position >= line.ContentEnd)
        {
            if (lineIndex + 1 >= lines.Count)
            {
                return false;
            }

            lineIndex++;
            if (!TryGetContinuationContentLine(
                sourceText,
                lines[lineIndex],
                containerContext,
                out line))
            {
                return false;
            }

            position = line.Start;
            SkipSpacesAndTabs(sourceText, ref position, line.ContentEnd);
        }

        if (position >= line.ContentEnd)
        {
            return false;
        }

        char opener = sourceText[position];
        char closer = opener switch
        {
            '"' => '"',
            '\'' => '\'',
            '(' => ')',
            _ => '\0',
        };
        if (closer == '\0')
        {
            return false;
        }

        for (int scanLineIndex = lineIndex; scanLineIndex < lines.Count; scanLineIndex++)
        {
            MarkdownSourceLine scanLine;
            if (scanLineIndex == lineIndex)
            {
                scanLine = line;
            }
            else if (!TryGetContinuationContentLine(
                sourceText,
                lines[scanLineIndex],
                containerContext,
                out scanLine))
            {
                return false;
            }

            int scanStart = scanLineIndex == lineIndex ? position + 1 : scanLine.Start;
            if (IsBlankLine(sourceText, scanLine))
            {
                return false;
            }

            for (int scan = scanStart; scan < scanLine.ContentEnd; scan++)
            {
                if (sourceText[scan] != closer || IsEscapedDelimiter(sourceText, scan, scanStart))
                {
                    continue;
                }

                int afterCloser = scan + 1;
                SkipSpacesAndTabs(sourceText, ref afterCloser, scanLine.ContentEnd);
                if (afterCloser == scanLine.ContentEnd)
                {
                    titleEnd = scanLine.End;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEscapedDelimiter(string sourceText, int delimiterIndex, int scanStart)
    {
        int slashCount = 0;
        for (int scan = delimiterIndex - 1; scan >= scanStart && sourceText[scan] == '\\'; scan--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private static void CollectRegexMatches(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        Regex regex,
        string kind)
    {
        foreach (Match match in regex.Matches(sourceText))
        {
            TextRange sourceRange = new(match.Index, match.Length);
            if (OverlapsOpaqueProtectedRange(sourceRange, slices))
            {
                continue;
            }

            if (kind == MarkdownProtectedRangeKinds.Autolink
                && OverlapsProtectedRangeOfKind(sourceRange, slices, kind))
            {
                continue;
            }

            AddRequiredRange(slices, diagnostics, kind, match.Index, match.Length, sourceText);
        }
    }

    private static void CollectUrlLiterals(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (Match match in UrlLiteralRegex().Matches(sourceText))
        {
            int length = TrimGfmTrailingPunctuation(sourceText, match.Index, match.Length);
            length = TrimTrailingUnbalancedCloseParentheses(sourceText, match.Index, length);
            length = TrimGfmTrailingPunctuation(sourceText, match.Index, length);
            TextRange sourceRange = new(match.Index, length);
            if (OverlapsOpaqueProtectedRange(sourceRange, slices))
            {
                continue;
            }

            AddRequiredRange(
                slices,
                diagnostics,
                MarkdownProtectedRangeKinds.UrlLiteral,
                match.Index,
                length,
                sourceText);
        }
    }

    private static void CollectUriFragments(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (Match match in UriFragmentRegex().Matches(sourceText))
        {
            if (IsEscapedDelimiter(sourceText, match.Index, 0))
            {
                continue;
            }

            bool isInsideFragmentOwningRange =
                IsInsideFragmentOwningProtectedRange(slices, match.Index);
            if (!isInsideFragmentOwningRange
                && !HasStandaloneUriFragmentLeftBoundary(sourceText, match.Index))
            {
                continue;
            }

            int length = TrimTrailingUnbalancedCloseParentheses(
                sourceText,
                match.Index,
                match.Length);
            if (!isInsideFragmentOwningRange)
            {
                length = TrimGfmTrailingPunctuation(sourceText, match.Index, length);
            }

            length = ClampUriFragmentToExistingProtectedRangeEnd(slices, match.Index, length);
            if (length <= 1)
            {
                continue;
            }

            TextRange sourceRange = new(match.Index, length);
            if (OverlapsOpaqueProtectedRange(sourceRange, slices))
            {
                continue;
            }

            AddRequiredRange(
                slices,
                diagnostics,
                MarkdownProtectedRangeKinds.UriFragment,
                match.Index,
                length,
                sourceText);
        }
    }

    private static bool HasStandaloneUriFragmentLeftBoundary(string sourceText, int start)
    {
        if (start == 0)
        {
            return true;
        }

        int boundaryIndex = start - 1;
        while (boundaryIndex >= 0 && sourceText[boundaryIndex] == '\\')
        {
            boundaryIndex--;
        }

        if (boundaryIndex < 0)
        {
            return true;
        }

        char previous = sourceText[boundaryIndex];
        return char.IsWhiteSpace(previous) || IsOpeningPunctuation(previous);
    }

    private static bool IsOpeningPunctuation(char value) =>
        value is '(' or '[' or '{' or '<' or '"' or '\'';

    private static int TrimTrailingUnbalancedCloseParentheses(
        string sourceText,
        int start,
        int length)
    {
        if (length == 0 || sourceText[start + length - 1] != ')')
        {
            return length;
        }

        Stack<int> unmatchedOpenParentheses = [];
        HashSet<int> unmatchedCloseParentheses = [];
        int end = start + length;
        for (int index = start; index < end; index++)
        {
            if (sourceText[index] == '(')
            {
                unmatchedOpenParentheses.Push(index);
            }
            else if (sourceText[index] == ')')
            {
                if (unmatchedOpenParentheses.Count > 0)
                {
                    unmatchedOpenParentheses.Pop();
                }
                else
                {
                    unmatchedCloseParentheses.Add(index);
                }
            }
        }

        while (length > 0 && unmatchedCloseParentheses.Contains(start + length - 1))
        {
            length--;
        }

        return length;
    }

    private static int TrimGfmTrailingPunctuation(string sourceText, int start, int length)
    {
        while (length > 0 && IsGfmTrailingPunctuation(sourceText[start + length - 1]))
        {
            length--;
        }

        return length;
    }

    private static bool IsGfmTrailingPunctuation(char value) =>
        value is '.' or ',' or ':' or '?' or '!' or '*' or '_' or '~' or '\'' or '"';

    private static bool IsInsideFragmentOwningProtectedRange(
        IReadOnlyList<ProtectedSlice> slices,
        int start)
    {
        foreach (ProtectedSlice slice in slices)
        {
            if (slice.Kind is not (
                MarkdownProtectedRangeKinds.LinkDestination
                or MarkdownProtectedRangeKinds.UrlLiteral))
            {
                continue;
            }

            if (start >= slice.SourceRange.Start && start < slice.SourceRange.End)
            {
                return true;
            }
        }

        return false;
    }

    private static int ClampUriFragmentToExistingProtectedRangeEnd(
        IReadOnlyList<ProtectedSlice> slices,
        int start,
        int length)
    {
        int end = start + length;
        foreach (ProtectedSlice slice in slices)
        {
            if (slice.Kind is not (
                MarkdownProtectedRangeKinds.LinkDestination
                or MarkdownProtectedRangeKinds.UrlLiteral))
            {
                continue;
            }

            if (start >= slice.SourceRange.Start
                && start < slice.SourceRange.End
                && end > slice.SourceRange.End)
            {
                end = slice.SourceRange.End;
            }
        }

        return end - start;
    }

    private static void CollectEscapedMarkdownDelimiters(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (Match match in EscapedMarkdownDelimiterRegex().Matches(sourceText))
        {
            int delimiterIndex = match.Index + 1;
            if (!IsEscapedDelimiter(sourceText, delimiterIndex, 0))
            {
                continue;
            }

            TextRange sourceRange = new(match.Index, match.Length);
            if (OverlapsOpaqueProtectedRange(sourceRange, slices))
            {
                continue;
            }

            AddRequiredRange(
                slices,
                diagnostics,
                MarkdownProtectedRangeKinds.EscapedMarkdownDelimiter,
                match.Index,
                match.Length,
                sourceText);
        }
    }

    private static void CollectMarkdownStructuralSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        CollectMarkdownStructuralLineSyntax(document, sourceText, slices, diagnostics);
        CollectInlineMarkdownStructuralSyntax(document, sourceText, slices, diagnostics);
    }

    private static void CollectMarkdownStructuralLineSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        IReadOnlyList<MarkdownSourceLine> lines = SplitSourceLines(sourceText);
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            MarkdownSourceLine line = lines[lineIndex];
            CollectBlockquoteStructuralSyntax(sourceText, line, slices, diagnostics);
            CollectAtxHeadingStructuralSyntax(sourceText, line, slices, diagnostics);

            MarkdownSourceLine contentLine = GetContainerAwareContentLine(
                sourceText,
                lines,
                lineIndex,
                out _);
            if (contentLine.Start != line.Start)
            {
                CollectBlockquoteStructuralSyntax(sourceText, contentLine, slices, diagnostics);
                CollectAtxHeadingStructuralSyntax(sourceText, contentLine, slices, diagnostics);
            }
        }

        CollectThematicBreakStructuralSyntax(document, sourceText, slices, diagnostics);
        CollectHardLineBreakStructuralSyntax(document, sourceText, slices, diagnostics);
        CollectSetextHeadingStructuralSyntax(document, sourceText, slices, diagnostics);
        CollectPipeTableStructuralSyntax(document, sourceText, slices, diagnostics);
        CollectListItemStructuralSyntax(document, sourceText, slices, diagnostics);
    }

    private static void CollectHardLineBreakStructuralSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (LineBreakInline lineBreakInline in document.Descendants()
            .OfType<LineBreakInline>())
        {
            if (!lineBreakInline.IsHard)
            {
                continue;
            }

            if (lineBreakInline.IsBackslash)
            {
                int markerStart = lineBreakInline.Span.Start;
                if (markerStart >= 0
                    && markerStart < sourceText.Length
                    && sourceText[markerStart] == '\\')
                {
                    AddStructuralSyntaxRange(sourceText, slices, diagnostics, markerStart, 1);
                }

                continue;
            }

            int lineEndingStart = lineBreakInline.Span.Start;
            if (lineEndingStart <= 0
                || lineEndingStart >= sourceText.Length
                || sourceText[lineEndingStart] is not ('\r' or '\n'))
            {
                continue;
            }

            int trailingSpaceStart = lineEndingStart;
            while (trailingSpaceStart > 0 && sourceText[trailingSpaceStart - 1] == ' ')
            {
                trailingSpaceStart--;
            }

            int trailingSpaceLength = lineEndingStart - trailingSpaceStart;
            if (trailingSpaceLength >= 2)
            {
                AddStructuralSyntaxRange(
                    sourceText,
                    slices,
                    diagnostics,
                    trailingSpaceStart,
                    trailingSpaceLength);
            }
        }
    }

    private static void CollectAtxHeadingStructuralSyntax(
        string sourceText,
        MarkdownSourceLine line,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        int markerStart = line.Start + CountLeadingSpaces(sourceText, line.Start, line.ContentEnd);
        if (markerStart - line.Start > 3 || markerStart >= line.ContentEnd)
        {
            return;
        }

        int markerEnd = markerStart;
        while (markerEnd < line.ContentEnd
            && markerEnd - markerStart < 6
            && sourceText[markerEnd] == '#')
        {
            markerEnd++;
        }

        if (markerEnd == markerStart
            || markerEnd < line.ContentEnd && sourceText[markerEnd] is not (' ' or '\t'))
        {
            return;
        }

        AddStructuralSyntaxRange(
            sourceText,
            slices,
            diagnostics,
            markerStart,
            markerEnd - markerStart);
        CollectClosingAtxHeadingSequence(sourceText, line, markerEnd, slices, diagnostics);
    }

    private static void CollectClosingAtxHeadingSequence(
        string sourceText,
        MarkdownSourceLine line,
        int contentStart,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        int position = line.ContentEnd - 1;
        while (position >= contentStart && sourceText[position] is ' ' or '\t')
        {
            position--;
        }

        int markerEnd = position + 1;
        while (position >= contentStart && sourceText[position] == '#')
        {
            position--;
        }

        int markerStart = position + 1;
        if (markerStart == markerEnd
            || markerStart == contentStart
            || sourceText[markerStart - 1] is not (' ' or '\t'))
        {
            return;
        }

        AddStructuralSyntaxRange(
            sourceText,
            slices,
            diagnostics,
            markerStart,
            markerEnd - markerStart);
    }

    private static void CollectBlockquoteStructuralSyntax(
        string sourceText,
        MarkdownSourceLine line,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        int position = line.Start;
        while (position < line.ContentEnd)
        {
            int markerStart = position + CountLeadingSpaces(sourceText, position, line.ContentEnd);
            if (markerStart - position > 3
                || markerStart >= line.ContentEnd)
            {
                return;
            }

            if (sourceText[markerStart] == '>')
            {
                AddStructuralSyntaxRange(sourceText, slices, diagnostics, markerStart, 1);

                position = markerStart + 1;
                if (position < line.ContentEnd && sourceText[position] is ' ' or '\t')
                {
                    position++;
                }

                continue;
            }

            if (TryScanListItemMarker(
                sourceText,
                markerStart,
                line.ContentEnd,
                out int markerEnd))
            {
                position = markerEnd;
                SkipSpacesAndTabs(sourceText, ref position, line.ContentEnd);
                continue;
            }

            return;
        }
    }

    private static void CollectThematicBreakStructuralSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        IReadOnlyList<MarkdownSourceLine> sourceLines = SplitSourceLines(sourceText);
        foreach (ThematicBreakBlock thematicBreakBlock in document.Descendants()
            .OfType<ThematicBreakBlock>())
        {
            if (!TryCreateTextRange(
                thematicBreakBlock.Span,
                sourceText,
                out TextRange thematicBreakRange))
            {
                AddUnreliableSourceSpanDiagnostic(
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    thematicBreakBlock.Span);
                continue;
            }

            bool foundMarkerLine = false;
            foreach (MarkdownSourceLine line in sourceLines)
            {
                if (!IsLineWithinRange(line, thematicBreakRange))
                {
                    continue;
                }

                MarkdownSourceLine contentLine = GetContainerAwareContentLine(sourceText, line);
                if (TryScanThematicBreakMarker(
                    sourceText,
                    contentLine,
                    out int markerStart,
                    out int markerLength))
                {
                    AddStructuralSyntaxRange(
                        sourceText,
                        slices,
                        diagnostics,
                        markerStart,
                        markerLength);
                    foundMarkerLine = true;
                    break;
                }
            }

            if (!foundMarkerLine)
            {
                AddUnreliableSourceSpanDiagnostic(
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    thematicBreakBlock.Span);
            }
        }
    }

    private static bool TryScanThematicBreakMarker(
        string sourceText,
        MarkdownSourceLine line,
        out int markerStart,
        out int markerLength)
    {
        markerStart = line.Start + CountLeadingSpaces(sourceText, line.Start, line.ContentEnd);
        markerLength = 0;
        if (markerStart - line.Start > 3 || markerStart >= line.ContentEnd)
        {
            return false;
        }

        char delimiter = sourceText[markerStart];
        if (delimiter is not ('-' or '_' or '*'))
        {
            return false;
        }

        int delimiterCount = 0;
        for (int position = markerStart; position < line.ContentEnd; position++)
        {
            char value = sourceText[position];
            if (value == delimiter)
            {
                delimiterCount++;
                continue;
            }

            if (value is not (' ' or '\t'))
            {
                return false;
            }
        }

        markerLength = line.ContentEnd - markerStart;
        return delimiterCount >= 3;
    }

    private static void CollectSetextHeadingStructuralSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        IReadOnlyList<MarkdownSourceLine> sourceLines = SplitSourceLines(sourceText);
        foreach (HeadingBlock headingBlock in document.Descendants().OfType<HeadingBlock>())
        {
            if (!headingBlock.IsSetext)
            {
                continue;
            }

            if (!TryCreateTextRange(headingBlock.Span, sourceText, out TextRange headingRange))
            {
                AddUnreliableSourceSpanDiagnostic(
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    headingBlock.Span);
                continue;
            }

            bool foundMarkerLine = false;
            for (int lineIndex = 0; lineIndex < sourceLines.Count; lineIndex++)
            {
                MarkdownSourceLine line = sourceLines[lineIndex];
                if (!IsLineWithinRange(line, headingRange)
                    || !TryScanSetextHeadingMarker(
                        sourceText,
                        GetContainerAwareContentLine(sourceText, sourceLines, lineIndex, out _),
                        out int markerStart,
                        out int markerLength))
                {
                    continue;
                }

                AddStructuralSyntaxRange(
                    sourceText,
                    slices,
                    diagnostics,
                    markerStart,
                    markerLength);
                foundMarkerLine = true;
                break;
            }

            if (!foundMarkerLine)
            {
                AddUnreliableSourceSpanDiagnostic(
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    headingBlock.Span);
            }
        }
    }

    private static bool TryScanSetextHeadingMarker(
        string sourceText,
        MarkdownSourceLine line,
        out int markerStart,
        out int markerLength)
    {
        markerStart = line.Start + CountLeadingSpaces(sourceText, line.Start, line.ContentEnd);
        markerLength = 0;
        if (markerStart - line.Start > 3 || markerStart >= line.ContentEnd)
        {
            return false;
        }

        char delimiter = sourceText[markerStart];
        if (delimiter is not ('=' or '-'))
        {
            return false;
        }

        int markerEnd = markerStart;
        while (markerEnd < line.ContentEnd && sourceText[markerEnd] == delimiter)
        {
            markerEnd++;
        }

        int position = markerEnd;
        SkipSpacesAndTabs(sourceText, ref position, line.ContentEnd);
        if (position != line.ContentEnd)
        {
            return false;
        }

        markerLength = markerEnd - markerStart;
        return markerLength > 0;
    }

    private static void CollectPipeTableStructuralSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        IReadOnlyList<MarkdownSourceLine> sourceLines = SplitSourceLines(sourceText);
        foreach (Table table in document.Descendants().OfType<Table>())
        {
            if (!TryCreateTextRange(table.Span, sourceText, out TextRange tableRange))
            {
                AddUnreliableSourceSpanDiagnostic(
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    table.Span);
                continue;
            }

            foreach (MarkdownSourceLine line in sourceLines)
            {
                if (IsLineWithinRange(line, tableRange))
                {
                    CollectPipeTableStructuralSyntax(sourceText, line, slices, diagnostics);
                }
            }
        }
    }

    private static bool IsLineWithinRange(MarkdownSourceLine line, TextRange range) =>
        line.Start < range.End && line.ContentEnd > range.Start;

    private static void CollectPipeTableStructuralSyntax(
        string sourceText,
        MarkdownSourceLine line,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        MarkdownSourceLine contentLine = GetContainerAwareContentLine(sourceText, line);
        if (!HasUnescapedPipe(sourceText, contentLine)
            && !IsPipeTableSeparatorLine(sourceText, contentLine))
        {
            return;
        }

        for (int position = contentLine.Start; position < contentLine.ContentEnd; position++)
        {
            if (sourceText[position] == '|'
                && !IsEscapedDelimiter(sourceText, position, contentLine.Start))
            {
                AddStructuralSyntaxRange(sourceText, slices, diagnostics, position, 1);
            }
        }

        if (!IsPipeTableSeparatorLine(sourceText, contentLine))
        {
            return;
        }

        for (int position = contentLine.Start; position < contentLine.ContentEnd; position++)
        {
            if (sourceText[position] == ':')
            {
                AddStructuralSyntaxRange(sourceText, slices, diagnostics, position, 1);
            }
            else if (sourceText[position] == '-')
            {
                int start = position;
                while (position < line.ContentEnd && sourceText[position] == '-')
                {
                    position++;
                }

                AddStructuralSyntaxRange(sourceText, slices, diagnostics, start, position - start);
                position--;
            }
        }
    }

    private static bool IsPipeTableLine(string sourceText, MarkdownSourceLine line) =>
        HasUnescapedPipe(sourceText, line)
        && (IsPipeTableSeparatorLine(sourceText, line)
            || LooksAdjacentToPipeTableSeparator(sourceText, line));

    private static bool LooksAdjacentToPipeTableSeparator(
        string sourceText,
        MarkdownSourceLine line)
    {
        foreach (MarkdownSourceLine candidate in SplitSourceLines(sourceText))
        {
            if (candidate.End == line.Start || line.End == candidate.Start)
            {
                return IsPipeTableSeparatorLine(sourceText, candidate);
            }
        }

        return false;
    }

    private static bool HasUnescapedPipe(string sourceText, MarkdownSourceLine line)
    {
        for (int position = line.Start; position < line.ContentEnd; position++)
        {
            if (sourceText[position] == '|'
                && !IsEscapedDelimiter(sourceText, position, line.Start))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPipeTableSeparatorLine(string sourceText, MarkdownSourceLine line)
    {
        bool hasPipe = false;
        bool hasHyphen = false;
        for (int position = line.Start; position < line.ContentEnd; position++)
        {
            char value = sourceText[position];
            if (value == '|')
            {
                if (IsEscapedDelimiter(sourceText, position, line.Start))
                {
                    return false;
                }

                hasPipe = true;
                continue;
            }

            if (value == '-')
            {
                hasHyphen = true;
                continue;
            }

            if (value is ':' or ' ' or '\t')
            {
                continue;
            }

            return false;
        }

        return hasPipe && hasHyphen;
    }

    private static void CollectListItemStructuralSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (ListItemBlock listItemBlock in document.Descendants().OfType<ListItemBlock>())
        {
            int markerStart = listItemBlock.Span.Start;
            if (markerStart < 0 || markerStart >= sourceText.Length)
            {
                AddUnreliableSourceSpanDiagnostic(
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    listItemBlock.Span);
                continue;
            }

            int lineEnd = FindLineContentEnd(sourceText, markerStart);
            if (!TryScanListItemMarker(sourceText, markerStart, lineEnd, out int markerEnd))
            {
                AddUnreliableSourceSpanDiagnostic(
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    listItemBlock.Span);
                continue;
            }

            TextRange markerRange = new(markerStart, markerEnd - markerStart);
            if (!OverlapsOpaqueProtectedRange(markerRange, slices))
            {
                AddRequiredRange(
                    slices,
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    markerStart,
                    markerEnd - markerStart,
                    sourceText);
            }

            int checkboxStart = markerEnd;
            SkipSpacesAndTabs(sourceText, ref checkboxStart, lineEnd);
            TextRange checkboxRange = new(checkboxStart, 3);
            if (IsTaskListCheckbox(sourceText, checkboxStart, lineEnd)
                && !OverlapsOpaqueProtectedRange(checkboxRange, slices))
            {
                AddRequiredRange(
                    slices,
                    diagnostics,
                    MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                    checkboxStart,
                    3,
                    sourceText);
            }
        }
    }

    private static void CollectInlineMarkdownStructuralSyntax(
        MarkdownDocument document,
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics)
    {
        foreach (EmphasisInline emphasisInline in document.Descendants().OfType<EmphasisInline>())
        {
            AddEmphasisDelimiterRun(
                sourceText,
                slices,
                diagnostics,
                emphasisInline,
                isOpeningDelimiter: true);
            AddEmphasisDelimiterRun(
                sourceText,
                slices,
                diagnostics,
                emphasisInline,
                isOpeningDelimiter: false);
        }
    }

    private static void AddEmphasisDelimiterRun(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        EmphasisInline emphasisInline,
        bool isOpeningDelimiter)
    {
        if (!TryCreateTextRange(emphasisInline.Span, sourceText, out TextRange spanRange)
            || emphasisInline.DelimiterCount <= 0)
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                emphasisInline.Span);
            return;
        }

        char delimiterChar = emphasisInline.DelimiterChar;
        int delimiterStart = isOpeningDelimiter
            ? spanRange.Start
            : spanRange.End - 1;
        if (delimiterStart < spanRange.Start
            || delimiterStart >= spanRange.End
            || sourceText[delimiterStart] != delimiterChar)
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                emphasisInline.Span);
            return;
        }

        int runStart;
        int runLength;
        if (isOpeningDelimiter)
        {
            runStart = delimiterStart;
            runLength = 0;
            while (runStart + runLength < spanRange.End
                && sourceText[runStart + runLength] == delimiterChar)
            {
                runLength++;
            }
        }
        else
        {
            int runEnd = delimiterStart + 1;
            runStart = delimiterStart;
            while (runStart - 1 >= spanRange.Start && sourceText[runStart - 1] == delimiterChar)
            {
                runStart--;
            }

            runLength = runEnd - runStart;
        }

        if (runLength < emphasisInline.DelimiterCount)
        {
            AddUnreliableSourceSpanDiagnostic(
                diagnostics,
                MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                emphasisInline.Span);
            return;
        }

        AddStructuralSyntaxRange(sourceText, slices, diagnostics, runStart, runLength);
    }

    private static void AddStructuralSyntaxRange(
        string sourceText,
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        int start,
        int length)
    {
        TextRange range = new(start, length);
        if (!OverlapsOpaqueProtectedRange(range, slices))
        {
            AddRequiredRange(
                slices,
                diagnostics,
                MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
                start,
                length,
                sourceText);
        }
    }

    private static bool TryScanListItemMarker(
        string sourceText,
        int markerStart,
        int lineEnd,
        out int markerEnd)
    {
        return TryScanListItemMarker(
            sourceText,
            markerStart,
            lineEnd,
            out markerEnd,
            out _,
            out _);
    }

    private static bool TryScanListItemMarker(
        string sourceText,
        int markerStart,
        int lineEnd,
        out int markerEnd,
        out bool isOrdered,
        out int orderedStartNumber)
    {
        markerEnd = markerStart;
        isOrdered = false;
        orderedStartNumber = 0;
        if (markerStart >= lineEnd)
        {
            return false;
        }

        if (sourceText[markerStart] is '-' or '+' or '*')
        {
            markerEnd = markerStart + 1;
            return IsEndOfListMarker(sourceText, markerEnd, lineEnd);
        }

        if (!char.IsAsciiDigit(sourceText[markerStart]))
        {
            return false;
        }

        isOrdered = true;
        orderedStartNumber = 0;
        int digitEnd = markerStart + 1;
        while (digitEnd < lineEnd
            && digitEnd - markerStart < 9
            && char.IsAsciiDigit(sourceText[digitEnd]))
        {
            digitEnd++;
        }

        for (int position = markerStart; position < digitEnd; position++)
        {
            orderedStartNumber = checked((orderedStartNumber * 10) + sourceText[position] - '0');
        }

        if (digitEnd >= lineEnd || sourceText[digitEnd] is not ('.' or ')'))
        {
            return false;
        }

        markerEnd = digitEnd + 1;
        return IsEndOfListMarker(sourceText, markerEnd, lineEnd);
    }

    private static bool IsEndOfListMarker(string sourceText, int markerEnd, int lineEnd) =>
        markerEnd == lineEnd || sourceText[markerEnd] is ' ' or '\t';

    private static bool IsTaskListCheckbox(string sourceText, int start, int lineEnd) =>
        start + 3 <= lineEnd
        && sourceText[start] == '['
        && sourceText[start + 1] is 'x' or 'X' or ' '
        && sourceText[start + 2] == ']'
        && (start + 3 == lineEnd || sourceText[start + 3] is ' ' or '\t');

    private static void AddRequiredSpan(
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        string kind,
        SourceSpan span,
        string sourceText)
    {
        if (span.Start < 0 || span.End < span.Start)
        {
            AddUnreliableSourceSpanDiagnostic(diagnostics, kind, span);
            return;
        }

        if (!TryCreateTextRange(span, sourceText, out TextRange range))
        {
            AddUnreliableSourceSpanDiagnostic(diagnostics, kind, span);
            return;
        }

        AddRequiredRange(slices, diagnostics, kind, range.Start, range.Length, sourceText);
    }

    private static void AddOptionalSpan(
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        string kind,
        SourceSpan span,
        string sourceText)
    {
        if (span.Start < 0 || span.End < span.Start)
        {
            return;
        }

        AddRequiredRange(
            slices,
            diagnostics,
            kind,
            span.Start,
            checked(span.End - span.Start + 1),
            sourceText);
    }

    private static bool TryCreateTextRange(SourceSpan span, string sourceText, out TextRange range)
    {
        range = default;
        if (span.Start < 0 || span.End < span.Start)
        {
            return false;
        }

        range = new TextRange(span.Start, checked(span.End - span.Start + 1));
        return range.IsWithin(sourceText);
    }

    private static void AddRequiredRange(
        List<ProtectedSlice> slices,
        List<MarkdownDiagnostic> diagnostics,
        string kind,
        int start,
        int length,
        string sourceText)
    {
        if (!TryAddRange(slices, kind, start, length, sourceText))
        {
            AddUnreliableRangeDiagnostic(diagnostics, kind, start, length);
        }
    }

    private static bool TryAddRange(
        List<ProtectedSlice> slices,
        string kind,
        int start,
        int length,
        string sourceText)
    {
        if (start < 0 || length < 0)
        {
            return false;
        }

        TextRange range = new(start, length);
        if (!range.IsWithin(sourceText))
        {
            return false;
        }

        slices.Add(new ProtectedSlice(
            SliceId: $"{kind}-{slices.Count}",
            Kind: kind,
            SourceRange: range,
            OriginalText: sourceText.Substring(start, length)));
        return true;
    }

    private static bool OverlapsExistingProtectedRange(
        TextRange range,
        IReadOnlyList<ProtectedSlice> slices) =>
        slices.Any(slice => slice.Kind != MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
            && RangesOverlap(range, slice.SourceRange));

    private static bool OverlapsOpaqueProtectedRange(
        TextRange range,
        IReadOnlyList<ProtectedSlice> slices) =>
        slices.Any(slice => IsOpaqueProtectedRangeKind(slice.Kind)
            && RangesOverlap(range, slice.SourceRange));

    private static bool OverlapsProtectedRangeOfKind(
        TextRange range,
        IReadOnlyList<ProtectedSlice> slices,
        string kind) =>
        slices.Any(slice => slice.Kind == kind && RangesOverlap(range, slice.SourceRange));

    private static bool IsOpaqueProtectedRangeKind(string kind) =>
        kind is MarkdownProtectedRangeKinds.FencedCodeBlock
            or MarkdownProtectedRangeKinds.IndentedCodeBlock
            or MarkdownProtectedRangeKinds.InlineCode
            or MarkdownProtectedRangeKinds.YamlFrontMatter
            or MarkdownProtectedRangeKinds.RawHtmlBlock
            or MarkdownProtectedRangeKinds.HtmlComment
            or MarkdownProtectedRangeKinds.InlineHtmlTag
            or MarkdownProtectedRangeKinds.InlineHtmlEnclosureText;

    private static bool RangesOverlap(TextRange left, TextRange right) =>
        left.Start < right.End && right.Start < left.End;

    private static void AddUnreliableSourceSpanDiagnostic(
        List<MarkdownDiagnostic> diagnostics,
        string kind,
        SourceSpan span) =>
        diagnostics.Add(new MarkdownDiagnostic(
            MarkdownFailureKind.UnreliableSourceSpan,
            $"Required protected range '{kind}' has no reliable source span "
                + $"({span.Start}..{span.End})."));

    private static void AddUnreliableRangeDiagnostic(
        List<MarkdownDiagnostic> diagnostics,
        string kind,
        int start,
        int length) =>
        diagnostics.Add(new MarkdownDiagnostic(
            MarkdownFailureKind.UnreliableSourceSpan,
            $"Required protected range '{kind}' is outside the source text "
                + $"(start {start}, length {length})."));

    private static string GetOriginalText(SourceSpan span, string sourceText)
    {
        if (span.Start < 0 || span.End < span.Start)
        {
            return string.Empty;
        }

        TextRange range = new(span.Start, checked(span.End - span.Start + 1));
        return range.IsWithin(sourceText)
            ? sourceText.Substring(range.Start, range.Length)
            : string.Empty;
    }

    private sealed record InlineHtmlTagInfo(
        string Name,
        TextRange SourceRange,
        TextRange ContainerRange,
        bool IsClosing,
        bool IsSelfClosing);

    private readonly record struct InlineHtmlTagContext(
        TextRange TagRange,
        TextRange ContainerRange);

    private readonly record struct ReferenceDefinitionRange(int Start, int Length);

    private readonly record struct MarkdownSourceLine(int Start, int ContentEnd, int End);

    private sealed record MarkdownContainerContext(IReadOnlyList<MarkdownContainerMarker> Markers)
    {
        public static MarkdownContainerContext Empty { get; } = new([]);
    }

    private readonly record struct MarkdownContainerMarker(
        MarkdownContainerMarkerKind Kind,
        int ContinuationIndent);

    private enum MarkdownContainerMarkerKind
    {
        Blockquote,
        ListItem,
    }

    private const int MaxReferenceLabelLength = 999;

    private const string AutolinkPattern =
        """<(?:(?:[A-Za-z][A-Za-z0-9+.-]{1,31}:[^<>\s\x00-\x20]+)|"""
        + """[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})>""";

    private const string EmailLiteralPattern =
        """(?<![\w.%+-])[A-Za-z0-9._%+-]+"""
        + """@[A-Za-z0-9.-]+\.[A-Za-z]{2,}(?![\w-]|\.[A-Za-z0-9-])""";

    private const string UriFragmentPattern =
        """#(?:\$\{[A-Za-z_][A-Za-z0-9_]*\}"""
        + """|\{\{[^{}\r\n]+\}\}"""
        + """|\{[0-9]+\}"""
        + """|%[0-9A-Fa-f]{2}"""
        + """|[A-Za-z0-9._~!$&'()*+,;=:@/?-])+""";

    [GeneratedRegex(
        AutolinkPattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AutolinkRegex();

    [GeneratedRegex(
        """(?<![\w])https?://[^\s<>\]`]+""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlLiteralRegex();

    [GeneratedRegex(EmailLiteralPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmailLiteralRegex();

    [GeneratedRegex(UriFragmentPattern, RegexOptions.CultureInvariant)]
    private static partial Regex UriFragmentRegex();

    [GeneratedRegex(@"\\[!""#$%&'()*+,\-./:;<=>?@\[\]\\^_`{|}~]", RegexOptions.CultureInvariant)]
    private static partial Regex EscapedMarkdownDelimiterRegex();
}
