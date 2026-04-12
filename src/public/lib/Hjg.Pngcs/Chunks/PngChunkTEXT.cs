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
    /// tEXt chunk: latin1 uncompressed text
    /// </summary>
    public class PngChunkTEXT : PngChunkTextVar
    {
        public const string ID = ChunkHelper.tEXt;

        public PngChunkTEXT(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkRaw CreateRawChunk()
        {
            if (key.Length == 0)
                throw new PngjException("Text chunk key must be non empty");
            byte[] b1 = PngHelperInternal.charsetLatin1.GetBytes(key);
            byte[] b2 = PngHelperInternal.charsetLatin1.GetBytes(val);
            ChunkRaw chunk = createEmptyChunk(b1.Length + b2.Length + 1, true);
            Array.Copy(b1, 0, chunk.Data, 0, b1.Length);
            chunk.Data[b1.Length] = 0;
            Array.Copy(b2, 0, chunk.Data, b1.Length + 1, b2.Length);
            return chunk;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            int i;
            for (i = 0; i < c.Data.Length; i++)
                if (c.Data[i] == 0)
                    break;
            key = PngHelperInternal.charsetLatin1.GetString(c.Data, 0, i);
            i++;
            val = i < c.Data.Length ? PngHelperInternal.charsetLatin1.GetString(c.Data, i, c.Data.Length - i) : "";
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkTEXT otherx = (PngChunkTEXT)other;
            key = otherx.key;
            val = otherx.val;
        }
    }
}
