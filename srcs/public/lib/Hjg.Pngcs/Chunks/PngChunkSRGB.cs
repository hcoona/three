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
    /// sRGB chunk: http://www.w3.org/TR/PNG/#11sRGB
    /// </summary>
    public class PngChunkSRGB : PngChunkSingle
    {
        public const string ID = ChunkHelper.sRGB;

        // CA1707: Identifiers should not contain underscores
        // Justification: Keep Public API compatibility
#pragma warning disable CA1707
        public const int RENDER_INTENT_Perceptual = 0;
        public const int RENDER_INTENT_Relative_colorimetric = 1;
        public const int RENDER_INTENT_Saturation = 2;
        public const int RENDER_INTENT_Absolute_colorimetric = 3;
#pragma warning restore CA1707

        public int Intent { get; set; }

        public PngChunkSRGB(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.BEFORE_PLTE_AND_IDAT;
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = null;
            c = createEmptyChunk(1, true);
            c.Data[0] = (byte)Intent;
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (c.Len != 1)
                throw new PngjException("bad chunk length " + c);
            Intent = PngHelperInternal.ReadInt1fromByte(c.Data, 0);
        }


        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkSRGB otherx = (PngChunkSRGB)other;
            Intent = otherx.Intent;
        }


    }
}
