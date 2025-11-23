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
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;

namespace Hjg.Pngcs.Zlib
{

    /// <summary>
    /// Zlib output (compressor) based on System.IO.Compression.ZLibStream (.NET 6+) or DeflateStream (older frameworks)
    /// </summary>
    public sealed class AZlibOutputStream : Stream
    {
        private readonly Stream rawStream;
        private readonly bool leaveOpen;
        private int compressLevel;
        private readonly EDeflateCompressStrategy strategy;

#if NET6_0_OR_GREATER
        private ZLibStream zlibStream; // lazily created, if real read/write is called
#else
        private DeflateStream deflateStream; // lazily created, if real read/write is called
        private Adler32 adler32 = new Adler32();
#endif
        private bool initdone = false;
        private bool closed = false;

        public AZlibOutputStream(Stream st, int compressLevel, EDeflateCompressStrategy strat, bool leaveOpen)
        {
            rawStream = st;
            this.leaveOpen = leaveOpen;
            this.strategy = strat;
            this.compressLevel = compressLevel;
        }

        public override void WriteByte(byte value)
        {
#if NET6_0_OR_GREATER
            if (!initdone) doInit();
            if (zlibStream == null) initStream();
            zlibStream.WriteByte(value);
#else
            if (!initdone) doInit();
            if (deflateStream == null) initStream();
            deflateStream.WriteByte(value);
            adler32.Update(value);
#endif
        }

        public override void Write(byte[] array, int offset, int count)
        {
            if (count == 0) return;
#if NET6_0_OR_GREATER
            if (!initdone) doInit();
            if (zlibStream == null) initStream();
            zlibStream.Write(array, offset, count);
#else
            if (!initdone) doInit();
            if (deflateStream == null) initStream();
            deflateStream.Write(array, offset, count);
            adler32.Update(array, offset, count);
#endif
        }

        public override void Close()
        {
#if NET6_0_OR_GREATER
            if (closed) return;
            closed = true;
            if (zlibStream != null)
            {
                zlibStream.Close();
            }
            if (!leaveOpen)
                rawStream.Close();
#else
            if (!initdone) doInit(); // can happen if never called write
            if (closed) return;
            closed = true;
            // sigh ... no only must I close the parent stream to force a flush, but I must save a reference
            // raw stream because (apparently) Close() sets it to null (shame on you, MS developers)
            if (deflateStream != null)
            {
                deflateStream.Close();
            }
            else
            {         // second hack: empty input?
                rawStream.WriteByte(3);
                rawStream.WriteByte(0);
            }
            // add crc
            uint crcv = adler32.GetValue();
            rawStream.WriteByte((byte)((crcv >> 24) & 0xFF));
            rawStream.WriteByte((byte)((crcv >> 16) & 0xFF));
            rawStream.WriteByte((byte)((crcv >> 8) & 0xFF));
            rawStream.WriteByte((byte)((crcv) & 0xFF));
            if (!leaveOpen)
                rawStream.Close();
#endif
        }

        private void initStream()
        {
#if NET6_0_OR_GREATER
            if (zlibStream != null) return;
            CompressionLevel clevel = GetCompressionLevel();
            zlibStream = new ZLibStream(rawStream, clevel, leaveOpen);
#else
            if (deflateStream != null) return;
            // I must create the DeflateStream only if necessary, because of its bug with empty input (sigh)
            // I must create with leaveopen=true always and do the closing myself, because MS moronic implementation of DeflateStream: I cant force a flush of the underlying stream witouth closing (sigh bis)
            CompressionLevel clevel = GetCompressionLevel();
            deflateStream = new DeflateStream(rawStream, clevel, true);
#endif
        }

        private CompressionLevel GetCompressionLevel()
        {
            // thanks for the granularity, MS!
            if (compressLevel >= 1 && compressLevel <= 5) return CompressionLevel.Fastest;
            else if (compressLevel == 0) return CompressionLevel.NoCompression;
            return CompressionLevel.Optimal;
        }

        private void doInit()
        {
            if (initdone) return;
            initdone = true;
#if !NET6_0_OR_GREATER
            // http://stackoverflow.com/a/2331025/277304
            int cmf = 0x78;
            int flg = 218;  // sorry about the following lines
            if (compressLevel >= 5 && compressLevel <= 6) flg = 156;
            else if (compressLevel >= 3 && compressLevel <= 4) flg = 94;
            else if (compressLevel <= 2) flg = 1;
            flg -= ((cmf * 256 + flg) % 31); // just in case
            if (flg < 0) flg += 31;
            rawStream.WriteByte((byte)cmf);
            rawStream.WriteByte((byte)flg);
#endif
        }

        public override void Flush()
        {
#if NET6_0_OR_GREATER
            if (zlibStream != null) zlibStream.Flush();
#else
            if (deflateStream != null) deflateStream.Flush();
#endif
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override bool CanTimeout
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// mainly for debugging
        /// </summary>
        /// <returns></returns>
        [SuppressMessage(
            "Performance",
            "CA1822:Mark members as static",
            Justification = "Maintain public interface consistent.")]
        public string getImplementationId()
        {
#if NET6_0_OR_GREATER
            return "Zlib deflater: .NET ZLibStream";
#else
            return "Zlib deflater: .Net CLR 4.5";
#endif
        }
    }
}
