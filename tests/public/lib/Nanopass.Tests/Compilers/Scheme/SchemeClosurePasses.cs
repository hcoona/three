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

using DirectCalls = System.Collections.Immutable.ImmutableDictionary<
    Hcoona.Nanopass.Symbol, Hcoona.Nanopass.Symbol>;

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

[Pass(typeof(L4), typeof(L5))]
internal sealed partial class RemoveImpureLetrec
{
    public L5.Expr Run(L4.Expr input) => this.Run(input, new());

    internal L5.Expr Run(L4.Expr input, SchemeSymbols symbols) => this.Lower(input, symbols);

    [Processor("Expr", "Expr")]
    private partial L5.Expr Lower(L4.Expr ir, SchemeSymbols symbols);

    [Processor("SetBody", "SetBody")]
    private partial L5.SetBody LowerSetBody(L4.SetBody ir, SchemeSymbols symbols);

    [Rewrite(Processor = nameof(Lower))]
    private static L5.Expr Lambda(L4.Lambda node, [Recur(Field = "Sbody")] L5.SetBody body) =>
        (L5.LambdaExpr)new L5.Lambda(node.XList, body);

    [Rewrite(Processor = nameof(Lower), Guard = nameof(IsPure))]
    private L5.Expr PureLetrec(L4.Letrec node, SchemeSymbols symbols)
    {
        var body = (L4.Settable)node.Sbody.Value!;
        var lambdas = node.EList.Select(item =>
        {
            var lambda = (L4.Lambda)item.Value!;
            return (L5.LambdaExpr)new L5.Lambda(lambda.XList, this.LowerSetBody(lambda.Sbody, symbols));
        });
        return new L5.Letrec(node.XList, lambdas, this.Lower(body.Body, symbols));
    }

    [Rewrite(Processor = nameof(Lower))]
    private L5.Expr ImpureLetrec(
        L4.Letrec node, [Recur(Field = "EList")] Seq<L5.Expr> values, SchemeSymbols symbols)
    {
        var names = node.XList;
        var body = this.Lower(((L4.Settable)node.Sbody.Value!).Body, symbols);
        var temps = names.Select(symbols.GenSymbol);
        Seq<L5.Expr> assignments = [.. names.Zip(temps, static (name, temp) =>
            (L5.Expr)new L5.Set(name, new L5.Var(temp))).Reverse()];
        return new L5.Let(names, names.Select(static _ => (L5.Expr)new L5.Primapp("void", [])),
            new L5.Settable(names, new L5.Begin([
                new L5.Primapp("void", []),
                new L5.Let(temps, values,
                    new L5.Settable([], new L5.Begin(assignments[..^1], assignments[^1]))),
            ], body)));
    }

    private static bool IsPure(L4.Letrec node) =>
        node.Sbody.Value is L4.Settable { XList.IsEmpty: true }
        && node.EList.All(static item => item.Value is L4.Lambda);
}

[Pass(typeof(L5), typeof(L6))]
internal sealed partial class RemoveSetBang
{
    public L6.Expr Run(L5.Expr input) => this.Run(input, new());

    internal L6.Expr Run(L5.Expr input, SchemeSymbols symbols) => this.Lower(input, [], symbols);

    [Processor("Expr", "Expr")]
    private partial L6.Expr Lower(L5.Expr ir, Seq<Symbol> boxed, SchemeSymbols symbols);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L6.LambdaExpr LowerLambda(L5.LambdaExpr ir, Seq<Symbol> boxed, SchemeSymbols symbols);

    [Rewrite(Processor = nameof(Lower))]
    private static L6.Expr Var(L5.Var node, Seq<Symbol> boxed) =>
        boxed.Contains(node.X)
            ? new L6.Primapp("car", [new L6.Var(node.X)])
            : (L6.Expr)new L6.Var(node.X);

    [Rewrite(Processor = nameof(Lower))]
    private static L6.Expr Set(L5.Set node, [Recur(Field = "E")] L6.Expr value) =>
        new L6.Primapp("set-car!", [new L6.Var(node.X), value]);

    [Rewrite(Processor = nameof(Lower))]
    private L6.Expr Let(
        L5.Let node, [Recur(Field = "EList")] Seq<L6.Expr> values, Seq<Symbol> boxed, SchemeSymbols symbols)
    {
        var (formals, body) = this.SetBody(node.Sbody, node.XList, boxed, symbols);
        return new L6.Let(formals, values, body);
    }

    [Rewrite(Processor = nameof(LowerLambda))]
    private L6.LambdaExpr Lambda(L5.Lambda node, Seq<Symbol> boxed, SchemeSymbols symbols)
    {
        var (formals, body) = this.SetBody(node.Sbody, node.XList, boxed, symbols);
        return new L6.Lambda(formals, body);
    }

    private (Seq<Symbol>, L6.Expr) SetBody(
        L5.SetBody node, Seq<Symbol> names, Seq<Symbol> boxed, SchemeSymbols symbols)
    {
        var (assigned, body) = (L5.Settable)node.Value!;
        var result = this.Lower(body, [.. assigned, .. boxed], symbols);
        if (assigned.IsEmpty)
        {
            return (names, result);
        }

        var parameters = names.ToArray();
        List<(Symbol Name, L6.Expr Value)> bindings = [];
        for (int i = names.Count - 1; i >= 0; i--)
        {
            var name = names[i];
            if (assigned.Contains(name))
            {
                Symbol temp = symbols.GenSymbol(name);
                parameters[i] = temp;
                bindings.Add((name, new L6.Primapp("cons", [new L6.Var(temp), new L6.Primapp("void", [])])));
            }
        }

        bindings.Reverse();
        return ([.. parameters], new L6.Let(
            [.. bindings.Select(binding => binding.Name)], [.. bindings.Select(binding => binding.Value)], result));
    }
}

[Pass(typeof(L6), typeof(L6))]
internal sealed partial class SanitizeBinding
{
    [Rewrite]
    private static L6.Expr Let(
        L6.Let node, [Recur(Field = "EList")] Seq<L6.Expr> values, [Recur(Field = "Body")] L6.Expr body)
    {
        List<Symbol> localNames = [];
        List<L6.Expr> locals = [];
        List<Symbol> recursiveNames = [];
        List<L6.LambdaExpr> lambdas = [];
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i].Value is L6.LambdaExpr lambda)
            {
                recursiveNames.Add(node.XList[i]);
                lambdas.Add(lambda);
            }
            else
            {
                localNames.Add(node.XList[i]);
                locals.Add(values[i]);
            }
        }

        var local = new L6.Let([.. localNames], [.. locals], body);
        return lambdas.Count == 0 ? local :
            (L6.Expr)new L6.Letrec([.. recursiveNames], [.. lambdas], locals.Count == 0 ? body : local);
    }
}

[Pass(typeof(L6), typeof(L7))]
internal sealed partial class RemoveAnonymousLambda
{
    public L7.Expr Run(L6.Expr input) => this.Run(input, new());

    internal L7.Expr Run(L6.Expr input, SchemeSymbols symbols) => this.Lower(input, symbols);

    [Processor("Expr", "Expr")]
    private partial L7.Expr Lower(L6.Expr ir, SchemeSymbols symbols);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L7.LambdaExpr LowerLambda(L6.LambdaExpr ir, SchemeSymbols symbols);

    [Rewrite(typeof(L6.LambdaExpr), Processor = nameof(Lower))]
    private static L7.Expr Lambda(
        [Recur(Field = "lexpr", Processor = nameof(LowerLambda))] L7.LambdaExpr lambda,
        SchemeSymbols symbols)
    {
        Symbol anonymous = symbols.GenSymbol("anon");
        return new L7.Letrec([anonymous], [lambda], new L7.Var(anonymous));
    }
}

[Pass(typeof(L7), typeof(L8))]
internal sealed partial class UncoverFree
{
    public L8.Expr Run(L7.Expr input) => this.Lower(input, []).Term;

    [Processor("Expr", "Expr")]
    private partial (L8.Expr Term, Seq<Symbol> Free) Lower(L7.Expr ir, Seq<Symbol> free);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial (L8.LambdaExpr Term, Seq<Symbol> Free) LowerLambda(L7.LambdaExpr ir, Seq<Symbol> free);

    [Rewrite(Processor = nameof(Lower))]
    private static (L8.Expr, Seq<Symbol>) Var(L7.Var node, Seq<Symbol> free) =>
        (new L8.Var(node.X), [node.X, .. free]);

    [Rewrite(Processor = nameof(Lower))]
    private static (L8.Expr, Seq<Symbol>) QuotedConst(L7.QuotedConst node, Seq<Symbol> free) =>
        (new L8.QuotedConst(node.D), free);

    [Rewrite(Processor = nameof(Lower))]
    private (L8.Expr, Seq<Symbol>) Let(
        L7.Let node, [Recur(Field = "Body")] (L8.Expr Term, Seq<Symbol> Free) body)
    {
        var (values, free) = this.LowerAll(node.EList, SchemeHelpers.Difference(body.Free, node.XList));
        return (new L8.Let(node.XList, values, body.Term), free);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L8.Expr, Seq<Symbol>) Letrec(
        L7.Letrec node, [Recur(Field = "Body")] (L8.Expr Term, Seq<Symbol> Free) body)
    {
        var free = body.Free;
        var lambdas = node.LexprList.Select(item =>
        {
            var (lambda, updated) = this.LowerLambda(item, free);
            free = updated;
            return lambda;
        });
        return (new L8.Letrec(node.XList, lambdas, body.Term), SchemeHelpers.Difference(free, node.XList));
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L8.Expr, Seq<Symbol>) If(
        L7.If3 node, [Recur(Field = "E1")] (L8.Expr Term, Seq<Symbol> Free) test)
    {
        var (yes, next) = this.Lower(node.E2, test.Free);
        var (no, free) = this.Lower(node.E3, next);
        return (new L8.If3(test.Term, yes, no), free);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L8.Expr, Seq<Symbol>) Begin(
        L7.Begin node, [Recur(Field = "E2")] (L8.Expr Term, Seq<Symbol> Free) last)
    {
        var (effects, free) = this.LowerAll(node.E1List, last.Free);
        return (new L8.Begin(effects, last.Term), free);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L8.Expr, Seq<Symbol>) Primapp(L7.Primapp node, Seq<Symbol> free)
    {
        var (arguments, updated) = this.LowerAll(node.EList, free);
        return (new L8.Primapp(node.Pr, arguments), updated);
    }

    [Rewrite(Processor = nameof(Lower))]
    private (L8.Expr, Seq<Symbol>) App(
        L7.App node, [Recur(Field = "E0")] (L8.Expr Term, Seq<Symbol> Free) procedure)
    {
        var (arguments, free) = this.LowerAll(node.E1List, procedure.Free);
        return (new L8.App(procedure.Term, arguments), free);
    }

    [Rewrite(Processor = nameof(LowerLambda))]
    private static (L8.LambdaExpr, Seq<Symbol>) Lambda(
        L7.Lambda node, [Recur(Field = "Body", Processor = nameof(Lower))] (L8.Expr Term, Seq<Symbol> Free) body)
    {
        var free = SchemeHelpers.Difference(body.Free, node.XList);
        return (new L8.Lambda(node.XList, new L8.Free(free, body.Term)), free);
    }

    private (Seq<L8.Expr>, Seq<Symbol>) LowerAll(Seq<L7.Expr> terms, Seq<Symbol> free)
    {
        var result = terms.Select(item =>
        {
            var (term, updated) = this.Lower(item, free);
            free = updated;
            return term;
        });
        return (result, free);
    }
}

[Pass(typeof(L8), typeof(L9))]
internal sealed partial class ConvertClosure
{
    public L9.Expr Run(L8.Expr input) => this.Run(input, new());

    internal L9.Expr Run(L8.Expr input, SchemeSymbols symbols) => this.Lower(input, DirectCalls.Empty, symbols);

    [Processor("Expr", "Expr")]
    private partial L9.Expr Lower(L8.Expr ir, DirectCalls direct, SchemeSymbols symbols);

    [Rewrite(Processor = nameof(Lower), Guard = nameof(IsDirect))]
    private static L9.Expr DirectApplication(
        L8.App node, [Recur(Field = "E1List")] Seq<L9.Expr> arguments, DirectCalls direct)
    {
        var callee = (L8.Var)node.E0.Value!;
        return new L9.App(new L9.Var(direct[callee.X]), [new L9.Var(callee.X), .. arguments]);
    }

    [Rewrite(Processor = nameof(Lower))]
    private static L9.Expr IndirectApplication(
        L8.App node, [Recur(Field = "E0")] L9.Expr callee,
        [Recur(Field = "E1List")] Seq<L9.Expr> arguments) =>
        new L9.AnonymousCall(callee, arguments);

    [Rewrite(Processor = nameof(Lower))]
    private L9.Expr Letrec(L8.Letrec node, DirectCalls direct, SchemeSymbols symbols)
    {
        var labels = node.XList.Select(SchemeSymbols.GenLabel);
        var pointers = node.XList.Select(_ => symbols.GenSymbol("cp"));
        var locals = direct.SetItems(node.XList.Zip(labels, static (name, label) => KeyValuePair.Create(name, label)));
        var lambdas = new L9.LambdaExpr[labels.Count];
        var closures = new L9.Closure[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            var (formals, freeBody) = (L8.Lambda)node.LexprList[i].Value!;
            var (free, body) = (L8.Free)freeBody.Value!;
            var cp = pointers[i];
            lambdas[i] = new L9.Lambda([cp, .. formals], new L9.BindFree.BindFreeNode(cp, free,
                this.Lower(body, locals, symbols)));
            closures[i] = new L9.Closure.ClosureNode(labels[i], free);
        }

        return new L9.Letrec(labels, [.. lambdas],
            new L9.ClosureLetrec.ClosureLetrecNode(node.XList, [.. closures], this.Lower(node.Body, locals, symbols)));
    }

    private static bool IsDirect(L8.App node, DirectCalls direct) =>
        node.E0.Value is L8.Var(var name) && direct.ContainsKey(name);
}

[Pass(typeof(L9), typeof(L10))]
internal sealed partial class LiftLetrec
{
    public L10.LetrecExpr Run(L9.Expr input)
    {
        Bindings bindings = new();
        var body = this.Lower(input, bindings);
        bindings.Items.Reverse();
        return new L10.Letrec(
            [.. bindings.Items.Select(item => item.Label)], [.. bindings.Items.Select(item => item.Code)], body);
    }

    [Processor("Expr", "Expr")]
    private partial L10.Expr Lower(L9.Expr ir, Bindings bindings);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L10.LambdaExpr LowerLambda(L9.LambdaExpr ir, Bindings bindings);

    [Processor("BindFree", "BindFree")]
    private partial L10.BindFree LowerBindFree(L9.BindFree ir, Bindings bindings);

    [Processor("ClosureLetrec", "Expr")]
    private partial L10.Expr LowerClosureLetrec(L9.ClosureLetrec ir, Bindings bindings);

    [Rewrite(Processor = nameof(Lower))]
    private L10.Expr If(
        L9.If node, [Recur(Field = "E3")] L10.Expr other, Bindings bindings) =>
        new L10.If(this.Lower(node.E1, bindings), this.Lower(node.E2, bindings), other);

    [Rewrite(Processor = nameof(Lower))]
    private L10.Expr Begin(
        L9.Begin node, [Recur(Field = "E2")] L10.Expr last, Bindings bindings) =>
        new L10.Begin(node.E1List.Select(item => this.Lower(item, bindings)), last);

    [Rewrite(Processor = nameof(Lower))]
    private L10.Expr Let(
        L9.Let node, [Recur(Field = "Body")] L10.Expr body, Bindings bindings) =>
        new L10.Let(node.XList, node.EList.Select(item => this.Lower(item, bindings)), body);

    [Rewrite(Processor = nameof(Lower))]
    private L10.Expr Letrec(
        L9.Letrec node,
        [Recur(Field = "CLetrec", Processor = nameof(LowerClosureLetrec))] L10.Expr body,
        Bindings bindings)
    {
        var lambdas = node.LexprList.Select(item => this.LowerLambda(item, bindings));
        for (int i = lambdas.Count - 1; i >= 0; i--)
        {
            bindings.Items.Add((node.XList[i], lambdas[i]));
        }

        return body;
    }

    [Rewrite(Processor = nameof(LowerClosureLetrec))]
    private static L10.Expr ClosureLetrec(
        L9.ClosureLetrec.ClosureLetrecNode node,
        [Recur(Field = "CExpList")] Seq<L10.Closure> closures,
        [Recur(Field = "Body", Processor = nameof(Lower))] L10.Expr body) =>
        new L10.ClosureLetrec(node.XList, closures, body);

    private sealed class Bindings
    {
        internal List<(Symbol Label, L10.LambdaExpr Code)> Items { get; } = [];
    }
}

[Pass(typeof(L10), typeof(L11))]
internal sealed partial class ExplicitClosure
{
    [Processor("Expr", "Expr")]
    private partial L11.Expr Lower(L10.Expr ir, Symbol cp, Seq<Symbol> free);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L11.LambdaExpr LowerLambda(L10.LambdaExpr ir);

    [Processor("Closure", "Expr")]
    private partial (L11.Expr Term, Seq<Symbol> Free) LowerClosure(L10.Closure ir);

    [Rewrite]
    private L11.LetrecExpr Letrec(
        L10.Letrec node,
        [Recur(Field = "LexprList", Processor = nameof(LowerLambda))] Seq<L11.LambdaExpr> lambdas) =>
        new L11.Letrec(node.XList, lambdas, this.Lower(node.E, default, []));

    [Rewrite(Processor = nameof(LowerLambda))]
    private L11.LambdaExpr Lambda(L10.Lambda node)
    {
        var (cp, free, body) = (L10.BindFree.BindFreeNode)node.BfBody.Value!;
        return new L11.Lambda(node.XList, this.Lower(body, cp, free));
    }

    [Rewrite(Processor = nameof(Lower))]
    private static L11.Expr Var(L10.Var node, Symbol cp, Seq<Symbol> free) =>
        Reference(node.X, cp, free);

    [Rewrite(Processor = nameof(Lower))]
    private static L11.Expr ClosureLetrec(
        L10.ClosureLetrec node,
        [Recur(Field = "CExpList", Processor = nameof(LowerClosure))]
        Seq<(L11.Expr Term, Seq<Symbol> Free)> closures,
        [Recur(Field = "Body")] L11.Expr body, Symbol cp, Seq<Symbol> free)
    {
        List<L11.Expr> setup = [];
        for (int i = 0; i < closures.Count; i++)
        {
            var name = new L11.Var(node.XList[i]);
            var captured = closures[i].Free;
            for (int slot = 0; slot < captured.Count; slot++)
            {
                setup.Add(new L11.SysPrimapp("closure-set!",
                    [name, new L11.QuotedConst(new SExpr.Int(slot)), Reference(captured[slot], cp, free)]));
            }
        }

        return new L11.Let(node.XList, closures.Select(item => item.Term), new L11.Begin([.. setup], body));
    }

    [Rewrite(Processor = nameof(LowerClosure))]
    private static (L11.Expr, Seq<Symbol>) Closure(L10.Closure.ClosureNode node) =>
        (new L11.SysPrimapp("make-closure",
            [new L11.Var(node.X1), new L11.QuotedConst(new SExpr.Int(node.X2List.Count))]), node.X2List);

    private static L11.Expr Reference(Symbol name, Symbol cp, Seq<Symbol> free)
    {
        int index = SchemeHelpers.ListIndex(name, free);
        return index < 0 ? new L11.Var(name)
            : (L11.Expr)new L11.SysPrimapp("closure-ref", [new L11.Var(cp), new L11.QuotedConst(new SExpr.Int(index))]);
    }
}
