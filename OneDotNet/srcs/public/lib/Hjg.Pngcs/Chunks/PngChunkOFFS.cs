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
    /// oFFs chunk: http://www.libpng.org/pub/png/spec/register/pngext-1.3.0-pdg.html#C.oFFs
    /// </summary>
    public class PngChunkOFFS : PngChunkSingle
    {
        public const string ID = "oFFs";

        private long posX;
        private long posY;
        private int units; // 0: pixel 1:micrometer

        public PngChunkOFFS(ImageInfo info)
            : base(ID, info) { }


        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.BEFORE_IDAT;
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = createEmptyChunk(9, true);
            PngHelperInternal.WriteInt4tobytes((int)posX, c.Data, 0);
            PngHelperInternal.WriteInt4tobytes((int)posY, c.Data, 4);
            c.Data[8] = (byte)units;
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (c.Len != 9)
                throw new PngjException("bad chunk length " + c);
            posX = PngHelperInternal.ReadInt4fromBytes(c.Data, 0);
            if (posX < 0)
                posX += 0x100000000L;
            posY = PngHelperInternal.ReadInt4fromBytes(c.Data, 4);
            if (posY < 0)
                posY += 0x100000000L;
            units = PngHelperInternal.ReadInt1fromByte(c.Data, 8);
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkOFFS otherx = (PngChunkOFFS)other;
            this.posX = otherx.posX;
            this.posY = otherx.posY;
            this.units = otherx.units;
        }

        /// <summary>
        /// 0: pixel, 1:micrometer
        /// </summary>
        /// <returns></returns>
        public int GetUnits()
        {
            return units;
        }

        /// <summary>
        /// 0: pixel, 1:micrometer
        /// </summary>
        /// <param name="units"></param>
        public void SetUnits(int units)
        {
            this.units = units;
        }

        public long GetPosX()
        {
            return posX;
        }

        public void SetPosX(long posX)
        {
            this.posX = posX;
        }

        public long GetPosY()
        {
            return posY;
        }

        public void SetPosY(long posY)
        {
            this.posY = posY;
        }
    }
}
