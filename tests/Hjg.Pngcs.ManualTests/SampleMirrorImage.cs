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

namespace SampleTests
{
    using Hjg.Pngcs;
    using Hjg.Pngcs.Chunks;


    public class SampleMirrorImage
    {

        public static void mirror(string orig, string dest)
        {
            if (orig.Equals(dest, System.StringComparison.Ordinal)) throw new PngjException("input and output file cannot coincide");

            PngReader pngr = FileHelper.CreatePngReader(orig);
            PngWriter pngw = FileHelper.CreatePngWriter(dest, pngr.ImgInfo, true);
            pngr.SetUnpackedMode(true); // we dont want to do the unpacking ourselves, we want a sample per array element
            pngw.SetUseUnPackedMode(true); // not really necesary here, as we pass the ImageLine, but anyway...
            pngw.CopyChunksFirst(pngr, ChunkCopyBehaviour.COPY_ALL_SAFE);
            for (int row = 0; row < pngr.ImgInfo.Rows; row++)
            {
                ImageLine l1 = pngr.ReadRowInt(row);
                mirrorLineInt(pngr.ImgInfo, l1.Scanline);
                pngw.WriteRow(l1, row);
            }
            pngw.CopyChunksLast(pngr, ChunkCopyBehaviour.COPY_ALL_SAFE);
            pngw.End();
        }

        private static void mirrorLineInt(ImageInfo imgInfo, int[] line)
        { // unpacked line
            int channels = imgInfo.Channels;
            for (int c1 = 0, c2 = imgInfo.Cols - 1; c1 < c2; c1++, c2--)
            { // swap pixels (not samples!)
                for (int i = 0; i < channels; i++)
                {
                    int aux = line[c1 * channels + i];
                    line[c1 * channels + i] = line[c2 * channels + i];
                    line[c2 * channels + i] = aux;
                }
            }
        }


    }
}
