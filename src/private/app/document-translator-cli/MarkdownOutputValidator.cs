using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Reflection;

namespace Hcoona.DocumentTranslatorCli;

internal static class MarkdownOutputValidator
{
    public static MarkdownOutputValidationResult Validate(
        MarkdownParseResult sourceParseResult,
        MarkdownSourcePatchResult patchResult)
    {
        ArgumentNullException.ThrowIfNull(sourceParseResult);
        ArgumentNullException.ThrowIfNull(patchResult);

        if (!patchResult.Succeeded)
        {
            return new MarkdownOutputValidationResult(
                patchResult.PatchedText,
                patchResult.SourceMetadata,
                null,
                patchResult.Diagnostics.Count > 0
                    ? patchResult.Diagnostics
                    : [CreateDiagnostic("The Markdown source patch did not succeed.")]);
        }

        return Validate(
            sourceParseResult,
            patchResult.PatchedText,
            patchResult.SourceMetadata,
            patchResult.PatchMaps);
    }

    public static MarkdownOutputValidationResult Validate(
        MarkdownParseResult sourceParseResult,
        string patchedText,
        MarkdownSourceMetadata patchedSourceMetadata,
        IReadOnlyList<SourcePatchMap> patchMaps)
    {
        ArgumentNullException.ThrowIfNull(sourceParseResult);
        ArgumentNullException.ThrowIfNull(patchedText);
        ArgumentNullException.ThrowIfNull(patchedSourceMetadata);
        ArgumentNullException.ThrowIfNull(patchMaps);

        if (!sourceParseResult.Succeeded || sourceParseResult.Document is null)
        {
            IReadOnlyList<MarkdownDiagnostic> diagnostics = sourceParseResult.Diagnostics.Count > 0
                ? sourceParseResult.Diagnostics
                : [CreateDiagnostic("The source Markdown parse result did not succeed.")];
            return new MarkdownOutputValidationResult(
                patchedText,
                patchedSourceMetadata,
                null,
                diagnostics);
        }

        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();
        MarkdownParseResult patchedParseResult = parser.Parse(patchedText);
        if (!patchedParseResult.Succeeded || patchedParseResult.Document is null)
        {
            IReadOnlyList<MarkdownDiagnostic> diagnostics = patchedParseResult.Diagnostics.Count > 0
                ? patchedParseResult.Diagnostics
                : [CreateDiagnostic("The patched Markdown could not be reparsed.")];
            return new MarkdownOutputValidationResult(
                patchedText,
                patchedSourceMetadata,
                patchedParseResult,
                diagnostics);
        }

        List<MarkdownDiagnostic> validationDiagnostics = [];
        ValidateStructuralFingerprint(sourceParseResult, patchedParseResult, validationDiagnostics);
        ValidateProtectedSlices(
            sourceParseResult,
            patchedParseResult,
            patchMaps,
            validationDiagnostics);
        ValidateSourceMetadata(
            sourceParseResult,
            patchedParseResult,
            patchedSourceMetadata,
            patchMaps,
            validationDiagnostics);

        MarkdownSourceMetadata outputMetadata = validationDiagnostics.Count == 0
            ? patchedParseResult.SourceMetadata with
            {
                HasUtf8Bom = patchedSourceMetadata.HasUtf8Bom,
            }
            : patchedSourceMetadata;

        return new MarkdownOutputValidationResult(
            patchedText,
            outputMetadata,
            patchedParseResult,
            validationDiagnostics);
    }

    private static void ValidateStructuralFingerprint(
        MarkdownParseResult sourceParseResult,
        MarkdownParseResult patchedParseResult,
        List<MarkdownDiagnostic> diagnostics)
    {
        string[] sourceFingerprint = MarkdownStructuralFingerprint.Create(
            sourceParseResult.Document!,
            sourceParseResult.SourceText);
        string[] patchedFingerprint = MarkdownStructuralFingerprint.Create(
            patchedParseResult.Document!,
            patchedParseResult.SourceText);

        if (!sourceFingerprint.SequenceEqual(patchedFingerprint, StringComparer.Ordinal))
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownFailureKind.StructuralChanged,
                "The patched Markdown structural fingerprint differs from the source."));
        }
    }

    private static void ValidateProtectedSlices(
        MarkdownParseResult sourceParseResult,
        MarkdownParseResult patchedParseResult,
        IReadOnlyList<SourcePatchMap> patchMaps,
        List<MarkdownDiagnostic> diagnostics)
    {
        List<ProtectedSliceMatchKey> expectedKeys = [];
        foreach (ProtectedSlice sourceSlice in sourceParseResult.ProtectedSlices)
        {
            if (!TryMapUnpatchedRange(
                sourceSlice.SourceRange,
                patchMaps,
                out TextRange patchedRange))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.ReconstructionChanged,
                    $"Protected Markdown slice '{sourceSlice.SliceId}' overlaps translated text."));
                continue;
            }

            if (!patchedRange.IsWithin(patchedParseResult.SourceText))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.ReconstructionChanged,
                    $"Protected Markdown slice '{sourceSlice.SliceId}' maps outside "
                    + "the patched text."));
                continue;
            }

            string patchedText = patchedParseResult.SourceText.Substring(
                patchedRange.Start,
                patchedRange.Length);
            if (!string.Equals(patchedText, sourceSlice.OriginalText, StringComparison.Ordinal))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.ReconstructionChanged,
                    $"Protected Markdown slice '{sourceSlice.SliceId}' changed in "
                    + "the patched output."));
                continue;
            }

            expectedKeys.Add(new ProtectedSliceMatchKey(
                sourceSlice.Kind,
                patchedRange,
                sourceSlice.OriginalText));
        }

        Dictionary<ProtectedSliceMatchKey, int> patchedKeyCounts =
            CountProtectedSliceKeys(patchedParseResult.ProtectedSlices);
        foreach (IGrouping<ProtectedSliceMatchKey, ProtectedSliceMatchKey> expectedKeyGroup in
            expectedKeys.GroupBy(static key => key))
        {
            int expectedCount = expectedKeyGroup.Count();
            patchedKeyCounts.TryGetValue(expectedKeyGroup.Key, out int actualCount);
            if (actualCount != expectedCount)
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.ReconstructionChanged,
                    $"Protected Markdown slice '{expectedKeyGroup.Key.Kind}' at "
                    + $"{expectedKeyGroup.Key.SourceRange.Start}.."
                    + $"{expectedKeyGroup.Key.SourceRange.End} "
                    + "could not be uniquely located in the patched parse "
                    + $"(expected {expectedCount}, found {actualCount})."));
            }
        }

        Dictionary<ProtectedSliceMatchKey, int> expectedKeyCounts = expectedKeys
            .GroupBy(static key => key)
            .ToDictionary(static group => group.Key, static group => group.Count());
        foreach ((ProtectedSliceMatchKey key, int actualCount) in patchedKeyCounts)
        {
            expectedKeyCounts.TryGetValue(key, out int expectedCount);
            if (actualCount != expectedCount)
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.ReconstructionChanged,
                    $"Unexpected protected Markdown slice '{key.Kind}' at "
                    + $"{key.SourceRange.Start}..{key.SourceRange.End} in the patched parse "
                    + $"(expected {expectedCount}, found {actualCount})."));
            }
        }
    }

    private static void ValidateSourceMetadata(
        MarkdownParseResult sourceParseResult,
        MarkdownParseResult patchedParseResult,
        MarkdownSourceMetadata patchedSourceMetadata,
        IReadOnlyList<SourcePatchMap> patchMaps,
        List<MarkdownDiagnostic> diagnostics)
    {
        MarkdownSourceMetadata sourceMetadata = sourceParseResult.SourceMetadata;
        if (sourceMetadata.HasUtf8Bom != patchedSourceMetadata.HasUtf8Bom)
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownFailureKind.ReconstructionChanged,
                "The patched Markdown did not preserve UTF-8 BOM metadata."));
        }

        if (patchedParseResult.SourceMetadata.HasUtf8Bom)
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownFailureKind.ReconstructionChanged,
                "The patched Markdown contains a decoded leading UTF-8 BOM."));
        }

        if (sourceMetadata.HasFinalNewline != patchedSourceMetadata.HasFinalNewline
            || sourceMetadata.HasFinalNewline != patchedParseResult.SourceMetadata.HasFinalNewline)
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownFailureKind.ReconstructionChanged,
                "The patched Markdown did not preserve final newline metadata."));
        }

        if (!LineEndingsEqual(sourceMetadata.LineEndings, patchedSourceMetadata.LineEndings))
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownFailureKind.ReconstructionChanged,
                "The patched Markdown did not preserve line ending metadata."));
        }

        MarkdownLineEnding[] expectedLineEndings = CreateExpectedPatchedLineEndings(
            sourceMetadata.LineEndings,
            patchMaps);
        if (!LineEndingsEqual(expectedLineEndings, patchedParseResult.SourceMetadata.LineEndings))
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownFailureKind.ReconstructionChanged,
                "The patched Markdown line endings differ from the expected patched source."));
        }
    }

    private static bool TryMapUnpatchedRange(
        TextRange originalRange,
        IReadOnlyList<SourcePatchMap> patchMaps,
        out TextRange patchedRange)
    {
        patchedRange = default;
        if (RangeOverlapsPatchedText(originalRange, patchMaps))
        {
            return false;
        }

        int start = originalRange.Start + GetDeltaBefore(originalRange.Start, patchMaps);
        int end = originalRange.End + GetDeltaBefore(originalRange.End, patchMaps);
        if (end < start)
        {
            return false;
        }

        patchedRange = new TextRange(start, end - start);
        return true;
    }

    private static Dictionary<ProtectedSliceMatchKey, int> CountProtectedSliceKeys(
        IReadOnlyList<ProtectedSlice> protectedSlices)
    {
        Dictionary<ProtectedSliceMatchKey, int> counts = [];
        foreach (ProtectedSlice slice in protectedSlices)
        {
            ProtectedSliceMatchKey key = new(slice.Kind, slice.SourceRange, slice.OriginalText);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        return counts;
    }

    private static bool LineEndingsEqual(
        IReadOnlyList<MarkdownLineEnding> sourceLineEndings,
        IReadOnlyList<MarkdownLineEnding> patchedLineEndings)
    {
        if (sourceLineEndings.Count != patchedLineEndings.Count)
        {
            return false;
        }

        for (int i = 0; i < sourceLineEndings.Count; i++)
        {
            MarkdownLineEnding source = sourceLineEndings[i];
            MarkdownLineEnding patched = patchedLineEndings[i];
            if (source.SourceRange != patched.SourceRange
                || !string.Equals(source.Text, patched.Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static MarkdownLineEnding[] CreateExpectedPatchedLineEndings(
        IReadOnlyList<MarkdownLineEnding> sourceLineEndings,
        IReadOnlyList<SourcePatchMap> patchMaps)
    {
        List<MarkdownLineEnding> expected = [];
        foreach (MarkdownLineEnding lineEnding in sourceLineEndings)
        {
            if (TryMapLineEndingRange(
                lineEnding.SourceRange,
                patchMaps,
                out TextRange patchedRange))
            {
                expected.Add(new MarkdownLineEnding(patchedRange, lineEnding.Text));
            }
        }

        return expected
            .OrderBy(static lineEnding => lineEnding.SourceRange.Start)
            .ThenBy(static lineEnding => lineEnding.SourceRange.Length)
            .ThenBy(static lineEnding => lineEnding.Text, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryMapLineEndingRange(
        TextRange originalRange,
        IReadOnlyList<SourcePatchMap> patchMaps,
        out TextRange patchedRange)
    {
        patchedRange = default;
        SourcePatchMap? containingPatch = null;
        foreach (SourcePatchMap map in patchMaps)
        {
            if (!RangesOverlap(originalRange, map.OriginalRange))
            {
                continue;
            }

            if (originalRange.Start < map.OriginalRange.Start
                || originalRange.End > map.OriginalRange.End)
            {
                return false;
            }

            if (containingPatch is not null)
            {
                return false;
            }

            containingPatch = map;
        }

        if (containingPatch is null)
        {
            return TryMapUnpatchedRange(originalRange, patchMaps, out patchedRange);
        }

        int offsetInPatch = originalRange.Start - containingPatch.OriginalRange.Start;
        patchedRange = new TextRange(
            checked(containingPatch.PatchedRange.Start + offsetInPatch),
            originalRange.Length);
        return patchedRange.End <= containingPatch.PatchedRange.End;
    }

    private static bool RangeOverlapsPatchedText(
        TextRange range,
        IReadOnlyList<SourcePatchMap> patchMaps) =>
        patchMaps.Any(map => RangesOverlap(range, map.OriginalRange));

    private static int GetDeltaBefore(int originalOffset, IReadOnlyList<SourcePatchMap> patchMaps)
    {
        int delta = 0;
        foreach (SourcePatchMap map in patchMaps.OrderBy(static map => map.OriginalRange.Start))
        {
            if (map.OriginalRange.End > originalOffset)
            {
                break;
            }

            delta = checked(delta + map.LengthDelta);
        }

        return delta;
    }

    private static bool RangesOverlap(TextRange left, TextRange right) =>
        left.Start < right.End && right.Start < left.End;

    private static MarkdownDiagnostic CreateDiagnostic(string message) =>
        new(MarkdownFailureKind.ReconstructionChanged, message);

    private readonly record struct ProtectedSliceMatchKey(
        string Kind,
        TextRange SourceRange,
        string OriginalText);

    private sealed class MarkdownStructuralFingerprint
    {
        private readonly List<string> entries = [];
        private readonly string sourceText;

        private MarkdownStructuralFingerprint(string sourceText)
        {
            this.sourceText = sourceText;
        }

        public static string[] Create(MarkdownDocument document, string sourceText)
        {
            MarkdownStructuralFingerprint fingerprint = new(sourceText);
            fingerprint.AddBlock(document, 0);
            return fingerprint.entries.ToArray();
        }

        private void AddBlock(Block block, int depth)
        {
            entries.Add($"{depth}:block:{BlockSignature(block)}");
            if (block is LeafBlock leafBlock)
            {
                AddInlineContainer(leafBlock.Inline, depth + 1);
            }

            if (block is ContainerBlock containerBlock)
            {
                foreach (Block child in containerBlock)
                {
                    AddBlock(child, depth + 1);
                }
            }
        }

        private string BlockSignature(Block block) =>
            block switch
            {
                MarkdownDocument document => $"document:{document.Count}",
                HeadingBlock heading => string.Join(
                    ':',
                    nameof(HeadingBlock),
                    heading.Level,
                    heading.IsSetext,
                    heading.HeaderCharCount),
                ListBlock list => string.Join(
                    ':',
                    nameof(ListBlock),
                    list.IsOrdered,
                    list.BulletType,
                    list.OrderedStart,
                    list.Count),
                ListItemBlock listItem => $"{nameof(ListItemBlock)}:{listItem.Count}",
                QuoteBlock quote => $"{nameof(QuoteBlock)}:{quote.Count}",
                ParagraphBlock => nameof(ParagraphBlock),
                ThematicBreakBlock thematicBreak => string.Join(
                    ':',
                    nameof(ThematicBreakBlock),
                    thematicBreak.ThematicChar,
                    thematicBreak.ThematicCharCount),
                YamlFrontMatterBlock yaml =>
                    $"{nameof(YamlFrontMatterBlock)}:{SourceSpanText(yaml.Span)}",
                FencedCodeBlock fencedCode => string.Join(
                    ':',
                    nameof(FencedCodeBlock),
                    fencedCode.FencedChar,
                    fencedCode.OpeningFencedCharCount,
                    fencedCode.ClosingFencedCharCount,
                    fencedCode.Info,
                    fencedCode.Arguments,
                    SourceSpanText(fencedCode.Span)),
                HtmlBlock htmlBlock => $"{nameof(HtmlBlock)}:{SourceSpanText(htmlBlock.Span)}",
                CodeBlock codeBlock => $"{nameof(CodeBlock)}:{SourceSpanText(codeBlock.Span)}",
                Table table => string.Join(
                    ':',
                    nameof(Table),
                    table.Count,
                    table.ColumnDefinitions.Count,
                    string.Join(
                        ',',
                        table.ColumnDefinitions.Select(static column => column.Alignment))),
                TableRow row => $"{nameof(TableRow)}:{row.IsHeader}:{row.Count}",
                TableCell cell => $"{nameof(TableCell)}:{cell.Count}",
                Footnote footnote => string.Join(
                    ':',
                    nameof(Footnote),
                    footnote.Label,
                    footnote.Order,
                    footnote.Count),
                _ => $"{block.GetType().FullName}:{ContainerCount(block)}",
            };

        private static int ContainerCount(Block block) =>
            block is ContainerBlock containerBlock ? containerBlock.Count : 0;

        private void AddInlineContainer(ContainerInline? containerInline, int depth)
        {
            if (containerInline is null)
            {
                entries.Add($"{depth}:inline-container:null");
                return;
            }

            foreach (Inline inline in containerInline)
            {
                AddInline(inline, depth);
            }
        }

        private void AddInline(Inline inline, int depth)
        {
            entries.Add($"{depth}:inline:{InlineSignature(inline)}");
            if (inline is ContainerInline containerInline)
            {
                foreach (Inline child in containerInline)
                {
                    AddInline(child, depth + 1);
                }
            }
        }

        private string InlineSignature(Inline inline) =>
            inline switch
            {
                LiteralInline => nameof(LiteralInline),
                LineBreakInline lineBreak => string.Join(
                    ':',
                    nameof(LineBreakInline),
                    lineBreak.IsHard,
                    lineBreak.IsBackslash,
                    lineBreak.NewLine),
                CodeInline code => string.Join(
                    ':',
                    nameof(CodeInline),
                    code.Delimiter,
                    code.DelimiterCount,
                    code.Content),
                HtmlInline html => $"{nameof(HtmlInline)}:{SourceSpanText(html.Span)}",
                EmphasisInline emphasis => string.Join(
                    ':',
                    nameof(EmphasisInline),
                    emphasis.DelimiterChar,
                    emphasis.DelimiterCount),
                LinkInline link => string.Join(
                    ':',
                    nameof(LinkInline),
                    link.IsImage,
                    link.IsAutoLink,
                    link.Url,
                    link.Title,
                    link.Label),
                TaskList taskList => $"{nameof(TaskList)}:{taskList.Checked}",
                FootnoteLink footnoteLink => string.Join(
                    ':',
                    nameof(FootnoteLink),
                    footnoteLink.IsBackLink,
                    footnoteLink.Index,
                    footnoteLink.Footnote?.Label),
                _ => $"{inline.GetType().FullName}:{ReflectionSignature(inline)}",
            };

        private static string ReflectionSignature(object value)
        {
            PropertyInfo[] properties = value.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(static property =>
                    property.GetIndexParameters().Length == 0
                    && property.PropertyType != typeof(Inline)
                    && property.PropertyType != typeof(Block)
                    && property.PropertyType != typeof(ContainerInline)
                    && property.PropertyType != typeof(ContainerBlock)
                    && (property.PropertyType.IsPrimitive
                        || property.PropertyType.IsEnum
                        || property.PropertyType == typeof(string)))
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToArray();

            return string.Join(
                ';',
                properties.Select(property => $"{property.Name}={property.GetValue(value)}"));
        }

        private string SourceSpanText(SourceSpan span)
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
    }
}
