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
using System.IO;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;

namespace SamplesTests
{


    /// <summary>
    /// To test all images in PNG test suite (except interlaced) doing a horizontal
    /// mirror on all them
    /// </summary>
    ///
    public class TestPngSuite
    {

        static void testmirror(string orig, string origni, string truecolor)
        {
            string mirror = TestsHelper.addSuffixToName(orig, "_mirror");
            string recov = TestsHelper.addSuffixToName(orig, "_recov");
            long crc0 = 0;
            bool interlaced;
            bool palete;
            {
                PngReader pngr = FileHelper.CreatePngReader(orig);
                palete = pngr.ImgInfo.Indexed;
                PngHelperInternal.InitCrcForTests(pngr);
                pngr.SetUnpackedMode(true);
                interlaced = pngr.IsInterlaced();
                PngWriter pngw = FileHelper.CreatePngWriter(mirror, pngr.ImgInfo, true);
                pngw.SetFilterType(FilterType.FILTER_CYCLIC); // just to test all filters
                pngw.CopyChunksFirst(pngr, ChunkCopyBehaviour.COPY_ALL);
                pngw.SetUseUnPackedMode(true);
                for (int row = 0; row < pngr.ImgInfo.Rows; row++)
                {
                    ImageLine line = pngr.ReadRowInt(row);
                    mirrorLine(line);
                    pngw.WriteRow(line, row);
                }
                pngr.End();
                crc0 = PngHelperInternal.GetCrctestVal(pngr);
                pngw.CopyChunksFirst(pngr, ChunkCopyBehaviour.COPY_ALL);
                pngw.End();
            }
            // mirror again, now with BYTE (if depth<16) and loading all rows
            {
                PngReader pngr2 = FileHelper.CreatePngReader(mirror);
                pngr2.SetUnpackedMode(true);
                PngWriter pngw = FileHelper.CreatePngWriter(recov, pngr2.ImgInfo, true);
                pngw.SetFilterType(FilterType.FILTER_AGGRESSIVE);
                pngw.CopyChunksFirst(pngr2, ChunkCopyBehaviour.COPY_ALL);
                pngw.SetUseUnPackedMode(true);
                ImageLines lines = pngr2.ImgInfo.BitDepth < 16 ? pngr2.ReadRowsByte() : pngr2
                        .ReadRowsInt();
                for (int row = 0; row < pngr2.ImgInfo.Rows; row++)
                {
                    ImageLine line = lines.GetImageLineAtMatrixRow(row);
                    mirrorLine(line);
                    pngw.WriteRow(line, row);
                }
                pngr2.End();
                pngw.End();
            }
            // now check
            if (interlaced)
            {
                // Not supported yet.
                //
                //TestsHelper.testEqual(recov, origni);
                //additionalTestInterlaced(orig, origni);
            }
            else
            {
                TestsHelper.testCrcEquals(recov, crc0);
            }

            if (palete && File.Exists(truecolor))
            {
                additionalTestPalette(orig, truecolor);
            }
        }

        private static void additionalTestPalette(string orig, string truecolor)
        {
            // covnert to true color 8 bits and check equality
            PngReader pngr = FileHelper.CreatePngReader(orig);
            PngChunkPLTE plte = pngr.GetMetadata().GetPLTE();
            PngChunkTRNS trns = pngr.GetMetadata().GetTRNS();
            string copy = TestsHelper.addSuffixToName(orig, "_tccopy");
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
            pngr.End();
            pngw.End();
            TestsHelper.testEqual(copy, truecolor);
            File.Delete(copy);
        }

        private static void additionalTestInterlaced(string orig, string origni)
        {
            // tests also read/write in packed format
            PngReader pngr = FileHelper.CreatePngReader(orig);
            string copy = TestsHelper.addSuffixToName(orig, "_icopy");
            pngr.SetUnpackedMode(false);
            PngWriter pngw = FileHelper.CreatePngWriter(copy, pngr.ImgInfo, true);
            pngw.CopyChunksFirst(pngr, ChunkCopyBehaviour.COPY_ALL);
            pngw.SetUseUnPackedMode(false);
            Random random = new Random();
            bool useByte = random.NextDouble() > 0.5 && pngr.ImgInfo.BitDepth < 16;
            for (int row = 0; row < pngr.ImgInfo.Rows; row++)
            {
                if (useByte)
                {
                    ImageLine line = pngr.ReadRowByte(row);
                    pngw.WriteRow(line, row);
                }
                else
                {
                    ImageLine line = pngr.ReadRowInt(row);
                    pngw.WriteRow(line, row);
                }
            }
            pngr.End();
            pngw.End();
            TestsHelper.testEqual(copy, origni);
            File.Delete(copy);
        }

        static void mirrorLine(ImageLine imline)
        { // unpacked line !
            if (!imline.SamplesUnpacked)
                throw new PngjException("this requires unpacked lines");
            int channels = imline.ImgInfo.Channels;
            for (int c1 = 0, c2 = imline.ImgInfo.Cols - 1; c1 < c2; c1++, c2--)
            {
                for (int i = 0; i < channels; i++)
                {
                    int s1 = c1 * channels + i; // sample left
                    int s2 = c2 * channels + i; // sample right
                    if (imline.SampleType == ImageLine.ESampleType.INT)
                    {
                        int aux = imline.Scanline[s1]; // swap
                        imline.Scanline[s1] = imline.Scanline[s2];
                        imline.Scanline[s2] = aux;
                    }
                    else
                    {
                        byte auxb = imline.ScanlineB[s1]; // swap
                        imline.ScanlineB[s1] = imline.ScanlineB[s2];
                        imline.ScanlineB[s2] = auxb;
                    }
                }
            }
        }

        public static void testSingle(string orig, string origni, string truecolor)
        {
            testmirror(orig, origni, truecolor);
        }

        public static void testAllSuite(string dirsrc, string dirdest, int maxfiles)
        {
            if ((File.GetAttributes(dirsrc) & FileAttributes.Directory) != FileAttributes.Directory)
                throw new Exception("not a directory:" + dirsrc);
            int cont = 0;
            int conterr = 0;
            /* foreach file in the suite */
            foreach (string fname in Directory.GetFiles(dirsrc))
            {
                FileInfo fi = new FileInfo(fname);
                string name = fi.Name;
                if (!name.EndsWith(".png"))
                    continue;
                if (name.Contains("_ni.png") || name.Contains("_tc.png"))
                    continue; // non-interlaced version of interlaced or true color version

                try
                {
                    string orig = dirdest + "/" + name;
                    File.Copy(fname, orig, true);
                    cont++;
                    testSingle(orig, TestsHelper.addSuffixToName(fname, "_ni"),
                        TestsHelper.addSuffixToName(fname, "_tc"));
                    if (fi.Name.StartsWith('x'))
                    {
                        Console.Error.WriteLine("this should have failed! " + name);
                        conterr++;
                    }
                }
                catch (Exception e)
                {
                    if (fi.Name.StartsWith('x'))
                    { // suppposed to fail
                        Console.Out.WriteLine("ok error with " + name + " " + e.Message);
                    }
                    else
                    { // real error
                        Console.Error.WriteLine("error with " + name + " " + e.Message);
                        conterr++;
                        throw;

                    }
                }
            }

            Console.Out.WriteLine("Errors: " + conterr + "/" + cont);
            Console.Out.WriteLine("Lines starting with 'ok error' are expected errors, they are ok.");
            Console.Out.WriteLine("Output dir: " + dirdest);

            if (conterr == 0)
                Console.Out.WriteLine("=========== SUCCESS ! ================");
            else
                Console.Out.WriteLine("---- THERE WERE ERRORS!  :-((( ");
        }


    }
}
