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

using System.Linq;
using System.Runtime.CompilerServices;

namespace Hcoona.Nanopass;

/// <summary>
/// Records a value at the boundary of a pass or processor.
/// </summary>
/// <param name="PassName">The name of the pass.</param>
/// <param name="ProcessorName">The processor name, or null for a pass-level event.</param>
/// <param name="Phase">Whether execution is starting or has finished.</param>
/// <param name="Value">The input or output value.</param>
/// <param name="Descriptor">The pass descriptor, if available.</param>
public sealed record PassTraceEvent(
    string PassName,
    string? ProcessorName,
    PassTracePhase Phase,
    object? Value,
    PassDescriptor? Descriptor)
{
    /// <summary>
    /// Unparses the recorded output on demand using the pass descriptor.
    /// </summary>
    /// <param name="raw">Whether to omit language prettifiers.</param>
    /// <returns>
    /// The output datum, or null for entry events or events without a descriptor.
    /// </returns>
    /// <remarks>
    /// For processor results with extra returns, only the primary term is unparsed;
    /// <see cref="Value"/> retains the complete tuple.
    /// Any exception from the output unparser propagates to the caller.
    /// </remarks>
    public SExpr? TryUnparse(bool raw = false)
    {
        if (this.Phase != PassTracePhase.Exit || this.Descriptor is not { } descriptor)
        {
            return null;
        }

        object? value = this.Value;
        if (this.ProcessorName is { } name && value is ITuple { Length: > 0 } tuple
            && descriptor.Processors.Any(processor =>
                processor.ExtraReturnCount > 0 && processor.Name.Name == name))
        {
            value = tuple[0];
        }

        return descriptor.UnparseOutput(value, raw);
    }
}
