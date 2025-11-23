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
    /// Zlib input (decompressor) based on System.IO.Compression.ZLibStream (.NET 6+) or DeflateStream (older frameworks)
    /// </summary>
    public sealed class AZlibInputStream : Stream
    {
        private readonly Stream rawStream;
        private readonly bool leaveOpen;

#if NET6_0_OR_GREATER
        private ZLibStream zlibStream; // lazily created, if real read is called
#else
        private DeflateStream deflateStream; // lazily created, if real read is called
#endif
        private bool initdone = false;
        private bool closed = false;

#if !NET6_0_OR_GREATER
        // Legacy implementation fields for .NET Standard 2.0/2.1
        private bool fdict;// merely informational, not used
        private int cmdinfo;// merely informational, not used
        private byte[] dictid; // merely informational, not used
        private byte[] crcread = null; // merely informational, not checked
#endif

        public AZlibInputStream(Stream st, bool leaveOpen)
        {
            rawStream = st;
            this.leaveOpen = leaveOpen;
        }

        public override int Read(byte[] array, int offset, int count)
        {
#if NET6_0_OR_GREATER
            if (!initdone) doInit();
            if (zlibStream == null && count > 0) initStream();
            return zlibStream.Read(array, offset, count);
#else
            if (!initdone) doInit();
            if (deflateStream == null && count > 0) initStream();
            // we dont't check CRC on reading
            int r = deflateStream.Read(array, offset, count);
            if (r < 1 && crcread == null)
            {  // deflater has ended. we try to read next 4 bytes from raw stream (crc)
                crcread = new byte[4];
                for (int i = 0; i < 4; i++) crcread[i] = (byte)rawStream.ReadByte(); // we dont really check/use this
            }
            return r;
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
            if (deflateStream != null)
            {
                deflateStream.Close();
            }
            if (crcread == null)
            { // eat trailing 4 bytes
                crcread = new byte[4];
                for (int i = 0; i < 4; i++) crcread[i] = (byte)rawStream.ReadByte();
            }
            if (!leaveOpen)
                rawStream.Close();
#endif
        }

        private void initStream()
        {
#if NET6_0_OR_GREATER
            if (zlibStream != null) return;
            zlibStream = new ZLibStream(rawStream, CompressionMode.Decompress, leaveOpen);
#else
            if (deflateStream != null) return;
            deflateStream = new DeflateStream(rawStream, CompressionMode.Decompress, true);
#endif
        }

        private void doInit()
        {
            if (initdone) return;
            initdone = true;
#if !NET6_0_OR_GREATER
            // read zlib header : http://www.ietf.org/rfc/rfc1950.txt
            int cmf = rawStream.ReadByte();
            int flag = rawStream.ReadByte();
            if (cmf == -1 || flag == -1) return;
            if ((cmf & 0x0f) != 8) throw new PngjInputException("Bad compression method for ZLIB header: cmf=" + cmf);
            cmdinfo = ((cmf & (0xf0)) >> 8);// not used?
            fdict = (flag & 32) != 0;
            if (fdict)
            {
                dictid = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    dictid[i] = (byte)rawStream.ReadByte(); // we eat but don't use this
                }
            }
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

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return false; }
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

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
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
            return "Zlib inflater: .NET ZLibStream";
#else
            return "Zlib inflater: .Net CLR 4.5";
#endif
        }
    }
}
