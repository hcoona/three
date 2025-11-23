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

namespace Hjg.Pngcs
{
    using System.IO;

    /// <summary>
    /// A few utility static methods to read and write files
    /// </summary>
    ///
    public class FileHelper
    {

        public static Stream OpenFileForReading(string file)
        {
            Stream isx = null;
            if (file == null || !File.Exists(file))
                throw new PngjInputException("Cannot open file for reading (" + file + ")");
            isx = new FileStream(file, FileMode.Open);
            return isx;
        }

        public static Stream OpenFileForWriting(string file, bool allowOverwrite)
        {
            Stream osx = null;
            if (File.Exists(file) && !allowOverwrite)
                throw new PngjOutputException("File already exists (" + file + ") and overwrite=false");
            osx = new FileStream(file, FileMode.Create);
            return osx;
        }

        /// <summary>
        /// Given a filename and a ImageInfo, produces a PngWriter object, ready for writing.</summary>
        /// <param name="fileName">Path of file</param>
        /// <param name="imgInfo">ImageInfo object</param>
        /// <param name="allowOverwrite">Flag: if false and file exists, a PngjOutputException is thrown</param>
        /// <returns>A PngWriter object, ready for writing</returns>
        public static PngWriter CreatePngWriter(string fileName, ImageInfo imgInfo, bool allowOverwrite)
        {
            return new PngWriter(OpenFileForWriting(fileName, allowOverwrite), imgInfo,
                    fileName);
        }

        /// <summary>
        /// Given a filename, produces a PngReader object, ready for reading.
        /// </summary>
        /// <param name="fileName">Path of file</param>
        /// <returns>PngReader, ready for reading</returns>
        public static PngReader CreatePngReader(string fileName)
        {
            return new PngReader(OpenFileForReading(fileName), fileName);
        }
    }
}
