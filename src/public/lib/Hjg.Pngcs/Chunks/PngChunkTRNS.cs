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
    /// tRNS chunk: http://www.w3.org/TR/PNG/#11tRNS
    /// </summary>
    public class PngChunkTRNS : PngChunkSingle
    {
        public const string ID = ChunkHelper.tRNS;

        // this chunk structure depends on the image type
        // only one of these is meaningful
        private int gray;
        private int red, green, blue;
        private int[] paletteAlpha;

        public PngChunkTRNS(ImageInfo info)
            : base(ID, info)
        {
        }
        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.AFTER_PLTE_BEFORE_IDAT;
        }
        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = null;
            if (ImgInfo.Greyscale)
            {
                c = createEmptyChunk(2, true);
                PngHelperInternal.WriteInt2tobytes(gray, c.Data, 0);
            }
            else if (ImgInfo.Indexed)
            {
                c = createEmptyChunk(paletteAlpha.Length, true);
                for (int n = 0; n < c.Len; n++)
                {
                    c.Data[n] = (byte)paletteAlpha[n];
                }
            }
            else
            {
                c = createEmptyChunk(6, true);
                PngHelperInternal.WriteInt2tobytes(red, c.Data, 0);
                PngHelperInternal.WriteInt2tobytes(green, c.Data, 0);
                PngHelperInternal.WriteInt2tobytes(blue, c.Data, 0);
            }
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (ImgInfo.Greyscale)
            {
                gray = PngHelperInternal.ReadInt2fromBytes(c.Data, 0);
            }
            else if (ImgInfo.Indexed)
            {
                int nentries = c.Data.Length;
                paletteAlpha = new int[nentries];
                for (int n = 0; n < nentries; n++)
                {
                    paletteAlpha[n] = (int)(c.Data[n] & 0xff);
                }
            }
            else
            {
                red = PngHelperInternal.ReadInt2fromBytes(c.Data, 0);
                green = PngHelperInternal.ReadInt2fromBytes(c.Data, 2);
                blue = PngHelperInternal.ReadInt2fromBytes(c.Data, 4);
            }
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkTRNS otherx = (PngChunkTRNS)other;
            gray = otherx.gray;
            red = otherx.red;
            green = otherx.green;
            blue = otherx.blue;
            if (otherx.paletteAlpha != null)
            {
                paletteAlpha = new int[otherx.paletteAlpha.Length];
                Array.Copy(otherx.paletteAlpha, 0, paletteAlpha, 0, paletteAlpha.Length);
            }
        }

        public void SetRGB(int r, int g, int b)
        {
            if (ImgInfo.Greyscale || ImgInfo.Indexed)
                throw new PngjException("only rgb or rgba images support this");
            red = r;
            green = g;
            blue = b;
        }

        public int[] GetRGB()
        {
            if (ImgInfo.Greyscale || ImgInfo.Indexed)
                throw new PngjException("only rgb or rgba images support this");
            return new int[] { red, green, blue };
        }

        public void SetGray(int g)
        {
            if (!ImgInfo.Greyscale)
                throw new PngjException("only grayscale images support this");
            gray = g;
        }

        public int GetGray()
        {
            if (!ImgInfo.Greyscale)
                throw new PngjException("only grayscale images support this");
            return gray;
        }

        /// <summary>
        /// WARNING: non deep copy
        /// </summary>
        /// <param name="palAlpha"></param>
        public void SetPalletteAlpha(int[] palAlpha)
        {
            if (!ImgInfo.Indexed)
                throw new PngjException("only indexed images support this");
            paletteAlpha = palAlpha;
        }

        /// <summary>
        /// utiliy method : to use when only one pallete index is set as totally transparent
        /// </summary>
        /// <param name="palAlphaIndex"></param>
        public void setIndexEntryAsTransparent(int palAlphaIndex)
        {
            if (!ImgInfo.Indexed)
                throw new PngjException("only indexed images support this");
            paletteAlpha = new int[] { palAlphaIndex + 1 };
            for (int i = 0; i < palAlphaIndex; i++)
                paletteAlpha[i] = 255;
            paletteAlpha[palAlphaIndex] = 0;
        }

        /// <summary>
        /// WARNING: non deep copy
        /// </summary>
        /// <returns></returns>
        public int[] GetPalletteAlpha()
        {
            if (!ImgInfo.Indexed)
                throw new PngjException("only indexed images support this");
            return paletteAlpha;
        }
    }
}
