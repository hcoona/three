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

using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

/// <summary>
/// Compares independent source and target execution across the canonical Scheme corpus.
/// </summary>
[TestClass]
public sealed class SchemeCompilerTests
{
    /// <summary>Gets the independently numbered upstream main test programs.</summary>
    public static IEnumerable<object[]> MainPrograms => Programs(SchemeAllTestsData.Main);

    /// <summary>Gets the independently numbered upstream final test programs.</summary>
    public static IEnumerable<object[]> FinalPrograms => Programs(SchemeAllTestsData.Final);

    /// <summary>
    /// Checks that neither upstream test list was truncated during transcription.
    /// </summary>
    [TestMethod]
    public void Corpus_ContainsEveryUpstreamProgram()
    {
        Assert.HasCount(164, SchemeAllTestsData.Main);
        Assert.HasCount(30, SchemeAllTestsData.Final);
    }

    /// <summary>Runs each main test through the source and compiled evaluators.</summary>
    [TestMethod]
    [DynamicData(nameof(MainPrograms))]
    public void MainProgram_MatchesSourceEvaluation(int index, string source) =>
        Compare(index, source);

    /// <summary>Runs each final test through the source and compiled evaluators.</summary>
    [TestMethod]
    [DynamicData(nameof(FinalPrograms))]
    public void FinalProgram_MatchesSourceEvaluation(int index, string source) =>
        Compare(index, source);

    /// <summary>
    /// Checks structural round-trips and membership across the entire language series.
    /// </summary>
    [TestMethod]
    [DataRow("""
        (let ((cell (cons 0 1)) (v (vector #f #\a "text")))
          (letrec ((update (lambda (n)
                             (if (zero? n)
                                 (begin (set-cdr! cell (car cell)) (vector-set! v 1 cell))
                                 (begin (set! v v) (set-car! cell n) (update (sub1 n)))))))
            (update 3)
            (vector-ref v 1)))
        """)]
    [DataRow("""
        (let ((build (lambda (x) (lambda (y) (set! x (+ x y)) x))))
          (let ((counter (build 4)))
            (if (begin (counter 1) (not (zero? (counter 0))))
                (cons (counter 2) (counter 3))
                (vector))))
        """)]
    public void Compile_RoundTripsEveryIntermediateLanguage(string source)
    {
        SExpr input = SExpr.Parse(source);
        var parsed = LP.Parse(input);
        Assert.IsTrue(LP.IsLanguageTerm(parsed));
        Assert.AreEqual(parsed, LP.Parse(LP.Unparse(parsed)));

        List<string> stages = [];
        var compiled = SchemeCompiler.Compile(input, (language, isTerm, ir, roundTrip) =>
        {
            Assert.IsTrue(isTerm, $"{language} must accept its pass output.");
            Assert.AreEqual(ir, roundTrip, $"{language} must round-trip its pass output.");
            stages.Add(language);
        });
        Assert.AreSequenceEqual(
            Enumerable.Range(0, 19)
                .SelectMany(stage => stage == 6 ? new[] { stage, stage } : [stage])
                .Select(stage => "L" + stage.ToString(CultureInfo.InvariantCulture))
                .ToArray(),
            stages);
        Assert.IsTrue(SchemeValues.Equal(
            SchemeSourceEvaluator.EvalSource(input), SchemeL18Evaluator.EvalL18(compiled)));
    }

    /// <summary>
    /// Checks both lexical scope in the checked LP entry point and the resulting program.
    /// </summary>
    [TestMethod]
    public void VerifyScheme_CompilesParsedProgram()
    {
        const string source = "(let ((x 20)) (letrec ((f (lambda (y) (+ x y)))) (f 22)))";
        var input = LP.Parse(source);
        var verified = new VerifyScheme().Run(input);
        Assert.AreEqual(
            "(let ((x (datum 20))) (letrec ((f (lambda (y) (primapp + (var x) (var y))))) "
                + "(app (var f) (datum 22))))",
            L0.UnparseToString(verified));
        Assert.AreEqual(42L, SchemeL18Evaluator.EvalL18(SchemeCompiler.CompileVerified(input)));
    }

    /// <summary>Compiles compound quotation through the typed LP entry point.</summary>
    [TestMethod]
    [DataRow("'()", "()")]
    [DataRow("'(1 . 2)", "(1 . 2)")]
    [DataRow("'#(1 (2 3))", "#(1 (2 3))")]
    public void VerifyScheme_CompilesQuotedDatums(string source, string datum)
    {
        var parsed = LP.Parse(source);
        Assert.IsInstanceOfType<LP.Quote>(parsed.Value);
        Assert.AreEqual($"(datum {datum})", L0.UnparseToString(new VerifyScheme().Run(parsed)));
        Assert.IsTrue(SchemeValues.Equal(
            SchemeSourceEvaluator.EvalSource(SExpr.Parse(source)),
            SchemeL18Evaluator.EvalL18(SchemeCompiler.CompileVerified(parsed))));
    }

    /// <summary>
    /// Keeps sibling references out of a closure and captures repeated uses only once.
    /// </summary>
    [TestMethod]
    [DataRow("""
        (let ((x (quoted-const 1)))
          (if (var x) (var x) (letrec ((f (lambda (y) (var y)))) (var f))))
        """, "(free () (var y))")]
    [DataRow("""
        (letrec ((f (lambda (y) (primapp + (var x) (var x))))) (var f))
        """, "(free (x) (primapp + (var x) (var x)))")]
    public void UncoverFree_CapturesOnlyDistinctVariablesUsedByTheLambda(string source, string free)
    {
        var term = new UncoverFree().Run(L7.Parse(source));
        Assert.Contains(free, L8.UnparseToString(term), StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps distinct LP lexical bindings distinct after lowering to frame slots.
    /// </summary>
    [TestMethod]
    public void VerifyScheme_CompilesShadowedLocalWithCapture()
    {
        var input = LP.Parse("""
            (let ((x 20))
              (let ((capture (lambda (n) (+ x n))))
                (let ((x 3))
                  (set! x 22)
                  (capture x))))
            """);
        Assert.AreEqual(42L, SchemeL18Evaluator.EvalL18(SchemeCompiler.CompileVerified(input)));
    }

    /// <summary>Reports invalid variable references, assignments, and duplicate bindings.</summary>
    [TestMethod]
    [DataRow("missing", "unbound variable missing")]
    [DataRow("if", "keyword if")]
    [DataRow("+", "user-primitive +")]
    [DataRow("(set! missing 42)", "Assignment to unbound variable missing")]
    [DataRow("(lambda (x x) x)", "Duplicate bindings x in lambda")]
    [DataRow("(let ((x 1) (x 2)) x)", "Duplicate bindings x in let")]
    [DataRow("(letrec ((x 1) (x 2)) x)", "Duplicate bindings x in letrec")]
    public void VerifyScheme_RejectsInvalidParsedProgram(string source, string message)
    {
        var input = LP.Parse(source);
        var error = Assert.ThrowsExactly<NanopassException>(() => new VerifyScheme().Run(input));
        Assert.Contains(message, error.Message);
    }

    /// <summary>
    /// Exercises shadowed syntax, zero and variadic vector arities, and deterministic fresh names.
    /// </summary>
    [TestMethod]
    public void Compile_ShadowsKeywordsAndPrimitivesDeterministically()
    {
        const string source = """
            (let ((if (lambda (n) (vector n n)))
                  (quote (lambda (n) (vector n)))
                  (let (lambda (n) (vector n)))
                  (letrec (lambda (n) (vector n)))
                  (+ (lambda (n) n)))
              (begin (if 3) (quote 4) (let 5) (letrec 6)
                (vector-length (vector (vector) (if (+ 7))))))
            """;
        var input = SExpr.Parse(source);
        var first = SchemeCompiler.Compile(input);
        Parallel.For(0, 8, _ => Assert.AreEqual(first, SchemeCompiler.Compile(input)));
        Assert.AreEqual(2L, SchemeSourceEvaluator.EvalSource(input));
        Assert.AreEqual(2L, SchemeL18Evaluator.EvalL18(first));
    }

    /// <summary>Rejects wrong arities without restricting variadic primitives.</summary>
    [TestMethod]
    [DataRow("(+ 1)", "+")]
    [DataRow("(void 1)", "void")]
    public void RenameVarsVerifyLegal_RejectsWrongPrimitiveArity(string source, string primitive)
    {
        var error = Assert.ThrowsExactly<NanopassException>(
            () => SchemeCompiler.Compile(SExpr.Parse(source)));
        Assert.Contains($"Wrong arity for Scheme primitive {primitive}", error.Message);
    }

    /// <summary>Preserves the representation and contents of quoted non-atomic datums.</summary>
    [TestMethod]
    public void Compile_PreservesQuotedDatums()
    {
        var input = SExpr.Parse("""'#((1 . 2) #\z "text" 9223372036854775807)""");
        var expected = new SchemeVector([new SchemePair(1L, 2L), 'z', "text", long.MaxValue]);
        Assert.IsTrue(SchemeValues.Equal(expected, SchemeSourceEvaluator.EvalSource(input)));
        Assert.IsTrue(SchemeValues.Equal(
            expected, SchemeL18Evaluator.EvalL18(SchemeCompiler.Compile(input))));
    }

    /// <summary>
    /// Checks stable evaluation of cyclic values without recursive structural comparison.
    /// </summary>
    [TestMethod]
    public void Compile_ProducesEquivalentCyclicPairAndVector()
    {
        var input = SExpr.Parse("""
            (let ((p (cons 9 ())) (v (vector #f)))
              (set-cdr! p v)
              (vector-set! v 0 p)
              (cons p v))
            """);
        var actual = (SchemePair)SchemeL18Evaluator.EvalL18(SchemeCompiler.Compile(input))!;
        Assert.IsTrue(SchemeValues.Equal(SchemeSourceEvaluator.EvalSource(input), actual));
        var cycle = (SchemePair)actual.Car!;
        Assert.AreSame(actual.Cdr, cycle.Cdr);
        Assert.AreSame(cycle, ((SchemeVector)actual.Cdr!).Items[0]);
    }

    /// <summary>
    /// Checks that deep tail calls do not grow the managed stack in either evaluator.
    /// </summary>
    [TestMethod]
    public void Compile_TrampolinesDeepTailRecursion()
    {
        var input = SExpr.Parse("""
            (letrec ((loop (lambda (n acc)
                             (if (zero? n) acc (loop (sub1 n) (add1 acc))))))
              (+ 1 (loop 50000 0)))
            """);
        Assert.AreEqual(50001L, SchemeSourceEvaluator.EvalSource(input));
        Assert.AreEqual(50001L, SchemeL18Evaluator.EvalL18(SchemeCompiler.Compile(input)));
    }

    private static IEnumerable<object[]> Programs(Seq<SExpr> programs) =>
        programs.Select((program, index) => new object[] { index + 1, program.Format() });

    private static void Compare(int index, string source)
    {
        var input = SExpr.Parse(source);
        object? expected = SchemeSourceEvaluator.EvalSource(input);
        object? actual = SchemeL18Evaluator.EvalL18(SchemeCompiler.Compile(input));
        Assert.IsTrue(SchemeValues.Equal(expected, actual),
            $"Program {index} disagreed with source evaluation. "
                + $"Expected {expected}, got {actual}: {source}");
    }
}
