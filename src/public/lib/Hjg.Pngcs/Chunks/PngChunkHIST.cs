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
    /// hIST chunk, see http://www.w3.org/TR/PNG/#11hIST
    /// Only for palette images
    /// </summary>
    public class PngChunkHIST : PngChunkSingle
    {
        public readonly static string ID = ChunkHelper.hIST;

        private int[] hist = Array.Empty<int>(); // should have same lenght as palette

        public PngChunkHIST(ImageInfo info)
            : base(ID, info) { }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.AFTER_PLTE_BEFORE_IDAT;
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = null;
            if (!ImgInfo.Indexed)
                throw new PngjException("only indexed images accept a HIST chunk");

            c = createEmptyChunk(hist.Length * 2, true);
            for (int i = 0; i < hist.Length; i++)
            {
                PngHelperInternal.WriteInt2tobytes(hist[i], c.Data, i * 2);
            }
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (!ImgInfo.Indexed)
                throw new PngjException("only indexed images accept a HIST chunk");
            int nentries = c.Data.Length / 2;
            hist = new int[nentries];
            for (int i = 0; i < hist.Length; i++)
            {
                hist[i] = PngHelperInternal.ReadInt2fromBytes(c.Data, i * 2);
            }
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkHIST otherx = (PngChunkHIST)other;
            hist = new int[otherx.hist.Length];
            Array.Copy((Array)(otherx.hist), 0, (Array)(this.hist), 0, otherx.hist.Length);
        }

        public int[] GetHist()
        {
            return hist;
        }
        /// <summary>
        /// should have same length as palette
        /// </summary>
        /// <param name="hist"></param>
        public void SetHist(int[] hist)
        {
            this.hist = hist;
        }

    }
}
