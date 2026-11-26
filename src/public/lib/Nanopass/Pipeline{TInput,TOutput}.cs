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
/// Composes an ordered sequence of compatible passes into one transformation.
/// </summary>
/// <typeparam name="TInput">The pipeline input type.</typeparam>
/// <typeparam name="TOutput">The pipeline output type.</typeparam>
public sealed class Pipeline<TInput, TOutput> : IPass<TInput, TOutput>
{
    private readonly Func<TInput, TOutput> run;

    internal Pipeline(Func<TInput, TOutput> run, Seq<PipelineStage> stages)
    {
        this.run = run;
        this.Stages = stages;
    }

    /// <summary>
    /// Gets the stages in execution order.
    /// </summary>
    public Seq<PipelineStage> Stages { get; }

    /// <summary>
    /// Creates a pipeline that runs another pass after the existing stages.
    /// </summary>
    /// <typeparam name="TNext">The next pass's output type.</typeparam>
    /// <param name="next">The next pass, accepting the current pipeline's output type.</param>
    /// <returns>A new pipeline with the appended stage.</returns>
    public Pipeline<TInput, TNext> Then<TNext>(IPass<TOutput, TNext> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        PassDescriptor? descriptor = (next as INanopassPass)?.Descriptor;
        var stage = new PipelineStage(descriptor?.Name.Name ?? next.GetType().Name, descriptor);
        return new(input => RunStage(next, stage, this.run(input)), [.. this.Stages, stage]);
    }

    /// <inheritdoc/>
    public TOutput Run(TInput input) => this.run(input);

    /// <summary>
    /// Runs the pipeline and reports stage trace events to a callback for this run.
    /// </summary>
    /// <param name="input">The input term to transform.</param>
    /// <param name="trace">The callback to receive trace events.</param>
    /// <returns>The result of the pipeline.</returns>
    public TOutput Run(TInput input, Action<PassTraceEvent> trace)
    {
        using IDisposable scope = PassTrace.Capture(trace);
        return this.Run(input);
    }

    private static TNext RunStage<TNext>(
        IPass<TOutput, TNext> pass, PipelineStage stage, TOutput input)
    {
        if (!PassTrace.IsEnabled)
        {
            return pass.Run(input);
        }

        object? outerPending = PassTrace.GetPendingStage();
        try
        {
            PassTrace.SetPendingStage(null);
            PassTrace.Emit(new(stage.Name, null, PassTracePhase.Enter, input, stage.Descriptor));
            TNext output;
            PassTrace.SetPendingStage(pass);
            try
            {
                output = pass.Run(input);
            }
            finally
            {
                PassTrace.SetPendingStage(null);
            }

            PassTrace.Emit(new(stage.Name, null, PassTracePhase.Exit, output, stage.Descriptor));
            return output;
        }
        finally
        {
            PassTrace.SetPendingStage(outerPending);
        }
    }
}
