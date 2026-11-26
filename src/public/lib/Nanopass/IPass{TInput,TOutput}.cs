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
/// Transforms an input term into an output term.
/// </summary>
/// <typeparam name="TInput">The input term type.</typeparam>
/// <typeparam name="TOutput">The output term type.</typeparam>
public interface IPass<in TInput, out TOutput>
{
    /// <summary>
    /// Runs the pass on the input term.
    /// </summary>
    /// <param name="input">The term to transform.</param>
    /// <returns>The transformed term.</returns>
    TOutput Run(TInput input);
}
