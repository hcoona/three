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

// Adapted from the MIT-licensed Nanopass Compiler Framework (Copyright (c) 2000-2015
// Dipanwita Sarkar, Andrew W. Keep, R. Kent Dybvig, Oscar Waddell).
// See src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

[Pass(typeof(L11), typeof(L12))]
internal sealed partial class NormalizeContext
{
    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L12.LambdaExpr LambdaExpr(L11.LambdaExpr ir);

    [Processor("Expr", "Value")]
    private partial L12.Value Value(L11.Expr ir);

    [Processor("Expr", "Predicate")]
    private partial L12.Predicate Predicate(L11.Expr ir);

    [Processor("Expr", "Effect")]
    private partial L12.Effect Effect(L11.Expr ir);

    [Rewrite]
    private static L12.LetrecExpr Letrec(
        L11.Letrec node, [Recur(Field = "LexprList")] Seq<L12.LambdaExpr> lambdas,
        [Recur(Field = "E", Processor = nameof(Value))] L12.Value body) =>
        new L12.Letrec(node.XList, lambdas, body);

    [Rewrite(Processor = nameof(LambdaExpr))]
    private static L12.LambdaExpr Lambda(
        L11.Lambda node, [Recur(Field = "Body", Processor = nameof(Value))] L12.Value body) =>
        new L12.Lambda(node.XList, body);

    [Rewrite(Processor = nameof(Value))]
    private static L12.Value ValueIf(
        L11.If node, [Recur(Field = "E1", Processor = nameof(Predicate))] L12.Predicate test,
        [Recur(Field = "E2")] L12.Value yes, [Recur(Field = "E3")] L12.Value no) =>
        new L12.Value.If(test, yes, no);

    [Rewrite(Processor = nameof(Value))]
    private static L12.Value ValueBegin(
        L11.Begin node,
        [Recur(Field = "E1List", Processor = nameof(Effect))] Seq<L12.Effect> effects,
        [Recur(Field = "E2")] L12.Value end) => new L12.Value.Begin(effects, end);

    [Rewrite(Processor = nameof(Value))]
    private L12.Value ValuePrimapp(L11.Primapp node) =>
        this.PrimitiveInValue(node.Pr, node.EList, system: false);

    [Rewrite(Processor = nameof(Value))]
    private L12.Value ValueSysPrimapp(L11.SysPrimapp node) =>
        this.PrimitiveInValue(node.Spr, node.EList, system: true);

    [Rewrite(Processor = nameof(Predicate))]
    private static L12.Predicate PredicateVar(L11.Var node) => TruthTest(new L12.Var(node.X));

    [Rewrite(Processor = nameof(Predicate))]
    private static L12.Predicate PredicateQuote(L11.QuotedConst node) =>
        node.D is SExpr.Bool(false) ? new L12.False() : (L12.Predicate)new L12.True();

    [Rewrite(Processor = nameof(Predicate))]
    private static L12.Predicate PredicateIf(
        L11.If node, [Recur(Field = "E1")] L12.Predicate test,
        [Recur(Field = "E2")] L12.Predicate yes, [Recur(Field = "E3")] L12.Predicate no) =>
        new L12.Predicate.If(test, yes, no);

    [Rewrite(Processor = nameof(Predicate))]
    private static L12.Predicate PredicateBegin(
        L11.Begin node,
        [Recur(Field = "E1List", Processor = nameof(Effect))] Seq<L12.Effect> effects,
        [Recur(Field = "E2")] L12.Predicate end) => new L12.Predicate.Begin(effects, end);

    [Rewrite(Processor = nameof(Predicate))]
    private static L12.Predicate PredicateLet(
        L11.Let node, [Recur(Field = "EList", Processor = nameof(Value))] Seq<L12.Value> values,
        [Recur(Field = "Body")] L12.Predicate body) =>
        new L12.Predicate.Let(node.XList, values, body);

    [Rewrite(Processor = nameof(Predicate))]
    private L12.Predicate PredicatePrimapp(L11.Primapp node) =>
        this.PrimitiveInPredicate(node.Pr, node.EList, system: false);

    [Rewrite(Processor = nameof(Predicate))]
    private L12.Predicate PredicateSysPrimapp(L11.SysPrimapp node) =>
        this.PrimitiveInPredicate(node.Spr, node.EList, system: true);

    [Rewrite(Processor = nameof(Predicate))]
    private static L12.Predicate PredicateCall(
        L11.App node, [Recur(Field = "E0", Processor = nameof(Value))] L12.Value target,
        [Recur(Field = "E1List", Processor = nameof(Value))] Seq<L12.Value> values) =>
        TruthTest(new L12.Value.App(target, values));

    [Rewrite(Processor = nameof(Predicate))]
    private static L12.Predicate PredicateAnonymousCall(
        L11.AnonymousCall node, [Recur(Field = "E0", Processor = nameof(Value))] L12.Value target,
        [Recur(Field = "E1List", Processor = nameof(Value))] Seq<L12.Value> values) =>
        TruthTest(new L12.Value.AnonymousCall(target, values));

    [Rewrite(Processor = nameof(Effect))]
    private static L12.Effect EffectVar(L11.Var node) => new L12.Nop();

    [Rewrite(Processor = nameof(Effect))]
    private static L12.Effect EffectQuote(L11.QuotedConst node) => new L12.Nop();

    [Rewrite(Processor = nameof(Effect))]
    private static L12.Effect EffectIf(
        L11.If node, [Recur(Field = "E1", Processor = nameof(Predicate))] L12.Predicate test,
        [Recur(Field = "E2")] L12.Effect yes, [Recur(Field = "E3")] L12.Effect no) =>
        new L12.Effect.If(test, yes, no);

    [Rewrite(Processor = nameof(Effect))]
    private static L12.Effect EffectBegin(
        L11.Begin node, [Recur(Field = "E1List")] Seq<L12.Effect> effects,
        [Recur(Field = "E2")] L12.Effect end) => new L12.Effect.Begin(effects, end);

    [Rewrite(Processor = nameof(Effect))]
    private static L12.Effect EffectLet(
        L11.Let node, [Recur(Field = "EList", Processor = nameof(Value))] Seq<L12.Value> values,
        [Recur(Field = "Body")] L12.Effect body) => new L12.Effect.Let(node.XList, values, body);

    [Rewrite(Processor = nameof(Effect))]
    private L12.Effect EffectPrimapp(L11.Primapp node) =>
        this.PrimitiveInEffect(node.Pr, node.EList, system: false);

    [Rewrite(Processor = nameof(Effect))]
    private L12.Effect EffectSysPrimapp(L11.SysPrimapp node) =>
        this.PrimitiveInEffect(node.Spr, node.EList, system: true);

    [Rewrite(Processor = nameof(Effect))]
    private static L12.Effect EffectCall(
        L11.App node, [Recur(Field = "E0", Processor = nameof(Value))] L12.Value target,
        [Recur(Field = "E1List", Processor = nameof(Value))] Seq<L12.Value> values) =>
        new L12.Effect.App(target, values);

    [Rewrite(Processor = nameof(Effect))]
    private static L12.Effect EffectAnonymousCall(
        L11.AnonymousCall node, [Recur(Field = "E0", Processor = nameof(Value))] L12.Value target,
        [Recur(Field = "E1List", Processor = nameof(Value))] Seq<L12.Value> values) =>
        new L12.Effect.AnonymousCall(target, values);

    private static L12.Predicate TruthTest(L12.Value value) =>
        new L12.Predicate.If(new L12.Predicate.Primapp("eq?", [value, Boolean(false)]),
            new L12.False(), new L12.True());

    private static L12.Value Boolean(bool value) => new L12.QuotedConst(new SExpr.Bool(value));

    private static L12.Value PrimitiveValue(Symbol name, Seq<L12.Value> values, bool system) =>
        system
            ? new L12.Value.SysPrimapp(name, values)
            : (L12.Value)new L12.Value.Primapp(name, values);

    private static L12.Predicate PrimitivePredicate(
        Symbol name, Seq<L12.Value> values, bool system) =>
        system
            ? new L12.Predicate.SysPrimapp(name, values)
            : (L12.Predicate)new L12.Predicate.Primapp(name, values);

    private static L12.Effect PrimitiveEffect(Symbol name, Seq<L12.Value> values, bool system) =>
        system
            ? new L12.Effect.SysPrimapp(name, values)
            : (L12.Effect)new L12.Effect.Primapp(name, values);

    private L12.Value PrimitiveInValue(Symbol name, Seq<L11.Expr> operands, bool system)
    {
        if (name.Name == "not")
        {
            return new L12.Value.If(this.Predicate(operands[0]), Boolean(false), Boolean(true));
        }

        var values = operands.Select(this.Value);
        if (SchemeHelpers.IsPredicatePrimitive(name))
        {
            return new L12.Value.If(
                PrimitivePredicate(name, values, system), Boolean(true), Boolean(false));
        }

        return SchemeHelpers.IsEffectPrimitive(name)
            ? new L12.Value.Begin(
                [PrimitiveEffect(name, values, system)], new L12.Value.Primapp("void", []))
            : PrimitiveValue(name, values, system);
    }

    private L12.Predicate PrimitiveInPredicate(Symbol name, Seq<L11.Expr> operands, bool system)
    {
        if (name.Name == "not")
        {
            return new L12.Predicate.If(
                this.Predicate(operands[0]), new L12.False(), new L12.True());
        }

        var values = operands.Select(this.Value);
        if (SchemeHelpers.IsPredicatePrimitive(name))
        {
            return PrimitivePredicate(name, values, system);
        }

        return SchemeHelpers.IsEffectPrimitive(name)
            ? new L12.Predicate.Begin([PrimitiveEffect(name, values, system)], new L12.True())
            : TruthTest(PrimitiveValue(name, values, system));
    }

    private L12.Effect PrimitiveInEffect(Symbol name, Seq<L11.Expr> operands, bool system)
    {
        if (name.Name == "not")
        {
            return this.Effect(operands[0]);
        }

        if (SchemeHelpers.IsEffectPrimitive(name))
        {
            return PrimitiveEffect(name, operands.Select(this.Value), system);
        }

        return operands.IsEmpty ? new L12.Nop() : (L12.Effect)new L12.Effect.Begin(
            operands.Select(this.Effect), new L12.Nop());
    }
}

[Pass(typeof(L12), typeof(L13))]
internal sealed partial class RemoveComplexOpera
{
    public L13.LetrecExpr Run(L12.LetrecExpr input) => this.Run(input, new());

    internal L13.LetrecExpr Run(L12.LetrecExpr input, SchemeSymbols symbols) =>
        this.Lower(input, symbols);

    [Processor("LetrecExpr", "LetrecExpr")]
    private partial L13.LetrecExpr Lower(L12.LetrecExpr ir, SchemeSymbols symbols);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L13.LambdaExpr LambdaExpr(L12.LambdaExpr ir, SchemeSymbols symbols);

    [Processor("Value", "Value")]
    private partial L13.Value Value(L12.Value ir, SchemeSymbols symbols);

    [Processor("Predicate", "Predicate")]
    private partial L13.Predicate Predicate(L12.Predicate ir, SchemeSymbols symbols);

    [Processor("Effect", "Effect")]
    private partial L13.Effect Effect(L12.Effect ir, SchemeSymbols symbols);

    [Processor("Value", "Triv")]
    private partial (L13.Triv Triv, Binding? Binding) Operand(L12.Value ir, SchemeSymbols symbols);

    [Rewrite(Processor = nameof(Operand))]
    private static (L13.Triv, Binding?) OperandVar(L12.Var node) => (new L13.Var(node.X), null);

    [Rewrite(Processor = nameof(Operand))]
    private static (L13.Triv, Binding?) OperandQuote(L12.QuotedConst node) =>
        (new L13.QuotedConst(node.D), null);

    [Rewrite(Processor = nameof(Operand), IsElse = true)]
    private (L13.Triv, Binding?) ComplexOperand(L12.Value ir, SchemeSymbols symbols)
    {
        var value = this.Value(ir, symbols);
        Symbol name = symbols.GenSymbol("tmp");
        return (new L13.Var(name), new Binding(name, value));
    }

    [Rewrite(Processor = nameof(Value))]
    private static L13.Value ValueVar(L12.Var node) => (L13.Triv)new L13.Var(node.X);

    [Rewrite(Processor = nameof(Value))]
    private static L13.Value ValueQuote(L12.QuotedConst node) =>
        (L13.Triv)new L13.QuotedConst(node.D);

    [Rewrite(Processor = nameof(Value))]
    private static L13.Value ValuePrim(
        L12.Value.Primapp node,
        [Recur(Field = "VList", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(operands, new L13.Value.Primapp(node.Vp, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Value))]
    private static L13.Value ValueSysPrim(
        L12.Value.SysPrimapp node,
        [Recur(Field = "VList", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(operands, new L13.Value.SysPrimapp(node.Spr, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Value))]
    private static L13.Value ValueAnonymousCall(
        L12.Value.AnonymousCall node,
        [Recur(Field = "V0", Processor = nameof(Operand))] (L13.Triv Triv, Binding? Binding) target,
        [Recur(Field = "V1List", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(
            [target, .. operands],
            new L13.Value.AnonymousCall(target.Triv, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Value))]
    private static L13.Value ValueCall(
        L12.Value.App node,
        [Recur(Field = "V0", Processor = nameof(Operand))] (L13.Triv Triv, Binding? Binding) target,
        [Recur(Field = "V1List", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind([target, .. operands], new L13.Value.App(target.Triv, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Predicate))]
    private static L13.Predicate PredicatePrim(
        L12.Predicate.Primapp node,
        [Recur(Field = "VList", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(operands, new L13.Predicate.Primapp(node.Pp, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Predicate))]
    private static L13.Predicate PredicateSysPrim(
        L12.Predicate.SysPrimapp node,
        [Recur(Field = "VList", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(operands, new L13.Predicate.SysPrimapp(node.Spr, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Predicate))]
    private static L13.Predicate PredicateAnonymousCall(
        L12.Predicate.AnonymousCall node,
        [Recur(Field = "V0", Processor = nameof(Operand))] (L13.Triv Triv, Binding? Binding) target,
        [Recur(Field = "V1List", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(
            [target, .. operands],
            new L13.Predicate.AnonymousCall(target.Triv, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Predicate))]
    private static L13.Predicate PredicateCall(
        L12.Predicate.App node,
        [Recur(Field = "V0", Processor = nameof(Operand))] (L13.Triv Triv, Binding? Binding) target,
        [Recur(Field = "V1List", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind([target, .. operands], new L13.Predicate.App(target.Triv, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Effect))]
    private static L13.Effect EffectPrim(
        L12.Effect.Primapp node,
        [Recur(Field = "VList", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(operands, new L13.Effect.Primapp(node.Ep, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Effect))]
    private static L13.Effect EffectSysPrim(
        L12.Effect.SysPrimapp node,
        [Recur(Field = "VList", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(operands, new L13.Effect.SysPrimapp(node.Spr, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Effect))]
    private static L13.Effect EffectAnonymousCall(
        L12.Effect.AnonymousCall node,
        [Recur(Field = "V0", Processor = nameof(Operand))] (L13.Triv Triv, Binding? Binding) target,
        [Recur(Field = "V1List", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind(
            [target, .. operands],
            new L13.Effect.AnonymousCall(target.Triv, TrivialOperands(operands)));

    [Rewrite(Processor = nameof(Effect))]
    private static L13.Effect EffectCall(
        L12.Effect.App node,
        [Recur(Field = "V0", Processor = nameof(Operand))] (L13.Triv Triv, Binding? Binding) target,
        [Recur(Field = "V1List", Processor = nameof(Operand))]
        Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        Bind([target, .. operands], new L13.Effect.App(target.Triv, TrivialOperands(operands)));

    private static Seq<L13.Triv> TrivialOperands(Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        operands.Select(item => item.Triv);

    private static Seq<Binding> Bindings(Seq<(L13.Triv Triv, Binding? Binding)> operands) =>
        [.. operands.Select(item => item.Binding).OfType<Binding>()];

    private static L13.Value Bind(Seq<(L13.Triv Triv, Binding? Binding)> operands, L13.Value body)
    {
        var bindings = Bindings(operands);
        return bindings.IsEmpty ? body :
            new L13.Value.Let(
                bindings.Select(item => item.Name), bindings.Select(item => item.Value), body);
    }

    private static L13.Predicate Bind(
        Seq<(L13.Triv Triv, Binding? Binding)> operands, L13.Predicate body)
    {
        var bindings = Bindings(operands);
        return bindings.IsEmpty ? body :
            new L13.Predicate.Let(
                bindings.Select(item => item.Name), bindings.Select(item => item.Value), body);
    }

    private static L13.Effect Bind(Seq<(L13.Triv Triv, Binding? Binding)> operands, L13.Effect body)
    {
        var bindings = Bindings(operands);
        return bindings.IsEmpty ? body :
            new L13.Effect.Let(
                bindings.Select(item => item.Name), bindings.Select(item => item.Value), body);
    }

    private sealed record Binding(Symbol Name, L13.Value Value);
}

[Pass(typeof(L13), typeof(L14))]
internal sealed partial class RemoveAnonymousCall
{
    public L14.LetrecExpr Run(L13.LetrecExpr input) => this.Run(input, new());

    internal L14.LetrecExpr Run(L13.LetrecExpr input, SchemeSymbols symbols) =>
        this.Lower(input, symbols);

    [Processor("LetrecExpr", "LetrecExpr")]
    private partial L14.LetrecExpr Lower(L13.LetrecExpr ir, SchemeSymbols symbols);

    [Processor("LambdaExpr", "LambdaExpr")]
    private partial L14.LambdaExpr LambdaExpr(L13.LambdaExpr ir, SchemeSymbols symbols);

    [Processor("Value", "Value")]
    private partial L14.Value Value(L13.Value ir, SchemeSymbols symbols);

    [Processor("Predicate", "Predicate")]
    private partial L14.Predicate Predicate(L13.Predicate ir, SchemeSymbols symbols);

    [Processor("Effect", "Effect")]
    private partial L14.Effect Effect(L13.Effect ir, SchemeSymbols symbols);

    [Rewrite(Processor = nameof(Value))]
    private static L14.Value ValueCall(
        L13.Value.AnonymousCall node, [Recur(Field = "T0")] L14.Triv target,
        [Recur(Field = "T1List")] Seq<L14.Triv> args, SchemeSymbols symbols)
    {
        Symbol label = symbols.GenSymbol("tmp");
        return new L14.Value.Let(
            [label], [Code(target)], new L14.Value.App(new L14.Var(label), [target, .. args]));
    }

    [Rewrite(Processor = nameof(Predicate))]
    private static L14.Predicate PredicateCall(
        L13.Predicate.AnonymousCall node, [Recur(Field = "T0")] L14.Triv target,
        [Recur(Field = "T1List")] Seq<L14.Triv> args, SchemeSymbols symbols)
    {
        Symbol label = symbols.GenSymbol("tmp");
        return new L14.Predicate.Let(
            [label], [Code(target)], new L14.Predicate.App(new L14.Var(label), [target, .. args]));
    }

    [Rewrite(Processor = nameof(Effect))]
    private static L14.Effect EffectCall(
        L13.Effect.AnonymousCall node, [Recur(Field = "T0")] L14.Triv target,
        [Recur(Field = "T1List")] Seq<L14.Triv> args, SchemeSymbols symbols)
    {
        Symbol label = symbols.GenSymbol("tmp");
        return new L14.Effect.Let(
            [label], [Code(target)], new L14.Effect.App(new L14.Var(label), [target, .. args]));
    }

    private static L14.Value Code(L14.Triv target) =>
        new L14.Value.SysPrimapp("procedure-code", [target]);
}
