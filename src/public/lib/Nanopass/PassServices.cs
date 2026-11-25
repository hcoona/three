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
/// Identifies Nanopass transformations and retrieves their metadata.
/// </summary>
public static class PassServices
{
    /// <summary>
    /// Determines whether an object implements <see cref="INanopassPass"/>.
    /// </summary>
    /// <param name="candidate">The object to inspect.</param>
    /// <returns>Whether the object provides an instance pass descriptor.</returns>
    public static bool IsPass(object? candidate) => candidate is INanopassPass;

    /// <summary>
    /// Determines whether a type implements <see cref="INanopassPass"/>.
    /// </summary>
    /// <param name="candidateType">The type to inspect.</param>
    /// <returns>Whether the type provides an instance pass descriptor.</returns>
    public static bool IsPass(Type? candidateType) =>
        typeof(INanopassPass).IsAssignableFrom(candidateType);

    /// <summary>
    /// Gets an object's instance pass descriptor.
    /// </summary>
    /// <param name="pass">The pass to inspect.</param>
    /// <returns>The descriptor of the pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pass"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The object does not implement <see cref="INanopassPass"/>.
    /// </exception>
    public static PassDescriptor GetDescriptor(object pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        return pass is INanopassPass nanopass
            ? nanopass.Descriptor
            : throw new ArgumentException("The object has no pass descriptor.", nameof(pass));
    }

    /// <summary>
    /// Gets a pass descriptor without creating an instance.
    /// </summary>
    /// <typeparam name="TPass">The pass type providing static metadata.</typeparam>
    /// <returns>The static descriptor of the pass.</returns>
    public static PassDescriptor GetDescriptor<TPass>()
        where TPass : INanopassPassMetadata => TPass.PassDescriptor;
}
