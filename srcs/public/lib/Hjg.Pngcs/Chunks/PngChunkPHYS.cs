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
    /// pHYs chunk: http://www.w3.org/TR/PNG/#11pHYs
    /// </summary>
    public class PngChunkPHYS : PngChunkSingle
    {
        public const string ID = ChunkHelper.pHYs;

        public long PixelsxUnitX { get; set; }
        public long PixelsxUnitY { get; set; }
        /// <summary>
        /// 0: unknown 1:metre
        /// </summary>
        public int Units { get; set; }

        public PngChunkPHYS(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.BEFORE_IDAT;
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = createEmptyChunk(9, true);
            PngHelperInternal.WriteInt4tobytes((int)PixelsxUnitX, c.Data, 0);
            PngHelperInternal.WriteInt4tobytes((int)PixelsxUnitY, c.Data, 4);
            c.Data[8] = (byte)Units;
            return c;
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkPHYS otherx = (PngChunkPHYS)other;
            this.PixelsxUnitX = otherx.PixelsxUnitX;
            this.PixelsxUnitY = otherx.PixelsxUnitY;
            this.Units = otherx.Units;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (c.Len != 9)
                throw new PngjException("bad chunk length " + c);
            PixelsxUnitX = PngHelperInternal.ReadInt4fromBytes(c.Data, 0);
            if (PixelsxUnitX < 0)
                PixelsxUnitX += 0x100000000L;
            PixelsxUnitY = PngHelperInternal.ReadInt4fromBytes(c.Data, 4);
            if (PixelsxUnitY < 0)
                PixelsxUnitY += 0x100000000L;
            Units = PngHelperInternal.ReadInt1fromByte(c.Data, 8);
        }

        /// <summary>
        /// returns -1 if not in meters, or not equal
        /// </summary>
        /// <returns></returns>
        public double GetAsDpi()
        {
            if (Units != 1 || PixelsxUnitX != PixelsxUnitY)
                return -1;
            return ((double)PixelsxUnitX) * 0.0254d;
        }

        /// <summary>
        /// returns -1 if the physicial unit is unknown
        /// </summary>
        /// <returns></returns>
        public double[] GetAsDpi2()
        {
            if (Units != 1)
                return new double[] { -1, -1 };
            return new double[] { ((double)PixelsxUnitX) * 0.0254, ((double)PixelsxUnitY) * 0.0254 };
        }

        /// <summary>
        /// same in both directions
        /// </summary>
        /// <param name="dpi"></param>
        public void SetAsDpi(double dpi)
        {
            Units = 1;
            PixelsxUnitX = (long)(dpi / 0.0254d + 0.5d);
            PixelsxUnitY = PixelsxUnitX;
        }

        public void SetAsDpi2(double dpix, double dpiy)
        {
            Units = 1;
            PixelsxUnitX = (long)(dpix / 0.0254 + 0.5);
            PixelsxUnitY = (long)(dpiy / 0.0254 + 0.5);
        }


    }
}
