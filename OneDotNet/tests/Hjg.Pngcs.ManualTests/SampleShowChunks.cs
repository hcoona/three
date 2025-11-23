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

namespace SamplesTests
{

    using System;
    using Hjg.Pngcs;

    /**
 * prints all chunks (remember that IDAT is shown as only one pseudo zero-length chunk)
 */
    public class SampleShowChunks
    {

        public static void showChunks(string file)
        {
            PngReader pngr = FileHelper.CreatePngReader(file);
            pngr.MaxTotalBytesRead = 1024 * 1024 * 1024L * 3; // 3Gb!
            pngr.ReadSkippingAllRows();
            Console.Out.WriteLine(pngr.ToString());
            Console.Out.WriteLine(pngr.GetChunksList().ToStringFull());
        }


    }
}
