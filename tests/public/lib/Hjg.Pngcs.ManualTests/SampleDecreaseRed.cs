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

    class SampleDecreaseRed
    {

        public static void DecreaseRed(string origFilename, string destFilename)
        {
            if (origFilename.Equals(destFilename, StringComparison.Ordinal)) throw new PngjException("input and output file cannot coincide");
            PngReader pngr = FileHelper.CreatePngReader(origFilename);
            PngWriter pngw = FileHelper.CreatePngWriter(destFilename, pngr.ImgInfo, true);
            Console.WriteLine(pngr.ToString());
            int chunkBehav = ChunkCopyBehaviour.COPY_ALL_SAFE; // copy all 'safe' chunks
            // this can copy some metadata from reader
            pngw.CopyChunksFirst(pngr, chunkBehav);
            int channels = pngr.ImgInfo.Channels;
            if (channels < 3)
                throw new Exception("This method is for RGB/RGBA images");
            for (int row = 0; row < pngr.ImgInfo.Rows; row++)
            {
                ImageLine l1 = pngr.ReadRow(row);
                for (int j = 0; j < pngr.ImgInfo.Cols; j++)
                    l1.Scanline[j * channels] /= 2;
                pngw.WriteRow(l1, row);
            }
            // just in case some new metadata has been read after the image
            pngw.CopyChunksLast(pngr, chunkBehav);
            pngw.End();
        }


    }

}
