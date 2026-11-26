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

// Adapted from the MIT-licensed Nanopass Framework Racket compiler by Jens Axel Søgaard.
// Upstream copyright and license: src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Env = System.Collections.Immutable.ImmutableDictionary<
    Hcoona.Nanopass.Symbol, Hcoona.Nanopass.Symbol>;

namespace Hcoona.Nanopass.Tests.Compilers.CoreScheme;

[Language]
[Entry("Term")]
[Terminal(typeof(Symbol), "x", Name = "Variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(Symbol), "O", Name = "Primitive", Predicate = nameof(IsPrimitive))]
[Terminal(typeof(SExpr), "c", Name = "Constant", Predicate = nameof(IsConstant))]
[NonTerminalDef("Value", ["V"], "c", "x", "(λ (x1 ...) M)")]
[NonTerminalDef("Term", ["M"], "V", "(let (x M1) M2)", "(if M1 M2 M3)",
    "(prim O M1 ...) => (O M1 ...)", "(call M M1 ...) => (M M1 ...)")]
internal static partial class LCS
{
    internal static bool IsVariable(Symbol symbol) => !IsPrimitive(symbol);

    internal static bool IsPrimitive(Symbol symbol) =>
        symbol.Name is "+" or "-" or "*" or "/" or "zero?";

    internal static bool IsConstant(SExpr datum) =>
        datum is SExpr.Int or SExpr.Float or SExpr.Bool or SExpr.Str;
}

[Language]
[Entry("Term")]
[Terminal(typeof(Symbol), "x", Name = "Variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(Symbol), "O", Name = "Primitive", Predicate = nameof(IsPrimitive))]
[Terminal(typeof(SExpr), "c", Name = "Constant", Predicate = nameof(IsConstant))]
[NonTerminalDef("Value", ["V"], "c", "x", "(λ (x1 ...) M)")]
[NonTerminalDef("Application", ["A"], "(call V V1 ...)", "(prim O V1 ...)")]
[NonTerminalDef("BoundTerm", ["B"], "V", "A")]
[NonTerminalDef("Term", ["M"], "V", "A", "(let (x B) M)", "(if V M1 M2)")]
internal static partial class LACS
{
    private static bool IsVariable(Symbol symbol) => LCS.IsVariable(symbol);

    private static bool IsPrimitive(Symbol symbol) => LCS.IsPrimitive(symbol);

    private static bool IsConstant(SExpr datum) => LCS.IsConstant(datum);
}

[Pass(typeof(RawTerm), typeof(LCS))]
internal sealed partial class ParseCoreScheme
{
    [Processor("*", "Term")]
    private static LCS.Term Parse(SExpr datum)
    {
        if (LCS.IsConstant(datum))
        {
            return (LCS.Value)datum;
        }

        if (datum is SExpr.Sym(var symbol) && LCS.IsVariable(symbol))
        {
            return (LCS.Value)symbol;
        }

        if (!datum.TryToProperList(out var items) || items.IsEmpty)
        {
            throw new NanopassException($"Invalid CoreScheme term: {datum}.");
        }

        return items switch
        {
            [SExpr.Sym({ Name: "λ" }), var formals, var body]
                when formals.TryToProperList(out var parameters) =>
                (LCS.Value)new LCS.Λ(ParseFormals(parameters), Parse(body)),
            [SExpr.Sym({ Name: "let" }), var binding, var body]
                when binding.TryToProperList(out var pair) && pair is [var name, var rhs] =>
                new LCS.Let(ParseVariable(name), Parse(rhs), Parse(body)),
            [SExpr.Sym({ Name: "if" }), var test, var consequent, var alternative] =>
                new LCS.If(Parse(test), Parse(consequent), Parse(alternative)),
            [SExpr.Sym(var primitive), .. var operands] when LCS.IsPrimitive(primitive) =>
                new LCS.Prim(primitive, operands.Select(Parse)),
            [var procedure, .. var operands] =>
                new LCS.Call(Parse(procedure), operands.Select(Parse)),
            _ => throw new NanopassException($"Invalid CoreScheme term: {datum}."),
        };
    }

    private static Seq<Symbol> ParseFormals(Seq<SExpr> parameters)
    {
        HashSet<Symbol> seen = [];
        return parameters.Select(parameter =>
        {
            Symbol name = ParseVariable(parameter);
            return seen.Add(name)
                ? name
                : throw new NanopassException($"Duplicate parameter: {name}.");
        });
    }

    private static Symbol ParseVariable(SExpr datum) =>
        datum is SExpr.Sym(var name) && LCS.IsVariable(name)
            ? name
            : throw new NanopassException($"Invalid CoreScheme variable: {datum}.");
}

[Pass(typeof(LCS), typeof(LCS))]
internal sealed partial class AlphaRenameCoreScheme
{
    public LCS.Term Run(LCS.Term input) => this.RenameTerm(input, Env.Empty, new FreshNames()).Term;

    [Processor("Value", "Value")]
    private partial (LCS.Value Value, Env Environment) RenameValue(
        LCS.Value ir, Env env, FreshNames fresh);

    [Processor("Term", "Term")]
    private partial (LCS.Term Term, Env Environment) RenameTerm(
        LCS.Term ir, Env env, FreshNames fresh);

    [Rewrite(Processor = nameof(RenameValue))]
    private static (LCS.Value, Env) Constant(SExpr c, Env env) => (c, env);

    [Rewrite(Processor = nameof(RenameValue))]
    private static (LCS.Value, Env) Variable(Symbol x, Env env) =>
        (env.TryGetValue(x, out var renamed)
            ? renamed
            : throw new NanopassException($"Unbound identifier: {x}."), env);

    [Rewrite(Processor = nameof(RenameValue))]
    private (LCS.Value, Env) Lambda(LCS.Λ node, Env env, FreshNames fresh)
    {
        var (formals, body) = node;
        Env bodyEnv = env;
        var parameters = formals.Select(name =>
        {
            Symbol renamed = fresh.Claim(name);
            bodyEnv = bodyEnv.SetItem(name, renamed);
            return renamed;
        });
        return (new LCS.Λ(parameters, this.RenameTerm(body, bodyEnv, fresh).Term), env);
    }

    [Rewrite(typeof(LCS.Value), Processor = nameof(RenameTerm))]
    private static (LCS.Term, Env) Value(
        [Recur(Field = "V", Processor = nameof(RenameValue))]
        (LCS.Value Value, Env Environment) value) =>
        (value.Value, value.Environment);

    [Rewrite(Processor = nameof(RenameTerm))]
    private (LCS.Term, Env) Let(
        LCS.Let node, [Recur(Field = "M1")] (LCS.Term Term, Env Environment) rhs,
        Env env, FreshNames fresh)
    {
        Symbol name = fresh.Claim(node.X);
        var (body, _) = this.RenameTerm(node.M2, rhs.Environment.SetItem(node.X, name), fresh);
        return (new LCS.Let(name, rhs.Term, body), env);
    }

    [Rewrite(typeof(LCS.If), Processor = nameof(RenameTerm))]
    private static (LCS.Term, Env) If(
        [Recur(Field = "M1")] (LCS.Term Term, Env Environment) test,
        [Recur(Field = "M2")] (LCS.Term Term, Env Environment) consequent,
        [Recur(Field = "M3")] (LCS.Term Term, Env Environment) alternative,
        Env env) =>
        (new LCS.If(test.Term, consequent.Term, alternative.Term), env);

    [Rewrite(Processor = nameof(RenameTerm))]
    private static (LCS.Term, Env) Primitive(
        LCS.Prim node, [Recur(Field = "M1")] Seq<(LCS.Term Term, Env Environment)> operands,
        Env env) =>
        (new LCS.Prim(node.O, operands.Select(static arg => arg.Term)), env);

    [Rewrite(typeof(LCS.Call), Processor = nameof(RenameTerm))]
    private static (LCS.Term, Env) Call(
        [Recur(Field = "M")] (LCS.Term Term, Env Environment) procedure,
        [Recur(Field = "M1")] Seq<(LCS.Term Term, Env Environment)> operands, Env env) =>
        (new LCS.Call(procedure.Term, operands.Select(static arg => arg.Term)), env);
}

internal sealed class ANormalizeCoreScheme : IPass<LCS.Term, LACS.Term>
{
    public LACS.Term Run(LCS.Term input) => new Normalizer(input).NormalizeTail(input);

    private sealed class Normalizer(LCS.Term input)
    {
        private readonly FreshNames fresh = new(LCS.Unparse(input));

        internal LACS.Term NormalizeTail(LCS.Term term) =>
            this.Normalize(term, static value => value);

        private LACS.Term Normalize(LCS.Term term, Func<LACS.Term, LACS.Term> continuation) =>
            term.Value switch
            {
                LCS.Value value => continuation(this.NormalizeValue(value)),
                LCS.Let(var name, var rhs, var body) => this.Normalize(rhs,
                    right => new LACS.Let(
                        name, AsBoundTerm(right), this.Normalize(body, continuation))),
                LCS.If(var test, var consequent, var alternative) => this.NormalizeName(test,
                    value => continuation(new LACS.If(value,
                        this.NormalizeTail(consequent), this.NormalizeTail(alternative)))),
                LCS.Prim(var primitive, var operands) => this.NormalizeNames(operands,
                    values => continuation((LACS.Application)new LACS.Prim(primitive, values))),
                LCS.Call(var procedure, var operands) => this.NormalizeName(procedure,
                    value => this.NormalizeNames(operands,
                        values => continuation((LACS.Application)new LACS.Call(value, values)))),
                _ => throw new NanopassException($"Invalid CoreScheme term: {term}."),
            };

        private static LACS.BoundTerm AsBoundTerm(LACS.Term term) => term.Value switch
        {
            LACS.Value value => value,
            LACS.Application application => application,
            // A conditional is not a BoundTerm. Thunk it without duplicating its continuation.
            LACS.If branch => (LACS.Application)new LACS.Call(new LACS.Λ([], branch), []),
            _ => throw new NanopassException($"Invalid administrative binding: {term}."),
        };

        private LACS.Value NormalizeValue(LCS.Value value) =>
            ((LCS.Value.IUnionMembers)value).Value switch
            {
                LCS.Λ(var parameters, var body) => new LACS.Λ(parameters, this.NormalizeTail(body)),
                Symbol symbol => symbol,
                SExpr constant => constant,
                _ => throw new NanopassException($"Invalid CoreScheme value: {value}."),
            };

        private LACS.Term NormalizeName(LCS.Term term, Func<LACS.Value, LACS.Term> continuation) =>
            this.Normalize(term, normalized =>
            {
                if (normalized.Value is LACS.Value value)
                {
                    return continuation(value);
                }

                Symbol name = this.fresh.Next("t");
                return new LACS.Let(name, AsBoundTerm(normalized), continuation(name));
            });

        private LACS.Term NormalizeNames(
            Seq<LCS.Term> terms, Func<Seq<LACS.Value>, LACS.Term> continuation)
        {
            var names = new LACS.Value[terms.Count];
            return Next(0);

            LACS.Term Next(int index) => index == terms.Count
                ? continuation([.. names])
                : this.NormalizeName(terms[index], value =>
                {
                    names[index] = value;
                    return Next(index + 1);
                });
        }
    }
}

internal sealed class FreshNames
{
    private readonly HashSet<Symbol> used = [];
    private int counter;

    internal FreshNames()
    {
    }

    internal FreshNames(SExpr term)
    {
        Stack<SExpr> pending = new([term]);
        while (pending.TryPop(out SExpr? datum))
        {
            switch (datum)
            {
                case SExpr.Sym(var symbol):
                    this.used.Add(symbol);
                    break;
                case SExpr.Pair(var head, var tail):
                    pending.Push(tail);
                    pending.Push(head);
                    break;
            }
        }
    }

    internal Symbol Claim(Symbol name) => this.used.Add(name) ? name : this.Next(name);

    internal Symbol Next(Symbol stem)
    {
        Symbol name;
        do
        {
            name = new(stem.Name + "." + (++this.counter).ToString(CultureInfo.InvariantCulture));
        }
        while (!this.used.Add(name));

        return name;
    }
}

[Pass(typeof(LACS), typeof(RawTerm))]
internal sealed partial class GenerateJavaScript
{
    private const string Runtime =
        "// JavaScript Primitives\n"
        + "function plus(...args) { return args.reduce((r, x) => r + x, 0); }\n"
        + "function minus(first, ...rest) { return rest.reduce((r, x) => r - x, first); }\n"
        + "function mult(...args) { return args.reduce((r, x) => r * x, 1); }\n"
        + "function div(first, ...rest) { return rest.reduce((r, x) => r / x, first); }\n"
        + "function zero_p(v) { return v === 0; }\n"
        + "// CoreScheme Program";

    public SExpr Run(LACS.Term input)
    {
        string program = Text(this.EmitTerm(input, "result"));
        return new SExpr.Str($"{Runtime}\nlet result;\n{program};\nconsole.log(result);\n");
    }

    [Processor("Value", "*")]
    private partial SExpr EmitValue(LACS.Value ir);

    [Processor("Application", "*")]
    private partial SExpr EmitApplication(LACS.Application ir, string dest);

    [Processor("BoundTerm", "*")]
    private partial SExpr EmitBoundTerm(LACS.BoundTerm ir, string dest);

    [Processor("Term", "*")]
    private partial SExpr EmitTerm(LACS.Term ir, string dest);

    [Rewrite(Processor = nameof(EmitValue))]
    private static SExpr.Str EmitConstant(SExpr c) => new(c switch
    {
        SExpr.Int(var value) => value.ToString(CultureInfo.InvariantCulture),
        SExpr.Float(var value) => value.ToString("R", CultureInfo.InvariantCulture),
        SExpr.Bool(var value) => value ? "true" : "false",
        SExpr.Str(var value) => JsonSerializer.Serialize(value),
        _ => throw new NanopassException($"Invalid CoreScheme constant: {c}."),
    });

    [Rewrite(Processor = nameof(EmitValue))]
    private static SExpr.Str EmitVariable(Symbol x) => new(Identifier(x));

    [Rewrite(Processor = nameof(EmitValue))]
    private SExpr.Str EmitLambda(LACS.Λ node)
    {
        var (parameters, body) = node;
        return new($"function({string.Join(",", parameters.Select(Identifier))})"
            + $"{{let $value;\n{Text(this.EmitTerm(body, "$value"))};\nreturn $value;\n}}");
    }

    [Rewrite(typeof(LACS.Call), Processor = nameof(EmitApplication))]
    private static SExpr.Str EmitCall(
        [Recur(Field = "V", Processor = nameof(EmitValue))] SExpr procedure,
        [Recur(Field = "V1", Processor = nameof(EmitValue))] Seq<SExpr> operands, string dest) =>
        new($"{dest}=({Text(procedure)})({Arguments(operands)})");

    [Rewrite(Processor = nameof(EmitApplication))]
    private static SExpr.Str EmitPrimitive(
        LACS.Prim node,
        [Recur(Field = "V1", Processor = nameof(EmitValue))] Seq<SExpr> operands, string dest) =>
        new($"{dest}={PrimitiveName(node.O)}({Arguments(operands)})");

    [Rewrite(typeof(LACS.Value), Processor = nameof(EmitBoundTerm))]
    [Rewrite(typeof(LACS.Value), Processor = nameof(EmitTerm))]
    private static SExpr.Str EmitValueStore(
        [Recur(Field = "V", Processor = nameof(EmitValue))] SExpr value, string dest) =>
        new($"{dest}={Text(value)}");

    [Rewrite(typeof(LACS.Application), Processor = nameof(EmitBoundTerm))]
    [Rewrite(typeof(LACS.Application), Processor = nameof(EmitTerm))]
    private static SExpr EmitApplicationStore(
        [Recur(Field = "A", Processor = nameof(EmitApplication))] SExpr application) => application;

    [Rewrite(Processor = nameof(EmitTerm))]
    private SExpr.Str EmitLet(LACS.Let node, string dest)
    {
        string local = Identifier(node.X);
        return new($"(()=>{{let {local};\n{Text(this.EmitBoundTerm(node.B, local))};\n"
            + $"{Text(this.EmitTerm(node.M, dest))};\n}})()");
    }

    [Rewrite(typeof(LACS.If), Processor = nameof(EmitTerm))]
    private static SExpr.Str EmitIf(
        [Recur(Field = "V", Processor = nameof(EmitValue))] SExpr test,
        [Recur(Field = "M1")] SExpr consequent,
        [Recur(Field = "M2")] SExpr alternative) =>
        new($"(({Text(test)}!==false)?({Text(consequent)}):({Text(alternative)}))");

    private static string Text(SExpr emitted) => ((SExpr.Str)emitted).Value;

    private static string Arguments(Seq<SExpr> values) => string.Join(",", values.Select(Text));

    private static string PrimitiveName(Symbol primitive) => primitive.Name switch
    {
        "+" => "plus",
        "-" => "minus",
        "*" => "mult",
        "/" => "div",
        "zero?" => "zero_p",
        _ => throw new NanopassException($"Unknown primitive: {primitive}."),
    };

    private static string Identifier(Symbol symbol)
    {
        var name = new StringBuilder();
        foreach (char character in symbol.Name)
        {
            name.Append(character switch
            {
                '_' => "__",
                '-' => "_u",
                '?' => "_p",
                '.' => "_d",
                '/' => "_w",
                _ when char.IsAsciiLetterOrDigit(character) => character.ToString(),
                _ => "_x" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
            });
        }

        string id = name.ToString();
        return id.Length == 0 || char.IsAsciiDigit(id[0]) || id is
            "arguments" or "await" or "break" or "case" or "catch" or "class" or "const"
            or "continue" or "debugger" or "default" or "delete" or "do" or "else" or "enum"
            or "eval" or "export" or "extends" or "false" or "finally" or "for" or "function"
            or "if" or "implements" or "import" or "in" or "instanceof" or "interface" or "let"
            or "new" or "null" or "package" or "private" or "protected" or "public" or "return"
            or "static" or "super" or "switch" or "this" or "throw" or "true" or "try" or "typeof"
            or "var" or "void" or "while" or "with" or "yield" or "NaN" or "Infinity"
            or "result" or "console" or "plus" or "minus" or "mult" or "div"
            ? "$" + id : id;
    }
}

internal static class CoreSchemeCompiler
{
    internal static Pipeline<SExpr, SExpr> Pipeline { get; } =
        Nanopass.Pipeline.Start<SExpr>()
            .Then(new ParseCoreScheme())
            .Then(new AlphaRenameCoreScheme())
            .Then(new ANormalizeCoreScheme())
            .Then(new GenerateJavaScript());
}
