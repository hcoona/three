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
    using System.IO;
    using Hjg.Pngcs;

    /// <summary>
    /// IHDR chunk: http://www.w3.org/TR/PNG/#11IHDR
    /// </summary>
    public class PngChunkIHDR : PngChunkSingle
    {
        public const string ID = ChunkHelper.IHDR;
        public int Cols { get; set; }
        public int Rows { get; set; }
        public int Bitspc { get; set; }
        public int Colormodel { get; set; }
        public int Compmeth { get; set; }
        public int Filmeth { get; set; }
        public int Interlaced { get; set; }

        public PngChunkIHDR(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.NA;
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = new ChunkRaw(13, ChunkHelper.b_IHDR, true);
            int offset = 0;
            PngHelperInternal.WriteInt4tobytes(Cols, c.Data, offset);
            offset += 4;
            PngHelperInternal.WriteInt4tobytes(Rows, c.Data, offset);
            offset += 4;
            c.Data[offset++] = (byte)Bitspc;
            c.Data[offset++] = (byte)Colormodel;
            c.Data[offset++] = (byte)Compmeth;
            c.Data[offset++] = (byte)Filmeth;
            c.Data[offset++] = (byte)Interlaced;
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (c.Len != 13)
                throw new PngjException("Bad IDHR len " + c.Len);
            MemoryStream st = c.GetAsByteStream();
            Cols = PngHelperInternal.ReadInt4(st);
            Rows = PngHelperInternal.ReadInt4(st);
            // bit depth: number of bits per channel
            Bitspc = PngHelperInternal.ReadByte(st);
            Colormodel = PngHelperInternal.ReadByte(st);
            Compmeth = PngHelperInternal.ReadByte(st);
            Filmeth = PngHelperInternal.ReadByte(st);
            Interlaced = PngHelperInternal.ReadByte(st);
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkIHDR otherx = (PngChunkIHDR)other;
            Cols = otherx.Cols;
            Rows = otherx.Rows;
            Bitspc = otherx.Bitspc;
            Colormodel = otherx.Colormodel;
            Compmeth = otherx.Compmeth;
            Filmeth = otherx.Filmeth;
            Interlaced = otherx.Interlaced;
        }
    }
}
