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

[Language]
[Entry("Expr")]
[Terminal(typeof(Symbol), "x", Name = "Variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(SExpr), "c", Name = "Constant", Predicate = nameof(IsConstant))]
[Terminal(typeof(SExpr), "d", Name = "Datum", Predicate = nameof(IsDatum))]
[Terminal(typeof(Symbol), "pr", Name = "UserPrimitive", Predicate = nameof(IsUserPrimitive))]
[NonTerminalDef("Expr", ["e", "body"],
    "c",
    "x",
    "pr",
    "(quote d)",
    "(set! x e)",
    "(if e1 e2)",
    "(if e1 e2 e3)",
    "(begin e1 ... e2)",
    "(lambda (x ...) body1 ... body2)",
    "(let ((x e) ...) body1 ... body2)",
    "(letrec ((x e) ...) body1 ... body2)",
    "(e0 e1 ...)")]
internal static partial class LP
{
    private static bool IsVariable(Symbol value) => SchemeHelpers.IsVariable(value);

    private static bool IsConstant(SExpr value) => SchemeHelpers.IsConstant(value);

    private static bool IsDatum(SExpr value) => SchemeHelpers.IsDatum(value);

    private static bool IsUserPrimitive(Symbol value) => SchemeHelpers.IsUserPrimitive(value);
}

[Language]
[Extends(typeof(LP))]
[RemoveTerminal("Constant")]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["c", "x", "pr", "(quote d)", "(e0 e1 ...)"],
    Add = ["(datum d)", "(var x)", "(primapp pr e ...)", "(app e0 e1 ...)"])]
internal static partial class L0;

[Language]
[Extends(typeof(L0))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = [
        "(lambda (x ...) body1 ... body2)",
        "(let ((x e) ...) body1 ... body2)",
        "(letrec ((x e) ...) body1 ... body2)"],
    Add = ["(lambda (x ...) body)", "(let ((x e) ...) body)", "(letrec ((x e) ...) body)"])]
internal static partial class L1;

[Language]
[Extends(typeof(L1))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["(datum d)"],
    Add = ["(quoted-const d)"])]
internal static partial class L2;

[Language]
[Extends(typeof(L2))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["(if e1 e2)"])]
internal static partial class L3;

[Language]
[Extends(typeof(L3))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["(lambda (x ...) body)", "(let ((x e) ...) body)", "(letrec ((x e) ...) body)"],
    Add = ["(lambda (x ...) sbody)", "(let ((x e) ...) sbody)", "(letrec ((x e) ...) sbody)"])]
[ExtendNonTerminal("SetBody", MetaVars = ["sbody"],
    Add = ["(settable (x ...) body)"])]
internal static partial class L4;

[Language]
[Extends(typeof(L4))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Add = ["lexpr", "(letrec ((x lexpr) ...) body)"],
    Remove = ["(lambda (x ...) sbody)", "(letrec ((x e) ...) sbody)"])]
[ExtendNonTerminal("LambdaExpr", MetaVars = ["lexpr"],
    Add = ["(lambda (x ...) sbody)"])]
internal static partial class L5;

[Language]
[Extends(typeof(L5))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["(let ((x e) ...) sbody)", "(set! x e)"],
    Add = ["(let ((x e) ...) body)"])]
[ExtendNonTerminal("LambdaExpr", MetaVars = ["lexpr"],
    Remove = ["(lambda (x ...) sbody)"],
    Add = ["(lambda (x ...) body)"])]
[ExtendNonTerminal("SetBody", MetaVars = ["sbody"],
    Remove = ["(settable (x ...) body)"])]
internal static partial class L6;

[Language]
[Extends(typeof(L6))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["lexpr"])]
internal static partial class L7;

[Language]
[Extends(typeof(L7))]
[Entry("Expr")]
[ExtendNonTerminal("LambdaExpr", MetaVars = ["lexpr"],
    Remove = ["(lambda (x ...) body)"])]
[ExtendNonTerminal("FreeExp", MetaVars = ["free-body"],
    Add = ["(free (x ...) body)"])]
[ExtendNonTerminal("LambdaExpr", MetaVars = ["lexpr"],
    Add = ["(lambda (x ...) free-body)"])]
internal static partial class L8;

[Language]
[Entry("Expr")]
[Terminal(typeof(Symbol), "x", Name = "Variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(SExpr), "d", Name = "Datum", Predicate = nameof(IsDatum))]
[Terminal(typeof(Symbol), "pr", Name = "UserPrimitive", Predicate = nameof(IsUserPrimitive))]
[NonTerminalDef("Expr", ["e", "body"],
    "(var x)",
    "(quoted-const d)",
    "(if e1 e2 e3)",
    "(begin e1 ... e2)",
    "(let ((x e) ...) body)",
    "(letrec ((x lexpr) ...) c-letrec)",
    "(primapp pr e ...)",
    "(app e0 e1 ...)",
    "(anonymous-call e0 e1 ...)")]
[NonTerminalDef("LambdaExpr", ["lexpr"],
    "(lambda (x ...) bf-body)")]
[NonTerminalDef("BindFree", ["bf-body"],
    "(bind-free (x1 x2 ...) body)")]
[NonTerminalDef("Closure", ["c-exp"],
    "(closure x1 x2 ...)")]
[NonTerminalDef("ClosureLetrec", ["c-letrec"],
    "(closure-letrec ((x c-exp) ...) body)")]
internal static partial class L9
{
    private static bool IsVariable(Symbol value) => SchemeHelpers.IsVariable(value);

    private static bool IsDatum(SExpr value) => SchemeHelpers.IsDatum(value);

    private static bool IsUserPrimitive(Symbol value) => SchemeHelpers.IsUserPrimitive(value);
}

[Language]
[Extends(typeof(L9))]
[Entry("LetrecExpr")]
[ExtendNonTerminal("LetrecExpr", MetaVars = ["lrexpr"],
    Add = ["(letrec ((x lexpr) ...) e)"])]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["(letrec ((x lexpr) ...) c-letrec)"],
    Add = ["(closure-letrec ((x c-exp) ...) body)"])]
[ExtendNonTerminal("ClosureLetrec", MetaVars = ["c-letrec"],
    Remove = ["(closure-letrec ((x c-exp) ...) body)"])]
internal static partial class L10;

[Language]
[Extends(typeof(L10))]
[Entry("LetrecExpr")]
[Terminal(typeof(Symbol), "spr", Name = "SystemPrimitive", Predicate = nameof(IsSystemPrimitive))]
[ExtendNonTerminal("Expr", MetaVars = ["e", "body"],
    Remove = ["(closure-letrec ((x c-exp) ...) body)"],
    Add = ["(sys-primapp spr e ...)"])]
[ExtendNonTerminal("BindFree", MetaVars = ["bf-body"],
    Remove = ["(bind-free (x1 x2 ...) body)"])]
[ExtendNonTerminal("Closure", MetaVars = ["c-exp"],
    Remove = ["(closure x1 x2 ...)"])]
[ExtendNonTerminal("LambdaExpr", MetaVars = ["lexpr"],
    Remove = ["(lambda (x ...) bf-body)"],
    Add = ["(lambda (x ...) body)"])]
internal static partial class L11
{
    private static bool IsSystemPrimitive(Symbol value) => SchemeHelpers.IsSystemPrimitive(value);
}

[Language]
[Entry("LetrecExpr")]
[Terminal(typeof(Symbol), "x", Name = "Variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(SExpr), "d", Name = "Datum", Predicate = nameof(IsDatum))]
[Terminal(typeof(Symbol), "vp", Name = "ValuePrimitive", Predicate = nameof(IsValuePrimitive))]
[Terminal(
    typeof(Symbol), "pp", Name = "PredicatePrimitive", Predicate = nameof(IsPredicatePrimitive))]
[Terminal(typeof(Symbol), "ep", Name = "EffectPrimitive", Predicate = nameof(IsEffectPrimitive))]
[Terminal(typeof(Symbol), "spr", Name = "SystemPrimitive", Predicate = nameof(IsSystemPrimitive))]
[NonTerminalDef("LetrecExpr", ["lrexpr"],
    "(letrec ((x lexpr) ...) v)")]
[NonTerminalDef("LambdaExpr", ["lexpr"],
    "(lambda (x ...) v)")]
[NonTerminalDef("Value", ["v"],
    "(var x)",
    "(quoted-const d)",
    "(if p1 v2 v3)",
    "(begin f0 ... v1)",
    "(let ((x v1) ...) v2)",
    "(primapp vp v ...)",
    "(sys-primapp spr v ...)",
    "(anonymous-call v0 v1 ...)",
    "(app v0 v1 ...)")]
[NonTerminalDef("Predicate", ["p"],
    "(true)",
    "(false)",
    "(if p1 p2 p3)",
    "(begin f0 ... p1)",
    "(let ((x v) ...) p)",
    "(primapp pp v ...)",
    "(sys-primapp spr v ...)",
    "(anonymous-call v0 v1 ...)",
    "(app v0 v1 ...)")]
[NonTerminalDef("Effect", ["f"],
    "(nop)",
    "(if p1 f2 f3)",
    "(begin f0 ... f1)",
    "(let ((x v) ...) f)",
    "(primapp ep v ...)",
    "(sys-primapp spr v ...)",
    "(anonymous-call v0 v1 ...)",
    "(app v0 v1 ...)")]
internal static partial class L12
{
    private static bool IsVariable(Symbol value) => SchemeHelpers.IsVariable(value);

    private static bool IsDatum(SExpr value) => SchemeHelpers.IsDatum(value);

    private static bool IsValuePrimitive(Symbol value) => SchemeHelpers.IsValuePrimitive(value);

    private static bool IsPredicatePrimitive(Symbol value) =>
        SchemeHelpers.IsPredicatePrimitive(value);

    private static bool IsEffectPrimitive(Symbol value) => SchemeHelpers.IsEffectPrimitive(value);

    private static bool IsSystemPrimitive(Symbol value) => SchemeHelpers.IsSystemPrimitive(value);
}

[Language]
[Entry("LetrecExpr")]
[Terminal(typeof(Symbol), "x", Name = "Variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(SExpr), "d", Name = "Datum", Predicate = nameof(IsDatum))]
[Terminal(typeof(Symbol), "vp", Name = "ValuePrimitive", Predicate = nameof(IsValuePrimitive))]
[Terminal(
    typeof(Symbol), "pp", Name = "PredicatePrimitive", Predicate = nameof(IsPredicatePrimitive))]
[Terminal(typeof(Symbol), "ep", Name = "EffectPrimitive", Predicate = nameof(IsEffectPrimitive))]
[Terminal(typeof(Symbol), "spr", Name = "SystemPrimitive", Predicate = nameof(IsSystemPrimitive))]
[NonTerminalDef("LetrecExpr", ["lrexpr"],
    "(letrec ((x lexpr) ...) v)")]
[NonTerminalDef("LambdaExpr", ["lexpr"],
    "(lambda (x ...) v)")]
[NonTerminalDef("Triv", ["t"],
    "(var x)",
    "(quoted-const d)")]
[NonTerminalDef("Value", ["v"],
    "t",
    "(if p1 v2 v3)",
    "(begin f0 ... v1)",
    "(let ((x v1) ...) v2)",
    "(primapp vp t ...)",
    "(sys-primapp spr t ...)",
    "(anonymous-call t0 t1 ...)",
    "(app t0 t1 ...)")]
[NonTerminalDef("Predicate", ["p"],
    "(true)",
    "(false)",
    "(if p1 p2 p3)",
    "(begin f0 ... p1)",
    "(let ((x v) ...) p)",
    "(primapp pp t ...)",
    "(sys-primapp spr t ...)",
    "(anonymous-call t0 t1 ...)",
    "(app t0 t1 ...)")]
[NonTerminalDef("Effect", ["f"],
    "(nop)",
    "(if p1 f2 f3)",
    "(begin f0 ... f1)",
    "(let ((x v) ...) f)",
    "(primapp ep t ...)",
    "(sys-primapp spr t ...)",
    "(anonymous-call t0 t1 ...)",
    "(app t0 t1 ...)")]
internal static partial class L13
{
    private static bool IsVariable(Symbol value) => SchemeHelpers.IsVariable(value);

    private static bool IsDatum(SExpr value) => SchemeHelpers.IsDatum(value);

    private static bool IsValuePrimitive(Symbol value) => SchemeHelpers.IsValuePrimitive(value);

    private static bool IsPredicatePrimitive(Symbol value) =>
        SchemeHelpers.IsPredicatePrimitive(value);

    private static bool IsEffectPrimitive(Symbol value) => SchemeHelpers.IsEffectPrimitive(value);

    private static bool IsSystemPrimitive(Symbol value) => SchemeHelpers.IsSystemPrimitive(value);
}

[Language]
[Extends(typeof(L13))]
[Entry("LetrecExpr")]
[ExtendNonTerminal("Value", MetaVars = ["v"],
    Remove = ["(anonymous-call t0 t1 ...)"])]
[ExtendNonTerminal("Predicate", MetaVars = ["p"],
    Remove = ["(anonymous-call t0 t1 ...)"])]
[ExtendNonTerminal("Effect", MetaVars = ["f"],
    Remove = ["(anonymous-call t0 t1 ...)"])]
internal static partial class L14;

[Language]
[Entry("LetrecExpr")]
[Terminal(typeof(Symbol), "x", Name = "Variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(SExpr), "d", Name = "Datum", Predicate = nameof(IsDatum))]
[Terminal(typeof(Symbol), "vp", Name = "ValuePrimitive", Predicate = nameof(IsValuePrimitive))]
[Terminal(
    typeof(Symbol), "pp", Name = "PredicatePrimitive", Predicate = nameof(IsPredicatePrimitive))]
[Terminal(typeof(Symbol), "ep", Name = "EffectPrimitive", Predicate = nameof(IsEffectPrimitive))]
[Terminal(typeof(Symbol), "spr", Name = "SystemPrimitive", Predicate = nameof(IsSystemPrimitive))]
[NonTerminalDef("LetrecExpr", ["lrexpr"],
    "(letrec ((x1 lexpr) ...) rnexpr)")]
[NonTerminalDef("RunExpr", ["rnexpr"],
    "(run (x) tl)")]
[NonTerminalDef("LambdaExpr", ["lexpr"],
    "(lambda (x ...) tl)")]
[NonTerminalDef("Triv", ["t"],
    "(var x)",
    "(quoted-const d)")]
[NonTerminalDef("Application", ["a"],
    "(app t0 t1 ...)")]
[NonTerminalDef("Tail", ["tl"],
    "(return t1 t2)",
    "(if p1 tl2 tl3)",
    "(begin f0 ... tl1)",
    "(let ((x ntl1) ...) tl2)",
    "(app t0 t1 ...)")]
[NonTerminalDef("Nontail", ["ntl"],
    "t",
    "(if p1 ntl2 ntl3)",
    "(begin f0 ... ntl1)",
    "(let ((x ntl1) ...) ntl2)",
    "(primapp vp t ...)",
    "(sys-primapp spr t ...)",
    "(return-point x a)")]
[NonTerminalDef("Predicate", ["p"],
    "(true)",
    "(false)",
    "(if p1 p2 p3)",
    "(begin f0 ... p1)",
    "(let ((x ntl) ...) p)",
    "(primapp pp t ...)",
    "(sys-primapp spr t ...)")]
[NonTerminalDef("Effect", ["f"],
    "(nop)",
    "(if p1 f2 f3)",
    "(begin f0 ... f1)",
    "(let ((x ntl) ...) f)",
    "(primapp ep t ...)",
    "(sys-primapp spr t ...)",
    "(return-point x a)")]
internal static partial class L15
{
    private static bool IsVariable(Symbol value) => SchemeHelpers.IsVariable(value);

    private static bool IsDatum(SExpr value) => SchemeHelpers.IsDatum(value);

    private static bool IsValuePrimitive(Symbol value) => SchemeHelpers.IsValuePrimitive(value);

    private static bool IsPredicatePrimitive(Symbol value) =>
        SchemeHelpers.IsPredicatePrimitive(value);

    private static bool IsEffectPrimitive(Symbol value) => SchemeHelpers.IsEffectPrimitive(value);

    private static bool IsSystemPrimitive(Symbol value) => SchemeHelpers.IsSystemPrimitive(value);
}

[Language]
[Extends(typeof(L15))]
[Entry("LetrecExpr")]
[ExtendNonTerminal("Tail", MetaVars = ["tl"],
    Remove = ["(let ((x ntl1) ...) tl2)"],
    Add = ["(let ((x ntl1)) tl2)"])]
[ExtendNonTerminal("Nontail", MetaVars = ["ntl"],
    Remove = ["(let ((x ntl1) ...) ntl2)"],
    Add = ["(let ((x ntl1)) ntl2)"])]
[ExtendNonTerminal("Predicate", MetaVars = ["p"],
    Remove = ["(let ((x ntl) ...) p)"],
    Add = ["(let ((x ntl)) p)"])]
[ExtendNonTerminal("Effect", MetaVars = ["f"],
    Remove = ["(let ((x ntl) ...) f)"],
    Add = ["(let ((x ntl)) f)"])]
internal static partial class L16;

[Language]
[Extends(typeof(L16))]
[Entry("LetrecExpr")]
[ExtendNonTerminal("RunExpr", MetaVars = ["rnexpr"],
    Remove = ["(run (x) tl)"],
    Add = ["(run (x) dec)"])]
[ExtendNonTerminal("LambdaExpr", MetaVars = ["lexpr"],
    Remove = ["(lambda (x ...) tl)"],
    Add = ["(lambda (x ...) dec)"])]
[ExtendNonTerminal("DeclareExpr", MetaVars = ["dec"],
    Add = ["(declare (x ...) tl)"])]
[ExtendNonTerminal("Tail", MetaVars = ["tl"],
    Remove = ["(let ((x ntl1)) tl2)"])]
[ExtendNonTerminal("Nontail", MetaVars = ["ntl"],
    Remove = [
        "t", "(if p1 ntl2 ntl3)", "(begin f0 ... ntl1)", "(let ((x ntl1)) ntl2)",
        "(primapp vp t ...)", "(sys-primapp spr t ...)", "(return-point x a)"])]
[ExtendNonTerminal("RhsExpr", MetaVars = ["rhs"],
    Add = [
        "t", "(if p1 rhs2 rhs3)", "(begin f0 ... rhs1)",
        "(primapp vp t ...)", "(sys-primapp spr t ...)", "(return-point x a)"])]
[ExtendNonTerminal("Predicate", MetaVars = ["p"],
    Remove = ["(let ((x ntl)) p)"])]
[ExtendNonTerminal("Effect", MetaVars = ["f"],
    Remove = ["(let ((x ntl)) f)"],
    Add = ["(set! x rhs)"])]
internal static partial class L17;

[Language]
[Extends(typeof(L17))]
[Entry("LetrecExpr")]
[ExtendNonTerminal("Triv", MetaVars = ["t"],
    Add = ["(label x)"])]
internal static partial class L18;
