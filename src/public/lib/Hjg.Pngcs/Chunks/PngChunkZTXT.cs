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
    /// zTXt chunk: http://www.w3.org/TR/PNG/#11zTXt
    ///
    /// </summary>
    public class PngChunkZTXT : PngChunkTextVar
    {
        public const string ID = ChunkHelper.zTXt;

        public PngChunkZTXT(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkRaw CreateRawChunk()
        {
            if (key.Length == 0)
                throw new PngjException("Text chunk key must be non empty");
            MemoryStream ba = new MemoryStream();
            ChunkHelper.WriteBytesToStream(ba, ChunkHelper.ToBytes(key));
            ba.WriteByte(0); // separator
            ba.WriteByte(0); // compression method: 0
            byte[] textbytes = ChunkHelper.compressBytes(ChunkHelper.ToBytes(val), true);
            ChunkHelper.WriteBytesToStream(ba, textbytes);
            byte[] b = ba.ToArray();
            ChunkRaw chunk = createEmptyChunk(b.Length, false);
            chunk.Data = b;
            return chunk;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            int nullsep = -1;
            for (int i = 0; i < c.Data.Length; i++)
            { // look for first zero
                if (c.Data[i] != 0)
                    continue;
                nullsep = i;
                break;
            }
            if (nullsep < 0 || nullsep > c.Data.Length - 2)
                throw new PngjException("bad zTXt chunk: no separator found");
            key = ChunkHelper.ToString(c.Data, 0, nullsep);
            int compmet = (int)c.Data[nullsep + 1];
            if (compmet != 0)
                throw new PngjException("bad zTXt chunk: unknown compression method");
            byte[] uncomp = ChunkHelper.compressBytes(c.Data, nullsep + 2, c.Data.Length - nullsep - 2, false); // uncompress
            val = ChunkHelper.ToString(uncomp);
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkZTXT otherx = (PngChunkZTXT)other;
            key = otherx.key;
            val = otherx.val;
        }
    }
}
