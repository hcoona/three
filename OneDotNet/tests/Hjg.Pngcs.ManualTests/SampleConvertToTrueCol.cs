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

using System;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;

namespace SamplesTests
{

    class SampleConvertToTrueCol
    {

        public static void doit(string orig)
        {
            string copy = TestsHelper.addSuffixToName(orig, "_tc");

            PngReader pngr = FileHelper.CreatePngReader(orig);
            if (!pngr.ImgInfo.Indexed)
                throw new Exception("Not indexed image");
            PngChunkPLTE plte = pngr.GetMetadata().GetPLTE();
            PngChunkTRNS trns = pngr.GetMetadata().GetTRNS(); // transparency metadata, can be null
            bool alpha = trns != null;
            ImageInfo im2 = new ImageInfo(pngr.ImgInfo.Cols, pngr.ImgInfo.Rows, 8, alpha);
            PngWriter pngw = FileHelper.CreatePngWriter(copy, im2, true);
            pngw.CopyChunksFirst(pngr, ChunkCopyBehaviour.COPY_ALL_SAFE);
            int[] buf = null;
            for (int row = 0; row < pngr.ImgInfo.Rows; row++)
            {
                ImageLine line = pngr.ReadRowInt(row);
                buf = ImageLineHelper.Palette2rgb(line, plte, trns, buf);
                pngw.WriteRowInt(buf, row);
            }
            pngw.CopyChunksLast(pngr, ChunkCopyBehaviour.COPY_ALL_SAFE);
            pngr.End();
            pngw.End();
            Console.WriteLine("True color: " + copy);
        }


    }

}
