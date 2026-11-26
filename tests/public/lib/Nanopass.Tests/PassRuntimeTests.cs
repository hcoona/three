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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Tests pass introspection, composition, and tracing.
/// </summary>
[TestClass]
public sealed class PassRuntimeTests
{
    /// <summary>
    /// Verifies composition between raw and typed endpoints and both descriptor lookup paths.
    /// </summary>
    [TestMethod]
    public void Pipeline_Then_ComposesAndDescribesRawAndTypedStages()
    {
        const string inputText = "(pair source (unit))";
        const string prettyText = "(pair (expr (value (id source))) (expr cooked))";
        SExpr input = SExpr.Parse(inputText);
        var term = InclusionPrettyLanguage.Parse(input);
        var parse = new ParseInclusionPass();
        var unparse = new UnparseInclusionPass();

        var pipeline = Pipeline.Start<SExpr>().Then(parse).Then(unparse);

        Assert.AreEqual(SExpr.Parse(prettyText), pipeline.Run(input));
        Assert.HasCount(2, pipeline.Stages);

        var firstStage = pipeline.Stages[0];
        var first = firstStage.Descriptor;
        Assert.AreEqual(nameof(ParseInclusionPass), firstStage.Name);
        Assert.IsNotNull(first);
        Assert.IsTrue(PassServices.IsPass(parse));
        Assert.AreSame(PassServices.GetDescriptor(parse), first);
        Assert.IsNull(first.InputLanguage);
        Assert.IsNull(first.InputEntryNonTerminal);
        Assert.AreSame(input, first.ParseInput(input));
        Assert.AreEqual(input, first.ParseInput(inputText));
        Assert.AreSame(InclusionPrettyLanguage.Descriptor, first.OutputLanguage);
        Assert.AreEqual<Symbol?>(new Symbol("Expr"), first.OutputEntryNonTerminal);
        Assert.AreEqual(prettyText, first.UnparseOutputToString(term));
        Assert.AreEqual(input, first.UnparseOutput(term, raw: true));

        var secondStage = pipeline.Stages[1];
        var second = secondStage.Descriptor;
        Assert.AreEqual(nameof(UnparseInclusionPass), secondStage.Name);
        Assert.IsNotNull(second);
        Assert.IsTrue(PassServices.IsPass(typeof(UnparseInclusionPass)));
        Assert.AreSame(PassServices.GetDescriptor<UnparseInclusionPass>(), second);
        Assert.AreSame(PassServices.GetDescriptor(unparse), second);
        Assert.AreSame(InclusionPrettyLanguage.Descriptor, second.InputLanguage);
        Assert.AreEqual<Symbol?>(new Symbol("Expr"), second.InputEntryNonTerminal);
        Assert.AreEqual(term, Assert.IsInstanceOfType<InclusionPrettyLanguage.Expr>(
            second.ParseInput(inputText)));
        Assert.IsNull(second.OutputLanguage);
        Assert.IsNull(second.OutputEntryNonTerminal);
        Assert.AreSame(input, second.UnparseOutput(input));
        Assert.AreSame(SExpr.Nil.Instance, second.UnparseOutput(null));
        Assert.AreEqual("42", second.UnparseOutputToString(42, raw: true));
    }

    /// <summary>
    /// Verifies that an explicit pipeline trace temporarily replaces an ambient capture.
    /// </summary>
    [TestMethod]
    public void Pipeline_RunWithTrace_DescribesPlainStageAndRestoresAmbientSink()
    {
        var stage = new IncrementPass();
        var pipeline = Pipeline.Start<int>().Then(stage);
        var events = new List<PassTraceEvent>();
        using var writer = new StringWriter { NewLine = "\n" };
        using IDisposable scope = PassTrace.Capture(writer);

        Assert.AreEqual(2, pipeline.Run(1, events.Add));
        Assert.AreEqual(string.Empty, writer.ToString());
        Assert.AreEqual(3, pipeline.Run(2));

        Assert.IsFalse(PassServices.IsPass(stage));
        Assert.IsFalse(PassServices.IsPass(typeof(IncrementPass)));
        Assert.ThrowsExactly<ArgumentException>(() => PassServices.GetDescriptor(stage));
        var metadata = Assert.ContainsSingle(pipeline.Stages);
        Assert.AreEqual(nameof(IncrementPass), metadata.Name);
        Assert.IsNull(metadata.Descriptor);
        Assert.HasCount(2, events);
        Assert.AreEqual(
            new PassTraceEvent(nameof(IncrementPass), null, PassTracePhase.Enter, 1, null),
            events[0]);
        Assert.AreEqual(
            new PassTraceEvent(nameof(IncrementPass), null, PassTracePhase.Exit, 2, null),
            events[1]);
        Assert.IsNull(events[1].TryUnparse());
        Assert.AreEqual(
            "IncrementPass Enter: 2\nIncrementPass Exit: 3\n",
            writer.ToString());
    }

    /// <summary>
    /// Verifies that only an explicit request unparses a traced output.
    /// </summary>
    [TestMethod]
    public void Pipeline_RunWithTrace_DefersOutputUnparsingUntilRequested()
    {
        SExpr input = SExpr.Parse("(pair source (unit))");
        var expected = InclusionPrettyLanguage.Parse(input);
        int prettyCalls = 0;
        var pass = new ParseInclusionPass((value, raw) =>
        {
            if (!raw)
            {
                prettyCalls++;
                throw new InvalidOperationException("The prettifier failed.");
            }

            return InclusionPrettyLanguage.UnparseObject(value, raw);
        });
        var pipeline = Pipeline.Start<SExpr>().Then(pass);
        var events = new List<PassTraceEvent>();

        Assert.AreEqual(expected, pipeline.Run(input, events.Add));
        using (PassTrace.Capture(TextWriter.Null))
        {
            Assert.AreEqual(expected, pipeline.Run(input));
        }

        Assert.AreEqual(0, prettyCalls);
        Assert.HasCount(2, events);
        Assert.AreSame(pass.Descriptor, events[0].Descriptor);
        Assert.IsNull(events[0].TryUnparse());
        Assert.AreSame(pass.Descriptor, events[1].Descriptor);
        Assert.AreEqual(expected, events[1].Value);
        Assert.AreEqual(input, events[1].TryUnparse(raw: true));
        Assert.AreEqual(0, prettyCalls);
        Assert.ThrowsExactly<InvalidOperationException>(() => events[1].TryUnparse());
        Assert.AreEqual(1, prettyCalls);
    }

    /// <summary>
    /// Verifies a traced stage reports one pass boundary without suppressing processor events.
    /// </summary>
    [TestMethod]
    public void Pipeline_RunWithCapture_ReportsOnePassBoundaryForTracedStage()
    {
        var pipeline = Pipeline.Start<LoweringInput.Expr>().Then(new LowerNegationRecur());
        var input = LoweringInput.Parse("(neg (lit 5))");
        var expected = LoweringOutput.Parse("(sub (lit 0) (lit 5))");
        var events = new List<PassTraceEvent>();

        using (PassTrace.Capture(events.Add))
        {
            Assert.AreEqual(expected, pipeline.Run(input));
        }

        var passEvents = events.Where(static item => item.ProcessorName is null);
        Assert.AreSequenceEqual<PassTracePhase>(
            [PassTracePhase.Enter, PassTracePhase.Exit],
            passEvents.Select(static item => item.Phase));
        Assert.IsNull(events[0].ProcessorName);
        Assert.AreEqual(input, events[0].Value);
        Assert.IsNull(events[^1].ProcessorName);
        Assert.AreEqual(expected, events[^1].Value);

        var processorPhases = events.Where(static item => item.ProcessorName is not null)
            .Select(static item => item.Phase).ToArray();
        Assert.Contains(PassTracePhase.Enter, processorPhases);
        Assert.Contains(PassTracePhase.Exit, processorPhases);
    }

    /// <summary>
    /// Verifies a processor exit preserves extras while unparsing only the primary term.
    /// </summary>
    [TestMethod]
    public void TraceEvent_TryUnparse_RetainsTupleWhileUnparsingPrimaryTerm()
    {
        var input = LoweringOutput.Parse("(lit 5)");
        var events = new List<PassTraceEvent>();

        using (PassTrace.Capture(events.Add))
        {
            _ = new TracedTupleProcessorPass().Run(input);
        }

        var exit = Assert.ContainsSingle(events.Where(static item =>
            item.ProcessorName == nameof(TracedTupleProcessorPass.Map)
            && item.Phase == PassTracePhase.Exit));
        Assert.AreEqual(SExpr.Parse("(lit 5)"), exit.TryUnparse());
        Assert.AreEqual((input, 17L), exit.Value);
    }

    /// <summary>
    /// Verifies that concurrent asynchronous captures do not receive each other's trace events.
    /// </summary>
    /// <returns>A task that completes after both captures have finished.</returns>
    [TestMethod]
    public async Task PassTrace_Capture_IsolatesConcurrentAsyncFlows()
    {
        var pipeline = Pipeline.Start<int>().Then(new IncrementPass());
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<object?[]> first = CaptureAsync(10);
        Task<object?[]> second = CaptureAsync(20);

        start.SetResult();
        object?[][] values = await Task.WhenAll(first, second);

        Assert.AreSequenceEqual<object?>([10, 11], values[0]);
        Assert.AreSequenceEqual<object?>([20, 21], values[1]);

        async Task<object?[]> CaptureAsync(int input)
        {
            var events = new List<PassTraceEvent>();
            using (PassTrace.Capture(events.Add))
            {
                await start.Task;
                pipeline.Run(input);
            }

            pipeline.Run(-1);
            return events.Select(trace => trace.Value).ToArray();
        }
    }

    private sealed class ParseInclusionPass(Func<object?, bool, SExpr>? outputUnparser = null) :
        IPass<SExpr, InclusionPrettyLanguage.Expr>, INanopassPass
    {
        /// <inheritdoc/>
        public PassDescriptor Descriptor { get; } = new(
            nameof(ParseInclusionPass), null, null, InclusionPrettyLanguage.Descriptor, null, [],
            outputUnparser: outputUnparser ?? InclusionPrettyLanguage.UnparseObject);

        /// <inheritdoc/>
        public InclusionPrettyLanguage.Expr Run(SExpr input) =>
            InclusionPrettyLanguage.Parse(input);
    }

    private sealed class UnparseInclusionPass :
        IPass<InclusionPrettyLanguage.Expr, SExpr>, INanopassPass, INanopassPassMetadata
    {
        private static readonly PassDescriptor Metadata = new(
            nameof(UnparseInclusionPass),
            InclusionPrettyLanguage.Descriptor,
            "Expr",
            null,
            null,
            [],
            inputParser: static datum => InclusionPrettyLanguage.Parse(datum));

        static PassDescriptor INanopassPassMetadata.PassDescriptor => Metadata;

        /// <inheritdoc/>
        public PassDescriptor Descriptor => Metadata;

        /// <inheritdoc/>
        public SExpr Run(InclusionPrettyLanguage.Expr input) =>
            InclusionPrettyLanguage.Unparse(input);
    }

    private sealed class IncrementPass : IPass<int, int>
    {
        public static PassDescriptor UnrelatedDescriptor =>
            PassServices.GetDescriptor<UnparseInclusionPass>();

        /// <inheritdoc/>
        public int Run(int input) => input + 1;
    }
}

[Pass(typeof(LoweringOutput), typeof(LoweringOutput), Trace = true)]
internal sealed partial class TracedTupleProcessorPass
{
    [Processor(DefaultExtraReturns = new object[] { 17L })]
    internal partial (LoweringOutput.Expr Term, long Extra) Map(LoweringOutput.Expr ir);
}
