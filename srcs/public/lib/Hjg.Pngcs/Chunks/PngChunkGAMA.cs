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
    /// gAMA chunk, see http://www.w3.org/TR/PNG/#11gAMA
    /// </summary>
    public class PngChunkGAMA : PngChunkSingle
    {
        public const string ID = ChunkHelper.gAMA;

        private double gamma;

        public PngChunkGAMA(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.BEFORE_PLTE_AND_IDAT;
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = createEmptyChunk(4, true);
            int g = (int)(gamma * 100000 + 0.5d);
            PngHelperInternal.WriteInt4tobytes(g, c.Data, 0);
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (c.Len != 4)
                throw new PngjException("bad chunk " + c);
            int g = PngHelperInternal.ReadInt4fromBytes(c.Data, 0);
            gamma = ((double)g) / 100000.0d;
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            gamma = ((PngChunkGAMA)other).gamma;
        }

        public double GetGamma()
        {
            return gamma;
        }

        public void SetGamma(double gamma)
        {
            this.gamma = gamma;
        }
    }
}
