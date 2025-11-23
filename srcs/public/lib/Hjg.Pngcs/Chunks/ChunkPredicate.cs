// Pngcs is a CSharp implementation of PNG binary reader and writer.
// Copyright (C) 2025 Shuai Zhang <zhangshuai.ustc@gmail.com>
//
// Based on original work:
//   Copyright 2012    Hernán J. González    hgonzalez@gmail.com
//   Licensed under the Apache License, Version 2.0
//
//   You should have received a copy of the Apache License 2.0
//   along with the program.
//   If not, see <http://www.apache.org/licenses/LICENSE-2.0>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics.CodeAnalysis;

namespace Hjg.Pngcs.Chunks
{
    /// <summary>
    /// Decides if another chunk "matches", according to some criterion
    /// </summary>
    [SuppressMessage(
        "Naming",
        "CA1715:Identifiers should have correct prefix",
        Justification = "Keep Public API compatibility")]
    public interface ChunkPredicate
    {
        /// <summary>
        /// The other chunk matches with this one
        /// </summary>
        /// <param name="chunk">The other chunk</param>
        /// <returns>true if matches</returns>
        bool Matches(PngChunk chunk);
    }
}
