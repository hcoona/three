// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System;

namespace Hcoona.Nanopass;

/// <summary>
/// Describes a pass, its processors, and the conversions at its boundaries.
/// </summary>
/// <param name="name">The symbol naming the pass.</param>
/// <param name="inputLanguage">The input language, or null for a raw input.</param>
/// <param name="inputEntryNonTerminal">
/// The input nonterminal, defaulting to the language entry.
/// </param>
/// <param name="outputLanguage">The output language, or null for a raw output.</param>
/// <param name="outputEntryNonTerminal">
/// The output nonterminal, defaulting to the language entry.
/// </param>
/// <param name="processors">The processors belonging to the pass.</param>
/// <param name="inputParser">The parser, required only for a language input.</param>
/// <param name="outputUnparser">The unparser, required only for a language output.</param>
/// <exception cref="ArgumentNullException">
/// A language endpoint has no corresponding conversion.
/// </exception>
public sealed class PassDescriptor(
    Symbol name,
    LanguageDescriptor? inputLanguage,
    Symbol? inputEntryNonTerminal,
    LanguageDescriptor? outputLanguage,
    Symbol? outputEntryNonTerminal,
    Seq<ProcessorDescriptor> processors,
    Func<SExpr, object?>? inputParser = null,
    Func<object?, bool, SExpr>? outputUnparser = null)
{
    private readonly Func<SExpr, object?> inputParser = inputLanguage is null
        ? static datum => datum
        : inputParser ?? throw new ArgumentNullException(nameof(inputParser));

    private readonly Func<object?, bool, SExpr> outputUnparser = outputLanguage is null
        ? static (value, _) => value is null ? SExpr.Nil.Instance : SExpr.FromValue(value)
        : outputUnparser ?? throw new ArgumentNullException(nameof(outputUnparser));

    /// <summary>
    /// Gets the symbol naming the pass.
    /// </summary>
    public Symbol Name { get; } = name;

    /// <summary>
    /// Gets the input language, or null for a raw input.
    /// </summary>
    public LanguageDescriptor? InputLanguage { get; } = inputLanguage;

    /// <summary>
    /// Gets the input entry nonterminal, or null for a raw input.
    /// </summary>
    public Symbol? InputEntryNonTerminal { get; } =
        inputLanguage is null ? default(Symbol?) : inputEntryNonTerminal ?? inputLanguage.Entry;

    /// <summary>
    /// Gets the output language, or null for a raw output.
    /// </summary>
    public LanguageDescriptor? OutputLanguage { get; } = outputLanguage;

    /// <summary>
    /// Gets the output entry nonterminal, or null for a raw output.
    /// </summary>
    public Symbol? OutputEntryNonTerminal { get; } =
        outputLanguage is null ? default(Symbol?) : outputEntryNonTerminal ?? outputLanguage.Entry;

    /// <summary>
    /// Gets the processors belonging to the pass.
    /// </summary>
    public Seq<ProcessorDescriptor> Processors { get; } = processors;

    /// <summary>
    /// Parses a datum as the input of the pass.
    /// </summary>
    /// <param name="datum">The input datum.</param>
    /// <returns>The parsed term, or the original datum for a raw input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="datum"/> is null.</exception>
    public object? ParseInput(SExpr datum)
    {
        ArgumentNullException.ThrowIfNull(datum);
        return this.inputParser(datum);
    }

    /// <summary>
    /// Reads one datum from text and parses it as the input of the pass.
    /// </summary>
    /// <param name="text">The S-expression text to read.</param>
    /// <returns>The parsed term, or the read datum for a raw input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public object? ParseInput(string text) => this.ParseInput(SExpr.Parse(text));

    /// <summary>
    /// Unparses the output of the pass as a datum.
    /// </summary>
    /// <param name="value">The output term or raw value.</param>
    /// <param name="raw">Whether to omit language prettifiers.</param>
    /// <returns>The output datum.</returns>
    public SExpr UnparseOutput(object? value, bool raw = false) => this.outputUnparser(value, raw);

    /// <summary>
    /// Unparses the output of the pass as S-expression text.
    /// </summary>
    /// <param name="value">The output term or raw value.</param>
    /// <param name="raw">Whether to omit language prettifiers.</param>
    /// <returns>The formatted output datum.</returns>
    /// <exception cref="InvalidOperationException">The datum contains an opaque value.</exception>
    public string UnparseOutputToString(object? value, bool raw = false) =>
        this.UnparseOutput(value, raw).Format();
}
