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

namespace Hcoona.Nanopass;

/// <summary>
/// Describes a pass processor and its additional inputs and results.
/// </summary>
/// <param name="Name">The symbol naming the processor.</param>
/// <param name="InputKind">The input grammar symbol, or null for a raw input.</param>
/// <param name="OutputKind">The output grammar symbol, or null for a raw output.</param>
/// <param name="ExtraParameterCount">The number of parameters after the input term.</param>
/// <param name="ExtraReturnCount">The number of results in addition to the output term.</param>
public sealed record ProcessorDescriptor(
    Symbol Name,
    Symbol? InputKind,
    Symbol? OutputKind,
    int ExtraParameterCount,
    int ExtraReturnCount);
