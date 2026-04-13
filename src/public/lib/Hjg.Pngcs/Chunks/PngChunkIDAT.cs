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

namespace Hjg.Pngcs.Chunks
{
    using Hjg.Pngcs;
    /// <summary>
    /// IDAT chunk http://www.w3.org/TR/PNG/#11IDAT
    ///
    /// This object is dummy placeholder - We treat this chunk in a very different way than ancillary chnks
    /// </summary>
    public class PngChunkIDAT : PngChunkMultiple
    {
        public const string ID = ChunkHelper.IDAT;

        public PngChunkIDAT(ImageInfo i, int len, long offset)
            : base(ID, i)
        {
            this.Length = len;
            this.Offset = offset;
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.NA;
        }

        public override ChunkRaw CreateRawChunk()
        {// does nothing
            return null;
        }

        public override void ParseFromRaw(ChunkRaw c)
        { // does nothing
        }

        public override void CloneDataFromRead(PngChunk other)
        {
        }
    }
}
