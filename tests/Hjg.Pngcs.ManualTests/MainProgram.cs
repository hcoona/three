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
using SampleTests;


namespace SamplesTests
{

    class MainProgram
    {
        internal static readonly string[] testSuiteArgs = ["testsuite1/", "testcs/"];

        static void Main(string[] args)
        {
            Console.WriteLine("Starting all tests...");
            long t0 = Environment.TickCount;
            System.IO.Directory.CreateDirectory("temp");
            System.IO.Directory.CreateDirectory("testcs");
            //testX();
            myTestSuite();
            //testTextChunks();
            long t1 = Environment.TickCount;

            Console.Out.WriteLine("Done. (" + (t1 - t0) + " msecs) " + "Net version: " + Environment.Version + " Press ENTER to close");
            Console.In.ReadLine();
        }

        static void myTestSuite()
        {
            testSuite(testSuiteArgs);
        }

        /// <summary>
        /// textual chunks
        /// </summary>
        static void testTextChunks()
        {
            TestTextChunks.test();
        }


        static void sampleShowChunks(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("expected [inputfile]");
                return;
            }
            SampleShowChunks.showChunks(args[0]);
        }

        static void sampleConvertTrueColor(string file)
        {
            SampleConvertToTrueCol.doit(file);
        }


        static void sampleMirror(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("expected [inputfile] [outputfile]");
                return;
            }
            SampleMirrorImage.mirror(args[0], args[1]);
            Console.Out.WriteLine("sampleMirror done " + args[0] + " ->" + args[1]);
        }

        static void decreaseRed(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("expected [inputfile] [outputfile]");
                return;
            }
            SampleDecreaseRed.DecreaseRed(args[0], args[1]);
            Console.Out.WriteLine("decreaseRed done " + args[0] + " ->" + args[1]);
        }

        static void customChunk(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("expected [inputfile] [outputfile]");
                return;
            }
            Console.Out.WriteLine("custom chunk write : " + args[0] + " ->" + args[1]);
            SampleCustomChunk.testWrite(args[0], args[1]);
            Console.Out.WriteLine("custom chunk read: " + args[1]);
            SampleCustomChunk.testRead(args[1]);
        }

        static void testSingle(string file)
        {
            TestPngSuite.testSingle(file, null, null);
        }


        static void testSuite(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("expected [origdir] [destdir] [maxfiles]");
                return;
            }
            int maxfiles = args.Length < 3 ? 0 : int.Parse(args[2]);
            TestPngSuite.testAllSuite(args[0], args[1], maxfiles);
        }

    }
}
