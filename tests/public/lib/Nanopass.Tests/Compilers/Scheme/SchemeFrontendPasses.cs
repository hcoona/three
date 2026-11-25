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

// Adapted from the MIT-licensed Nanopass Compiler Framework (Copyright (c) 2000-2015 Dipanwita Sarkar, Andrew W. Keep, R. Kent Dybvig, Oscar Waddell). See src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

using RenameEnvironment = System.Collections.Immutable.ImmutableDictionary<
    Hcoona.Nanopass.Symbol, Hcoona.Nanopass.Symbol>;

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

[Pass(typeof(RawTerm), typeof(L0))]
internal sealed partial class RenameVarsVerifyLegal
{
    public L0.Expr Run(SExpr input) => this.Run(input, new());

    internal L0.Expr Run(SExpr input, SchemeSymbols symbols) =>
        this.Read(input, RenameEnvironment.Empty, symbols);

    [Processor("*", "Expr")]
    private L0.Expr Read(SExpr ir, RenameEnvironment env, SchemeSymbols symbols)
    {
        L0.Expr Lower(SExpr value) => this.Read(value, env, symbols);
        if (SchemeHelpers.IsConstant(ir))
        {
            return new L0.Datum(ir);
        }

        if (ir is SExpr.Sym(var name))
        {
            return new L0.Var(Lookup(name, env, "reference"));
        }

        if (!ir.TryToProperList(out var form) || form.IsEmpty)
        {
            throw new NanopassException($"Invalid Scheme expression: {ir}.");
        }

        if (form[0] is SExpr.Sym(var head) && !env.ContainsKey(head))
        {
            switch (head.Name)
            {
                case "quote" when form.Count == 2 && SchemeHelpers.IsDatum(form[1]):
                    return new L0.Datum(form[1]);
                case "set!" when form.Count == 3 && form[1] is SExpr.Sym(var lhs):
                    return new L0.Set(Lookup(lhs, env, "assignment"), Lower(form[2]));
                case "if" when form.Count == 3:
                    return new L0.If2(Lower(form[1]), Lower(form[2]));
                case "if" when form.Count == 4:
                    return new L0.If3(Lower(form[1]), Lower(form[2]), Lower(form[3]));
                case "begin" when form.Count >= 2:
                    return new L0.Begin(form[1..^1].Select(Lower), Lower(form[^1]));
                case "lambda" when form.Count >= 3:
                    var parameters = SchemeHelpers.RequireList(form[1]).Select(SchemeHelpers.RequireSymbol);
                    var (renamed, bodyEnvironment) = Extend(parameters, env, symbols, head);
                    return new L0.Lambda(
                        renamed, form[2..^1].Select(item => this.Read(item, bodyEnvironment, symbols)),
                        this.Read(form[^1], bodyEnvironment, symbols));
                case "let" or "letrec" when form.Count >= 3:
                    var binding = SchemeHelpers.RequireList(form[1]).Select(ReadBinding);
                    var (names, bodyEnv) = Extend(binding.Select(pair => pair.Name), env, symbols, head);
                    bool recursive = head.Name == "letrec";
                    var values = binding.Select(pair => this.Read(pair.Value, recursive ? bodyEnv : env, symbols));
                    var prefix = form[2..^1].Select(item => this.Read(item, bodyEnv, symbols));
                    var final = this.Read(form[^1], bodyEnv, symbols);
                    return recursive ? new L0.Letrec(names, values, prefix, final)
                        : (L0.Expr)new L0.Let(names, values, prefix, final);
                default:
                    if (SchemeHelpers.IsUserPrimitive(head))
                    {
                        if (!SchemeHelpers.AcceptsUserArity(head, form.Count - 1))
                        {
                            throw new NanopassException($"Wrong arity for Scheme primitive {head}: {ir}.");
                        }

                        return new L0.Primapp(head, form[1..].Select(Lower));
                    }

                    break;
            }
        }

        return new L0.App(Lower(form[0]), form[1..].Select(Lower));
    }

    private static (Symbol Name, SExpr Value) ReadBinding(SExpr raw) =>
        SchemeHelpers.RequireList(raw) is [SExpr.Sym(var name), var value] ? (name, value) :
            throw new NanopassException($"Invalid Scheme binding: {raw}.");

    private static (Seq<Symbol>, RenameEnvironment) Extend(
        Seq<Symbol> names, RenameEnvironment env, SchemeSymbols symbols, Symbol form)
    {
        if (!SchemeHelpers.IsSet(names))
        {
            throw new NanopassException($"Duplicate bindings in {form} form.");
        }

        var renamed = names.Select(symbols.GenSymbol);
        return (renamed, env.SetItems(names.Zip(renamed).Select(pair =>
            KeyValuePair.Create(pair.First, pair.Second))));
    }

    private static Symbol Lookup(Symbol name, RenameEnvironment env, string operation) =>
        env.TryGetValue(name, out var value) ? value :
            throw new NanopassException($"Invalid {operation} to {VerifyScheme.VariableKind(name)} {name}.");
}

[Pass(typeof(LP), typeof(L0))]
internal sealed partial class VerifyScheme
{
    public L0.Expr Run(LP.Expr input) => this.Verify(input, RenameEnvironment.Empty, null);

    internal L0.Expr Run(LP.Expr input, SchemeSymbols symbols) => this.Verify(input, RenameEnvironment.Empty, symbols);

    [Processor("Expr", "Expr")]
    private partial L0.Expr Verify(LP.Expr ir, RenameEnvironment env, SchemeSymbols? symbols);

    internal static string VariableKind(Symbol name) =>
        SchemeHelpers.IsKeyword(name) ? "keyword" :
        SchemeHelpers.IsUserPrimitive(name) ? "user-primitive" : "unbound variable";

    [Rewrite(typeof(SExpr), Processor = nameof(Verify))]
    private static L0.Expr Datum(SExpr d) => new L0.Datum(d);

    [Rewrite(typeof(Symbol), Processor = nameof(Verify))]
    private static L0.Expr Variable(Symbol x, RenameEnvironment env) =>
        new L0.Var(CheckVariable(x, env, "Reference"));

    [Rewrite(typeof(Symbol), Processor = nameof(Verify))]
    private static L0.Expr PrimitiveVariable(Symbol pr, RenameEnvironment env) => Variable(pr, env);

    [Rewrite(Processor = nameof(Verify))]
    private L0.Expr Assignment(LP.Set node, RenameEnvironment env, SchemeSymbols? symbols) =>
        new L0.Set(CheckVariable(node.X, env, "Assignment"), this.Verify(node.E, env, symbols));

    [Rewrite(Processor = nameof(Verify))]
    private L0.Expr Lambda(LP.Lambda node, RenameEnvironment env, SchemeSymbols? symbols)
    {
        var (names, prefix, last) = node;
        var (bound, bodyEnv) = Bind(names, env, "lambda", symbols);
        return new L0.Lambda(
            bound, prefix.Select(item => this.Verify(item, bodyEnv, symbols)), this.Verify(last, bodyEnv, symbols));
    }

    [Rewrite(Processor = nameof(Verify))]
    private L0.Expr Let(LP.Let node, RenameEnvironment env, SchemeSymbols? symbols)
    {
        var (names, values, prefix, last) = node;
        var (bound, bodyEnv) = Bind(names, env, "let", symbols);
        return new L0.Let(bound, values.Select(value => this.Verify(value, env, symbols)),
            prefix.Select(item => this.Verify(item, bodyEnv, symbols)), this.Verify(last, bodyEnv, symbols));
    }

    [Rewrite(Processor = nameof(Verify))]
    private L0.Expr Letrec(LP.Letrec node, RenameEnvironment env, SchemeSymbols? symbols)
    {
        var (names, values, prefix, last) = node;
        var (bound, bodyEnv) = Bind(names, env, "letrec", symbols);
        return new L0.Letrec(bound, values.Select(value => this.Verify(value, bodyEnv, symbols)),
            prefix.Select(item => this.Verify(item, bodyEnv, symbols)), this.Verify(last, bodyEnv, symbols));
    }

    [Rewrite(Processor = nameof(Verify))]
    private L0.Expr Application(LP.Implicit node, RenameEnvironment env, SchemeSymbols? symbols)
    {
        var (procedure, operands) = node;
        var arguments = operands.Select(item => this.Verify(item, env, symbols));
        if (procedure.Value is Symbol primitive && !env.ContainsKey(primitive) && SchemeHelpers.IsUserPrimitive(primitive))
        {
            if (!SchemeHelpers.AcceptsUserArity(primitive, operands.Count))
            {
                throw new NanopassException($"Wrong arity for Scheme primitive {primitive}.");
            }

            return new L0.Primapp(primitive, arguments);
        }

        return new L0.App(this.Verify(procedure, env, symbols), arguments);
    }

    private static Symbol CheckVariable(Symbol name, RenameEnvironment env, string operation) =>
        env.TryGetValue(name, out var bound)
            ? bound : throw new NanopassException($"{operation} to {VariableKind(name)} {name}.");

    private static (Seq<Symbol>, RenameEnvironment) Bind(
        Seq<Symbol> names, RenameEnvironment env, string form, SchemeSymbols? symbols)
    {
        if (!SchemeHelpers.IsSet(names))
        {
            var duplicates = names.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key);
            throw new NanopassException($"Duplicate bindings {string.Join(", ", duplicates)} in {form} form.");
        }

        var bound = symbols is null ? names : names.Select(symbols.GenSymbol);
        return (bound, env.SetItems(names.Zip(bound, static (name, value) => KeyValuePair.Create(name, value))));
    }
}

[Pass(typeof(L0), typeof(L1))]
internal sealed partial class RemoveImplicitBegin
{
    [Rewrite]
    private static L1.Expr Lambda(
        L0.Lambda node, [Recur(Field = "Body1List")] Seq<L1.Expr> prefix, [Recur(Field = "Body2")] L1.Expr last) =>
        new L1.Lambda(node.XList, new L1.Begin(prefix, last));

    [Rewrite]
    private static L1.Expr Let(
        L0.Let node, [Recur(Field = "EList")] Seq<L1.Expr> values,
        [Recur(Field = "Body1List")] Seq<L1.Expr> prefix, [Recur(Field = "Body2")] L1.Expr last) =>
        new L1.Let(node.XList, values, new L1.Begin(prefix, last));

    [Rewrite]
    private static L1.Expr Letrec(
        L0.Letrec node, [Recur(Field = "EList")] Seq<L1.Expr> values,
        [Recur(Field = "Body1List")] Seq<L1.Expr> prefix, [Recur(Field = "Body2")] L1.Expr last) =>
        new L1.Letrec(node.XList, values, new L1.Begin(prefix, last));
}

[Pass(typeof(L1), typeof(L2))]
internal sealed partial class RemoveUnquotedConstant
{
    [Rewrite]
    private static L2.Expr Datum(L1.Datum node) => new L2.QuotedConst(node.D);
}

[Pass(typeof(L2), typeof(L3))]
internal sealed partial class RemoveOneArmedIf
{
    [Rewrite]
    private static L3.Expr If(
        L2.If2 node, [Recur(Field = "E1")] L3.Expr test, [Recur(Field = "E2")] L3.Expr consequent) =>
        new L3.If3(test, consequent, new L3.Primapp("void", []));
}

[Pass(typeof(L3), typeof(L4))]
internal sealed partial class UncoverSettable
{
    public L4.Expr Run(L3.Expr input) => this.Lower(input, []).Term;

    [Processor("Expr", "Expr")]
    private partial (L4.Expr Term, Seq<Symbol> Assigned) Lower(L3.Expr ir, Seq<Symbol> assigned);

    [Rewrite(Processor = nameof(Lower))]
    private static (L4.Expr, Seq<Symbol>) Set(
        L3.Set node, [Recur(Field = "E")] (L4.Expr Term, Seq<Symbol> Assigned) value) =>
        (new L4.Set(node.X, value.Term), SchemeHelpers.SetCons(node.X, value.Assigned));

    [Rewrite(Processor = nameof(Lower))]
    private static (L4.Expr, Seq<Symbol>) Lambda(
        L3.Lambda node, [Recur(Field = "Body")] (L4.Expr Term, Seq<Symbol> Assigned) body)
    {
        var local = SchemeHelpers.Intersection(body.Assigned, node.XList);
        return (new L4.Lambda(node.XList, new L4.Settable(local, body.Term)),
            SchemeHelpers.Difference(body.Assigned, local));
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L4.Expr, Seq<Symbol>) Let(
        L3.Let node, [Recur(Field = "Body")] (L4.Expr Term, Seq<Symbol> Assigned) body)
    {
        var local = SchemeHelpers.Intersection(body.Assigned, node.XList);
        var (values, assigned) = this.LowerAll(node.EList, SchemeHelpers.Difference(body.Assigned, local));
        return (new L4.Let(node.XList, values, new L4.Settable(local, body.Term)), assigned);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L4.Expr, Seq<Symbol>) Letrec(
        L3.Letrec node, [Recur(Field = "Body")] (L4.Expr Term, Seq<Symbol> Assigned) body)
    {
        var (values, assigned) = this.LowerAll(node.EList, body.Assigned);
        var local = SchemeHelpers.Intersection(assigned, node.XList);
        return (new L4.Letrec(node.XList, values, new L4.Settable(local, body.Term)),
            SchemeHelpers.Difference(assigned, local));
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L4.Expr, Seq<Symbol>) Begin(
        L3.Begin node, [Recur(Field = "E2")] (L4.Expr Term, Seq<Symbol> Assigned) last)
    {
        var (prefix, assigned) = this.LowerAll(node.E1List, last.Assigned);
        return (new L4.Begin(prefix, last.Term), assigned);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L4.Expr, Seq<Symbol>) If(
        L3.If3 node, [Recur(Field = "E1")] (L4.Expr Term, Seq<Symbol> Assigned) test)
    {
        var (yes, next) = this.Lower(node.E2, test.Assigned);
        var (no, assigned) = this.Lower(node.E3, next);
        return (new L4.If3(test.Term, yes, no), assigned);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L4.Expr, Seq<Symbol>) Application(
        L3.App node, [Recur(Field = "E0")] (L4.Expr Term, Seq<Symbol> Assigned) procedure)
    {
        var (operands, assigned) = this.LowerAll(node.E1List, procedure.Assigned);
        return (new L4.App(procedure.Term, operands), assigned);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L4.Expr, Seq<Symbol>) Primitive(L3.Primapp node, Seq<Symbol> assigned)
    {
        var (operands, result) = this.LowerAll(node.EList, assigned);
        return (new L4.Primapp(node.Pr, operands), result);
    }

    [Rewrite(Processor = nameof(Lower))]
    private static (L4.Expr, Seq<Symbol>) Var(L3.Var node, Seq<Symbol> assigned) =>
        (new L4.Var(node.X), assigned);

    [Rewrite(Processor = nameof(Lower))]
    private static (L4.Expr, Seq<Symbol>) Quote(L3.QuotedConst node, Seq<Symbol> assigned) =>
        (new L4.QuotedConst(node.D), assigned);

    private (Seq<L4.Expr>, Seq<Symbol>) LowerAll(Seq<L3.Expr> terms, Seq<Symbol> assigned)
    {
        var result = new L4.Expr[terms.Count];
        for (int i = 0; i < terms.Count; i++)
        {
            (result[i], assigned) = this.Lower(terms[i], assigned);
        }

        return ([.. result], assigned);
    }
}
