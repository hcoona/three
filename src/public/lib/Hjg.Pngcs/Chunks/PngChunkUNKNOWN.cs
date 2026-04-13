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

    using System;
    using Hjg.Pngcs;
    /// <summary>
    /// Unknown (for our chunk factory) chunk type.
    /// </summary>
    public class PngChunkUNKNOWN : PngChunkMultiple
    {

        private byte[] data;

        public PngChunkUNKNOWN(string id, ImageInfo info)
            : base(id, info)
        {
        }

        private PngChunkUNKNOWN(PngChunkUNKNOWN c, ImageInfo info)
            : base(c.Id, info)
        {
            Array.Copy(c.data, 0, data, 0, c.data.Length);
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.NONE;
        }


        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw p = createEmptyChunk(data.Length, false);
            p.Data = this.data;
            return p;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            data = c.Data;
        }

        /* does not copy! */
        public byte[] GetData()
        {
            return data;
        }

        /* does not copy! */
        public void SetData(byte[] data0)
        {
            this.data = data0;
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            // THIS SHOULD NOT BE CALLED IF ALREADY CLONED WITH COPY CONSTRUCTOR
            PngChunkUNKNOWN c = (PngChunkUNKNOWN)other;
            data = c.data; // not deep copy
        }
    }
}
