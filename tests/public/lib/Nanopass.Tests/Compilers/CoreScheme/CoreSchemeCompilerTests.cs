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
// OneDotNet is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

// Scenarios adapted from the MIT-licensed Nanopass Framework Racket compiler by Jens Axel Søgaard.
// Upstream copyright and license: src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using EvalEnv = System.Collections.Immutable.ImmutableDictionary<Hcoona.Nanopass.Symbol, object>;

namespace Hcoona.Nanopass.Tests.Compilers.CoreScheme;

/// <summary>Verifies the CoreScheme reference compiler entirely in-process.</summary>
[TestClass]
public sealed class CoreSchemeCompilerTests
{
    private const string Factorial = """
        (let (Y (λ (b)
                   ((λ (f) (b (λ (x) ((f f) x))))
                    (λ (f) (b (λ (x) ((f f) x)))))))
          (let (Fact (Y (λ (fact)
                           (λ (n)
                             (if (zero? n)
                                 1
                                 (* n (fact (- n 1))))))))
            (Fact 5)))
        """;

    /// <summary>Verifies all upstream surface syntax examples and all constant types.</summary>
    [TestMethod]
    [DataRow("3")]
    [DataRow("(if0 1 2 3)")]
    [DataRow("(+ 1 2)")]
    [DataRow("(+ 1 2 3)")]
    [DataRow("(let (x 1) x)")]
    [DataRow("((λ (x y) (+ x y) 1 2))")]
    [DataRow("((λ (x y) (+ x y)) 1 2)")]
    [DataRow("(if #f \"false\" 1.25)")]
    public void ParseAndUnparse_RoundTripsSurfaceSyntax(string input) =>
        Assert.AreEqual(input, LCS.UnparseToString(Parse(input)));

    /// <summary>Distinguishes general calls from primitive applications.</summary>
    [TestMethod]
    public void Parse_PrimitiveAndGeneralCallsUseDistinctProductions() =>
        Assert.AreEqual("(call (λ (x y) (prim + x y)) 1 2)",
            LCS.UnparseToString(Parse("((λ (x y) (+ x y)) 1 2)"), raw: true));

    /// <summary>Rejects invalid terms, bindings, and lambda formals.</summary>
    [TestMethod]
    [DataRow("()")]
    [DataRow("(+ 1 . 2)")]
    [DataRow("zero?")]
    [DataRow("(let (+ 1) 2)")]
    [DataRow("(let (42 1) 2)")]
    [DataRow("(λ (+) 1)")]
    [DataRow("(λ (x x) x)")]
    public void Parse_InvalidTerm_Throws(string input) =>
        Assert.ThrowsExactly<NanopassException>(() => Parse(input));

    /// <summary>Verifies exact upstream normalization output and the generated grammar.</summary>
    [TestMethod]
    [DataRow("1", "1")]
    [DataRow("x", "x")]
    [DataRow("(λ (x) 1)", "(λ (x) 1)")]
    [DataRow("(if 1 2 3)", "(if 1 2 3)")]
    [DataRow("(if (+ 1 2) 3 4)", "(let (t.1 (prim + 1 2)) (if t.1 3 4))")]
    [DataRow("(if 1 (+ 2 3) 4)", "(if 1 (prim + 2 3) 4)")]
    [DataRow("(if 1 2 (+ 3 4))", "(if 1 2 (prim + 3 4))")]
    [DataRow("(+ (+ 2 2) (let (x 1) (f x)))",
        "(let (t.1 (prim + 2 2)) (let (x 1) (let (t.2 (call f x)) (prim + t.1 t.2))))")]
    [DataRow("((f g) (h x) 3)", "(let (t.1 (call f g)) (let (t.2 (call h x)) (call t.1 t.2 3)))")]
    [DataRow("(λ (x) (+ (* 2 x) 3))", "(λ (x) (let (t.1 (prim * 2 x)) (prim + t.1 3)))")]
    [DataRow("(let (t.1 40) (+ (+ 1 2) t.1))",
        "(let (t.1 40) (let (t.2 (prim + 1 2)) (prim + t.2 t.1)))")]
    public void Normalize_ProducesExactAdministrativeForm(string input, string expected)
    {
        var term = new ANormalizeCoreScheme().Run(Parse(input));

        Assert.AreEqual(expected, LACS.UnparseToString(term));
        Assert.AreEqual(term, LACS.Parse(expected));
    }

    /// <summary>Checks shadowing, initializer scope, and per-run fresh names.</summary>
    [TestMethod]
    [DataRow("(let (x 1) (let (x 2) x))", "(let (x 1) (let (x.1 2) x.1))")]
    [DataRow("(let (x 3) (let (x (+ x 1)) x))", "(let (x 3) (let (x.1 (+ x 1)) x.1))")]
    [DataRow("(let (x.1 1) (let (x 2) (λ (x) (+ x x.1))))",
        "(let (x.1 1) (let (x 2) (λ (x.2) (+ x.2 x.1))))")]
    public void AlphaRename_ShadowingPreservesLexicalBinding(string input, string expected)
    {
        var pass = new AlphaRenameCoreScheme();
        var term = Parse(input);
        Assert.AreEqual(expected, LCS.UnparseToString(pass.Run(term)));
        Assert.AreEqual(expected, LCS.UnparseToString(pass.Run(term)));
    }

    /// <summary>Checks that bindings do not leak out of lexical scopes.</summary>
    [TestMethod]
    [DataRow("x")]
    [DataRow("(let (x x) x)")]
    [DataRow("(if #t (let (x 1) x) x)")]
    [DataRow("((λ (x) x) x)")]
    public void AlphaRename_UnboundIdentifier_Throws(string input)
    {
        var term = Parse(input);
        var error = Assert.ThrowsExactly<NanopassException>(
            () => new AlphaRenameCoreScheme().Run(term));
        Assert.Contains("Unbound identifier: x.", error.Message);
    }

    /// <summary>Checks both IRs against independent results, including the Y-combinator.</summary>
    [TestMethod]
    [DataRow("(((λ (x) (λ (y) (+ x y))) 4) 6)", 10.0)]
    [DataRow("(let (x 40) (+ (let (x 1) x) x))", 41.0)]
    [DataRow("(let (x 1) (let (f (λ () x)) (let (x 2) (f))))", 1.0)]
    [DataRow("(if #f (0) 7)", 7.0)]
    [DataRow("(if 0 7 (0))", 7.0)]
    [DataRow("(let (x (if #f (0) 6)) (* x (if 0 7 (0))))", 42.0)]
    [DataRow("(/ (- 21 3 6) 2 3)", 2.0)]
    [DataRow("(zero? (+ 2.5 -2.5))", true)]
    [DataRow("(if \"\" \"yes\" \"no\")", "yes")]
    [DataRow(Factorial, 120.0)]
    public void Normalize_PreservesEvaluation(string input, object expected)
    {
        var original = Parse(input);
        var normalized = new ANormalizeCoreScheme().Run(new AlphaRenameCoreScheme().Run(original));
        var result = EvalLcs(original);

        Assert.AreEqual(expected, result);
        Assert.AreEqual(result, EvalLacs(normalized));
    }

    /// <summary>Verifies the compiled factorial program without executing JavaScript.</summary>
    [TestMethod]
    public void Pipeline_FactorialEmitsRecursiveCallsAndScopedControlFlow()
    {
        string js = Compile(Factorial);

        Assert.StartsWith("// JavaScript Primitives\n", js);
        Assert.Contains("function zero_p(v) { return v === 0; }", js);
        Assert.Contains("let result;\n(()=>{let Y;", js);
        Assert.Contains("Y=function(b)", js);
        Assert.Contains("function(f_d", js);
        Assert.Contains("let Fact;", js);
        Assert.Contains("Fact=(Y)(function(fact)", js);
        Assert.Contains("=zero_p(n)", js);
        Assert.Contains("!==false)?($value=1)", js);
        Assert.Contains("=minus(n,1)", js);
        Assert.Contains("=(fact)(", js);
        Assert.Contains("$value=mult(n,", js);
        Assert.Contains("result=(Fact)(5)", js);
        Assert.EndsWith("console.log(result);\n", js);
        Assert.AreEqual(js, Compile(Factorial));
    }

    /// <summary>Verifies identifier mangling, name isolation, and string escaping.</summary>
    [TestMethod]
    public void Pipeline_ManglesIdentifiersAndEscapesLiterals()
    {
        string js = Compile("""
            (let (plus (λ (a_b-c?d.e/f) (+ a_b-c?d.e/f 1)))
              (if 0 (plus 2) "a\"b\nc"))
            """);

        Assert.Contains("let $plus;", js);
        Assert.Contains("function(a__b_uc_pd_de_wf)", js);
        Assert.Contains("$value=plus(a__b_uc_pd_de_wf,1)", js);
        Assert.Contains("(0!==false)?(result=($plus)(2))", js);
        Assert.Contains("result=\"a\\u0022b\\nc\"", js);
    }

    private static LCS.Term Parse(string input) => new ParseCoreScheme().Run(SExpr.Parse(input));

    private static string Compile(string input)
    {
        SExpr output = CoreSchemeCompiler.Pipeline.Run(SExpr.Parse(input));
        return Assert.IsInstanceOfType<SExpr.Str>(output).Value;
    }

    private static object EvalLcs(LCS.Term term) => Eval(term, EvalEnv.Empty);

    private static object EvalLacs(LACS.Term term) => Eval(term, EvalEnv.Empty);

    private static object Eval(object ir, EvalEnv env) => ir switch
    {
        LCS.Term term => Eval(term.Value!, env),
        LCS.Value value => Eval(((LCS.Value.IUnionMembers)value).Value!, env),
        LACS.Term term => Eval(term.Value!, env),
        LACS.Value value => Eval(((LACS.Value.IUnionMembers)value).Value!, env),
        LACS.Application application => Eval(application.Value!, env),
        LACS.BoundTerm bound => Eval(bound.Value!, env),
        SExpr.Int(var value) => (double)value,
        SExpr.Float(var value) => value,
        SExpr.Bool(var value) => value,
        SExpr.Str(var value) => value,
        Symbol symbol => env[symbol],
        LCS.Λ(var parameters, var body) => Closure(parameters, body, env),
        LACS.Λ(var parameters, var body) => Closure(parameters, body, env),
        LCS.Let(var name, var rhs, var body) => Eval(body, env.SetItem(name, Eval(rhs, env))),
        LACS.Let(var name, var rhs, var body) => Eval(body, env.SetItem(name, Eval(rhs, env))),
        LCS.If(var test, var yes, var no) => Eval(Eval(test, env) is false ? no : yes, env),
        LACS.If(var test, var yes, var no) => Eval(Eval(test, env) is false ? no : yes, env),
        LCS.Call(var target, var args) => Apply(Eval(target, env), args.Select(a => Eval(a, env))),
        LACS.Call(var target, var args) => Apply(Eval(target, env), args.Select(a => Eval(a, env))),
        LCS.Prim(var op, var args) => EvalPrimitive(op, args.Select(a => (double)Eval(a, env))),
        LACS.Prim(var op, var args) => EvalPrimitive(op, args.Select(a => (double)Eval(a, env))),
        _ => throw new NanopassException($"Cannot evaluate {ir}."),
    };

    private static object Apply(object target, Seq<object> args) =>
        ((Func<Seq<object>, object>)target)(args);

    private static Func<Seq<object>, object> Closure(
        Seq<Symbol> parameters, object body, EvalEnv env) =>
        args => args.Count == parameters.Count
            ? Eval(body, env.SetItems(parameters.Zip(args, KeyValuePair.Create)))
            : throw new NanopassException("Incorrect number of arguments.");

    private static object EvalPrimitive(Symbol op, Seq<double> args) => op.Name switch
    {
        "+" => args.Sum(),
        "-" => args.Aggregate(static (left, right) => left - right),
        "*" => args.Aggregate(1.0, static (left, right) => left * right),
        "/" => args.Aggregate(static (left, right) => left / right),
        "zero?" => args.Single() == 0,
        _ => throw new NanopassException($"Unknown primitive: {op}."),
    };
}
