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
    /// Behaviours for chunks transfer when reading and writing.
    /// </summary>
    /// <remarks>
    /// They are bitmasks, can be OR-ed
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Keep Public API compatibility")]
    public class ChunkCopyBehaviour
    {

        /// <summary>
        /// Don't copy any chunk
        /// </summary>
        public static readonly int COPY_NONE = 0;

        /// <summary>
        /// Copy the Palette, if present
        /// </summary>
        public static readonly int COPY_PALETTE = 1;

        /// <summary>
        /// Copy all SAFE chunks
        /// </summary>
        public static readonly int COPY_ALL_SAFE = 1 << 2;

        /// <summary>
        /// Copy all chunks (includes palette)
        /// </summary>
        public static readonly int COPY_ALL = 1 << 3;

        /// <summary>
        /// Copy Physical resolution (DPI)
        /// </summary>
        public static readonly int COPY_PHYS = 1 << 4;


        /// <summary>
        /// Copy all textual chunks (not safe)
        /// </summary>
        public static readonly int COPY_TEXTUAL = 1 << 5;

        /// <summary>
        /// Copy transparency (not safe)
        /// </summary>
        public static readonly int COPY_TRANSPARENCY = 1 << 6; //


        /// <summary>
        /// Copy chunks unknown by our factory
        /// </summary>
        public static readonly int COPY_UNKNOWN = 1 << 7;

        /// <summary>
        /// Copy all known, except HIST, TIME and textual
        /// </summary>
        public static readonly int COPY_ALMOSTALL = 1 << 8;
    }
}
