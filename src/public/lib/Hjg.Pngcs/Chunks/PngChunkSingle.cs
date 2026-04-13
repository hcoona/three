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

using System;

namespace Hjg.Pngcs.Chunks
{
    /// <summary>
    /// A Chunk type that does not allow duplicate in an image
    /// </summary>
    public abstract class PngChunkSingle : PngChunk
    {
        public PngChunkSingle(string id, ImageInfo imgInfo)
            : base(id, imgInfo)
        {
        }

        public sealed override bool AllowsMultiple()
        {
            return false;
        }

        public override int GetHashCode()
        {
            int prime = 31;
            int result = 1;
            result = prime * result + ((Id == null) ? 0 : Id.GetHashCode());
            return result;
        }

        public override bool Equals(object obj)
        {
            return (obj is PngChunkSingle && Id != null && Id.Equals(((PngChunkSingle)obj).Id, StringComparison.Ordinal));
        }

    }
}
