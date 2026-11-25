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

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

[Pass(typeof(L14), typeof(L15))]
internal sealed partial class IntroduceDummyRp
{
    public L15.LetrecExpr Run(L14.LetrecExpr input) => this.Run(input, new());

    internal L15.LetrecExpr Run(L14.LetrecExpr input, SchemeSymbols symbols) => this.Program(input, symbols);

    [Processor("LetrecExpr", "LetrecExpr")]
    private partial L15.LetrecExpr Program(L14.LetrecExpr ir, SchemeSymbols symbols);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L15.LambdaExpr LambdaExpr(L14.LambdaExpr ir, SchemeSymbols symbols);

    [Processor("Value", "Tail")]
    private partial L15.Tail Tail(L14.Value ir, Symbol rp, SchemeSymbols symbols);

    [Processor("Value", "Nontail")]
    private partial L15.Nontail Nontail(L14.Value ir, SchemeSymbols symbols);

    [Processor("Predicate", "Predicate")]
    private partial L15.Predicate Predicate(L14.Predicate ir, SchemeSymbols symbols);

    [Processor("Effect", "Effect")]
    private partial L15.Effect Effect(L14.Effect ir, SchemeSymbols symbols);

    [Rewrite(Processor = nameof(Program))]
    private L15.LetrecExpr Letrec(
        L14.Letrec node, [Recur(Field = "LexprList")] Seq<L15.LambdaExpr> lambdas, SchemeSymbols symbols)
    {
        Symbol rp = symbols.GenSymbol("rp");
        return new L15.Letrec(node.XList, lambdas, new L15.Run(rp, this.Tail(node.V, rp, symbols)));
    }

    [Rewrite(Processor = nameof(LambdaExpr))]
    private L15.LambdaExpr Lambda(L14.Lambda node, SchemeSymbols symbols)
    {
        Symbol rp = symbols.GenSymbol("rp");
        return new L15.Lambda([rp, .. node.XList], this.Tail(node.V, rp, symbols));
    }

    [Rewrite(typeof(L14.Triv), Processor = nameof(Tail))]
    private static L15.Tail TailTriv([Recur(Field = "t")] L15.Triv value, Symbol rp) =>
        new L15.Return(new L15.Var(rp), value);

    [Rewrite(Processor = nameof(Tail))]
    private static L15.Tail TailIf(
        L14.Value.If node, [Recur(Field = "P1")] L15.Predicate test,
        [Recur(Field = "V2")] L15.Tail yes, [Recur(Field = "V3")] L15.Tail no) =>
        new L15.Tail.If(test, yes, no);

    [Rewrite(Processor = nameof(Tail))]
    private static L15.Tail TailBegin(
        L14.Value.Begin node, [Recur(Field = "F0List")] Seq<L15.Effect> effects,
        [Recur(Field = "V1")] L15.Tail end) => new L15.Tail.Begin(effects, end);

    [Rewrite(Processor = nameof(Tail))]
    private static L15.Tail TailLet(
        L14.Value.Let node, [Recur(Field = "V1List", Processor = nameof(Nontail))] Seq<L15.Nontail> values,
        [Recur(Field = "V2")] L15.Tail body) => new L15.Tail.Let(node.XList, values, body);

    [Rewrite(Processor = nameof(Tail))]
    private static L15.Tail TailPrimapp(
        L14.Value.Primapp node, [Recur(Field = "TList")] Seq<L15.Triv> operands,
        Symbol rp, SchemeSymbols symbols) =>
        ReturnPrimitive(new L15.Nontail.Primapp(node.Vp, operands), rp, symbols);

    [Rewrite(Processor = nameof(Tail))]
    private static L15.Tail TailSysPrimapp(
        L14.Value.SysPrimapp node, [Recur(Field = "TList")] Seq<L15.Triv> operands,
        Symbol rp, SchemeSymbols symbols) =>
        ReturnPrimitive(new L15.Nontail.SysPrimapp(node.Spr, operands), rp, symbols);

    [Rewrite(Processor = nameof(Tail))]
    private static L15.Tail TailApp(
        L14.Value.App node, [Recur(Field = "T0")] L15.Triv target,
        [Recur(Field = "T1List")] Seq<L15.Triv> arguments, Symbol rp) =>
        new L15.Tail.App(target, [new L15.Var(rp), .. arguments]);

    [Rewrite(Processor = nameof(Nontail))]
    private static L15.Nontail NontailLet(
        L14.Value.Let node, [Recur(Field = "V1List")] Seq<L15.Nontail> values,
        [Recur(Field = "V2")] L15.Nontail body) => new L15.Nontail.Let(node.XList, values, body);

    [Rewrite(Processor = nameof(Nontail))]
    private static L15.Nontail NontailIf(
        L14.Value.If node, [Recur(Field = "P1")] L15.Predicate test,
        [Recur(Field = "V2")] L15.Nontail yes, [Recur(Field = "V3")] L15.Nontail no) =>
        new L15.Nontail.If(test, yes, no);

    [Rewrite(Processor = nameof(Nontail))]
    private static L15.Nontail NontailBegin(
        L14.Value.Begin node, [Recur(Field = "F0List")] Seq<L15.Effect> effects,
        [Recur(Field = "V1")] L15.Nontail end) => new L15.Nontail.Begin(effects, end);

    [Rewrite(Processor = nameof(Nontail))]
    private static L15.Nontail NontailApp(
        L14.Value.App node, [Recur(Field = "T0")] L15.Triv target,
        [Recur(Field = "T1List")] Seq<L15.Triv> arguments, SchemeSymbols symbols)
    {
        var (label, app) = ReturnPoint(target, arguments, symbols);
        return new L15.Nontail.ReturnPoint(label, app);
    }

    [Rewrite(Processor = nameof(Predicate))]
    private static L15.Predicate PredicateLet(
        L14.Predicate.Let node, [Recur(Field = "VList", Processor = nameof(Nontail))] Seq<L15.Nontail> values,
        [Recur(Field = "P")] L15.Predicate body) => new L15.Predicate.Let(node.XList, values, body);

    [Rewrite(Processor = nameof(Predicate))]
    private static L15.Predicate PredicateApp(
        L14.Predicate.App node, [Recur(Field = "T0")] L15.Triv target,
        [Recur(Field = "T1List")] Seq<L15.Triv> arguments, SchemeSymbols symbols)
    {
        var (label, app) = ReturnPoint(target, arguments, symbols);
        Symbol result = symbols.GenSymbol("tmp");
        var isFalse = new L15.Predicate.Primapp("eq?", [new L15.Var(result), new L15.QuotedConst(new SExpr.Bool(false))]);
        return new L15.Predicate.Let([result], [new L15.Nontail.ReturnPoint(label, app)],
            new L15.Predicate.If(isFalse, new L15.False(), new L15.True()));
    }

    [Rewrite(Processor = nameof(Effect))]
    private static L15.Effect EffectLet(
        L14.Effect.Let node, [Recur(Field = "VList", Processor = nameof(Nontail))] Seq<L15.Nontail> values,
        [Recur(Field = "F")] L15.Effect body) => new L15.Effect.Let(node.XList, values, body);

    [Rewrite(Processor = nameof(Effect))]
    private static L15.Effect EffectApp(
        L14.Effect.App node, [Recur(Field = "T0")] L15.Triv target,
        [Recur(Field = "T1List")] Seq<L15.Triv> arguments, SchemeSymbols symbols)
    {
        var (label, app) = ReturnPoint(target, arguments, symbols);
        return new L15.Effect.ReturnPoint(label, app);
    }

    private static L15.Tail ReturnPrimitive(L15.Nontail operation, Symbol rp, SchemeSymbols symbols)
    {
        Symbol result = symbols.GenSymbol("tmp");
        return new L15.Tail.Let([result], [operation], new L15.Return(new L15.Var(rp), new L15.Var(result)));
    }

    private static (Symbol, L15.Application) ReturnPoint(
        L15.Triv target, Seq<L15.Triv> args, SchemeSymbols symbols)
    {
        Symbol label = SchemeSymbols.GenLabel(symbols.GenSymbol("lab"));
        return (label, new L15.Application.App(target, [new L15.Var(label), .. args]));
    }
}

[Pass(typeof(L15), typeof(L16))]
internal sealed partial class RemoveNonunaryLet
{
    [Processor("Tail", "Tail")]
    private partial L16.Tail Tail(L15.Tail ir);

    [Processor("Nontail", "Nontail")]
    private partial L16.Nontail Nontail(L15.Nontail ir);

    [Processor("Predicate", "Predicate")]
    private partial L16.Predicate Predicate(L15.Predicate ir);

    [Processor("Effect", "Effect")]
    private partial L16.Effect Effect(L15.Effect ir);

    [Rewrite(Processor = nameof(Tail))]
    private static L16.Tail TailLet(
        L15.Tail.Let node, [Recur(Field = "Ntl1List")] Seq<L16.Nontail> values,
        [Recur(Field = "Tl2")] L16.Tail body) =>
        Unnest(node.XList, values, body, static (name, value, rest) => new L16.Tail.Let(name, value, rest));

    [Rewrite(Processor = nameof(Nontail))]
    private static L16.Nontail NontailLet(
        L15.Nontail.Let node, [Recur(Field = "Ntl1List")] Seq<L16.Nontail> values,
        [Recur(Field = "Ntl2")] L16.Nontail body) =>
        Unnest(node.XList, values, body, static (name, value, rest) => new L16.Nontail.Let(name, value, rest));

    [Rewrite(Processor = nameof(Predicate))]
    private static L16.Predicate PredicateLet(
        L15.Predicate.Let node, [Recur(Field = "NtlList")] Seq<L16.Nontail> values,
        [Recur(Field = "P")] L16.Predicate body) =>
        Unnest(node.XList, values, body, static (name, value, rest) => new L16.Predicate.Let(name, value, rest));

    [Rewrite(Processor = nameof(Effect))]
    private static L16.Effect EffectLet(
        L15.Effect.Let node, [Recur(Field = "NtlList")] Seq<L16.Nontail> values,
        [Recur(Field = "F")] L16.Effect body) =>
        Unnest(node.XList, values, body, static (name, value, rest) => new L16.Effect.Let(name, value, rest));

    private static T Unnest<T>(Seq<Symbol> names, Seq<L16.Nontail> values, T body, Func<Symbol, L16.Nontail, T, T> let)
    {
        for (int i = names.Count - 1; i >= 0; i--)
        {
            body = let(names[i], values[i], body);
        }

        return body;
    }
}

[Pass(typeof(L16), typeof(L17))]
internal sealed partial class ReturnOfSetBang
{
    [Processor("RunExpr", "RunExpr")]
    private partial L17.RunExpr RunExpr(L16.RunExpr ir);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L17.LambdaExpr LambdaExpr(L16.LambdaExpr ir);

    [Processor("Tail", "Tail")]
    private partial L17.Tail Tail(L16.Tail ir, List<Symbol> locals);

    [Processor("Nontail", "RhsExpr")]
    private partial L17.RhsExpr Rhs(L16.Nontail ir, List<Symbol> locals);

    [Processor("Predicate", "Predicate")]
    private partial L17.Predicate Predicate(L16.Predicate ir, List<Symbol> locals);

    [Processor("Effect", "Effect")]
    private partial L17.Effect Effect(L16.Effect ir, List<Symbol> locals);

    [Rewrite(Processor = nameof(RunExpr))]
    private L17.RunExpr RunBody(L16.Run node) => new L17.Run(node.X, this.Declare(node.Tl));

    [Rewrite(Processor = nameof(LambdaExpr))]
    private L17.LambdaExpr LambdaBody(L16.Lambda node) => new L17.Lambda(node.XList, this.Declare(node.Tl));

    [Rewrite(Processor = nameof(Tail))]
    private L17.Tail TailLet(
        L16.Tail.Let node, [Recur(Field = "Tl2")] L17.Tail body, List<Symbol> locals)
    {
        var rhs = this.Rhs(node.Ntl1, locals);
        locals.Add(node.X);
        return new L17.Tail.Begin([new L17.Set(node.X, rhs)], body);
    }

    [Rewrite(Processor = nameof(Tail))]
    private L17.Tail TailBegin(
        L16.Tail.Begin node, [Recur(Field = "Tl1")] L17.Tail tail, List<Symbol> locals) =>
        new L17.Tail.Begin(node.F0List.Select(item => this.Effect(item, locals)), tail);

    [Rewrite(Processor = nameof(Rhs))]
    private L17.RhsExpr RhsLet(
        L16.Nontail.Let node, [Recur(Field = "Ntl2")] L17.RhsExpr body, List<Symbol> locals)
    {
        var rhs = this.Rhs(node.Ntl1, locals);
        locals.Add(node.X);
        return new L17.Begin([new L17.Set(node.X, rhs)], body);
    }

    [Rewrite(Processor = nameof(Rhs))]
    private static L17.RhsExpr RhsIf(
        L16.Nontail.If node, [Recur(Field = "P1")] L17.Predicate test,
        [Recur(Field = "Ntl2")] L17.RhsExpr yes, [Recur(Field = "Ntl3")] L17.RhsExpr no) =>
        new L17.If(test, yes, no);

    [Rewrite(Processor = nameof(Rhs))]
    private L17.RhsExpr RhsBegin(
        L16.Nontail.Begin node, [Recur(Field = "Ntl1")] L17.RhsExpr end, List<Symbol> locals) =>
        new L17.Begin(node.F0List.Select(item => this.Effect(item, locals)), end);

    [Rewrite(Processor = nameof(Predicate))]
    private L17.Predicate PredicateLet(
        L16.Predicate.Let node, [Recur(Field = "P")] L17.Predicate body, List<Symbol> locals)
    {
        var rhs = this.Rhs(node.Ntl, locals);
        locals.Add(node.X);
        return new L17.Predicate.Begin([new L17.Set(node.X, rhs)], body);
    }

    [Rewrite(Processor = nameof(Predicate))]
    private L17.Predicate PredicateBegin(
        L16.Predicate.Begin node, [Recur(Field = "P1")] L17.Predicate end, List<Symbol> locals) =>
        new L17.Predicate.Begin(node.F0List.Select(item => this.Effect(item, locals)), end);

    [Rewrite(Processor = nameof(Effect))]
    private L17.Effect EffectLet(
        L16.Effect.Let node, [Recur(Field = "F")] L17.Effect body, List<Symbol> locals)
    {
        var rhs = this.Rhs(node.Ntl, locals);
        locals.Add(node.X);
        return new L17.Effect.Begin([new L17.Set(node.X, rhs)], body);
    }

    [Rewrite(Processor = nameof(Effect))]
    private L17.Effect EffectBegin(
        L16.Effect.Begin node, [Recur(Field = "F1")] L17.Effect end, List<Symbol> locals) =>
        new L17.Effect.Begin(node.F0List.Select(item => this.Effect(item, locals)), end);

    private L17.DeclareExpr Declare(L16.Tail body)
    {
        List<Symbol> locals = [];
        var lowered = this.Tail(body, locals);
        locals.Reverse();
        return new L17.Declare([.. locals], lowered);
    }
}

[Pass(typeof(L17), typeof(L18))]
internal sealed partial class ExplicitLabels
{
    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L18.LambdaExpr Lambda(L17.LambdaExpr ir, Seq<Symbol> labs);

    [Processor("Triv", "Triv")]
    private partial L18.Triv Triv(L17.Triv ir, Seq<Symbol> labs);

    [Processor("Application", "Application")]
    private partial L18.Application Application(L17.Application ir, Seq<Symbol> labs);

    [Processor("DeclareExpr", "DeclareExpr")]
    private partial L18.DeclareExpr Declare(L17.DeclareExpr ir, Seq<Symbol> labs);

    [Processor("RunExpr", "RunExpr")]
    private partial L18.RunExpr RunBody(L17.RunExpr ir, Seq<Symbol> labs);

    [Processor("Tail", "Tail")]
    private partial L18.Tail Tail(L17.Tail ir, Seq<Symbol> labs);

    [Processor("RhsExpr", "RhsExpr")]
    private partial L18.RhsExpr Rhs(L17.RhsExpr ir, Seq<Symbol> labs);

    [Processor("Predicate", "Predicate")]
    private partial L18.Predicate Predicate(L17.Predicate ir, Seq<Symbol> labs);

    [Processor("Effect", "Effect")]
    private partial L18.Effect Effect(L17.Effect ir, Seq<Symbol> labs);

    [Rewrite]
    private L18.LetrecExpr Letrec(L17.Letrec node)
    {
        var labs = node.X1List;
        return new L18.Letrec(labs, node.LexprList.Select(item => this.Lambda(item, labs)),
            this.RunBody(node.Rnexpr, labs));
    }

    [Rewrite(Processor = nameof(Triv))]
    private static L18.Triv Var(L17.Var node, Seq<Symbol> labs) =>
        labs.Contains(node.X) ? new L18.Label(node.X) : (L18.Triv)new L18.Var(node.X);

    [Rewrite(Processor = nameof(Rhs))]
    private L18.RhsExpr RhsReturnPoint(L17.ReturnPoint node, Seq<Symbol> labs) =>
        new L18.ReturnPoint(node.X, this.Application(node.A, [node.X, .. labs]));

    [Rewrite(Processor = nameof(Effect))]
    private L18.Effect EffectReturnPoint(L17.Effect.ReturnPoint node, Seq<Symbol> labs) =>
        new L18.Effect.ReturnPoint(node.X, this.Application(node.A, [node.X, .. labs]));
}
