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
    /// sBIT chunk: http://www.w3.org/TR/PNG/#11sBIT
    ///
    /// this chunk structure depends on the image type
    /// </summary>
    public class PngChunkSBIT : PngChunkSingle
    {
        public const string ID = ChunkHelper.sBIT;

        //	significant bits
        public int Graysb { get; set; }
        public int Alphasb { get; set; }
        public int Redsb { get; set; }
        public int Greensb { get; set; }
        public int Bluesb { get; set; }

        public PngChunkSBIT(ImageInfo info)
            : base(ID, info)
        {
        }


        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.BEFORE_PLTE_AND_IDAT;
        }


        public override void ParseFromRaw(ChunkRaw c)
        {
            if (c.Len != GetLen())
                throw new PngjException("bad chunk length " + c);
            if (ImgInfo.Greyscale)
            {
                Graysb = PngHelperInternal.ReadInt1fromByte(c.Data, 0);
                if (ImgInfo.Alpha)
                    Alphasb = PngHelperInternal.ReadInt1fromByte(c.Data, 1);
            }
            else
            {
                Redsb = PngHelperInternal.ReadInt1fromByte(c.Data, 0);
                Greensb = PngHelperInternal.ReadInt1fromByte(c.Data, 1);
                Bluesb = PngHelperInternal.ReadInt1fromByte(c.Data, 2);
                if (ImgInfo.Alpha)
                    Alphasb = PngHelperInternal.ReadInt1fromByte(c.Data, 3);
            }
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = null;
            c = createEmptyChunk(GetLen(), true);
            if (ImgInfo.Greyscale)
            {
                c.Data[0] = (byte)Graysb;
                if (ImgInfo.Alpha)
                    c.Data[1] = (byte)Alphasb;
            }
            else
            {
                c.Data[0] = (byte)Redsb;
                c.Data[1] = (byte)Greensb;
                c.Data[2] = (byte)Bluesb;
                if (ImgInfo.Alpha)
                    c.Data[3] = (byte)Alphasb;
            }
            return c;
        }


        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkSBIT otherx = (PngChunkSBIT)other;
            Graysb = otherx.Graysb;
            Redsb = otherx.Redsb;
            Greensb = otherx.Greensb;
            Bluesb = otherx.Bluesb;
            Alphasb = otherx.Alphasb;
        }

        private int GetLen()
        {
            int len = ImgInfo.Greyscale ? 1 : 3;
            if (ImgInfo.Alpha) len += 1;
            return len;
        }
    }
}
