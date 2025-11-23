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
using System.Collections.Generic;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;

namespace SamplesTests
{

    class TestTextChunks
    {
        /// <summary>
        /// Writes a new file with several text chunks, reads it back and compares
        /// </summary>

        public static void test()
        {
            Dictionary<string, string> texts = new Dictionary<string, string>();
            texts.Add("key1", "val");
            texts.Add("empty1", "");
            texts.Add("unicode1", "Hernán");
            texts.Add("zero1", "Hola\0chau");
            texts.Add("key2", "val");
            texts.Add("empty2", "");
            texts.Add("unicode2", "Hernán");
            texts.Add("zero2", "Hola\0chau");
            texts.Add("key3", "val");
            texts.Add("empty3", "");
            texts.Add("unicode3", "Hernán");
            texts.Add("zero3", "Hola\0chau");
            texts.Add("nolatin1", "Hernán\u1230");
            string suffix = "text";
            PngWriter png = TestsHelper.prepareFileTmp(suffix);
            png.GetMetadata().SetText("key1", texts["key1"], false, false);
            png.GetMetadata().SetText("key2", texts["key2"], true, false);
            png.GetMetadata().SetText("key3", texts["key3"], true, true);
            png.GetMetadata().SetText("empty1", texts["empty1"], false, false);
            png.GetMetadata().SetText("empty2", texts["empty2"], true, false);
            png.GetMetadata().SetText("empty3", texts["empty3"], true, true);
            png.GetMetadata().SetText("unicode1", texts["unicode1"], false, false);
            png.GetMetadata().SetText("unicode2", texts["unicode2"], true, false);
            png.GetMetadata().SetText("unicode3", texts["unicode3"], true, true);
            png.GetMetadata().SetText("nolatin1", texts["nolatin1"], false, false);
            png.GetMetadata().SetText("zero1", texts["zero1"], false, false);
            png.GetMetadata().SetText("zero2", texts["zero2"], true, false);
            png.GetMetadata().SetText("zero3", texts["zero3"], true, true);
            TestsHelper.endFileTmp(png);
            PngReader pngr = TestsHelper.getReaderTmp(suffix);
            pngr.ReadSkippingAllRows();
            int ok = 0;
            foreach (PngChunk c in pngr.GetChunksList().GetChunks())
            {
                if (!ChunkHelper.IsText(c))
                    continue;
                ok++;
                PngChunkTextVar ct = (PngChunkTextVar)c;
                string key = ct.GetKey();
                string val = ct.GetVal();
                Console.WriteLine(c.Id + " chunk. Key:" + key + " val='" + val + "'");
                if (!val.Equals(texts[key], StringComparison.Ordinal))
                {
                    Console.WriteLine("ERROR: expected '" + texts[key] + "' got '" + val
                            + "' key=" + key + " id=" + c.Id);
                }
            }
            if (ok != texts.Keys.Count)
                throw new Exception("number of text chunks does not coincide");
            Console.WriteLine("done");
        }


    }

}
