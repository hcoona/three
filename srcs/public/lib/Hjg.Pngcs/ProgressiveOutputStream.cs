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

    using System;
    using System.IO;

    /// <summary>
    /// stream that outputs to memory and allows to flush fragments every 'size'
    /// bytes to some other destination
    /// </summary>
    ///
    abstract internal class ProgressiveOutputStream : MemoryStream
    {
        private readonly int size;
        private long countFlushed = 0;

        public ProgressiveOutputStream(int size_0)
        {
            this.size = size_0;
            if (size < 8) throw new PngjException("bad size for ProgressiveOutputStream: " + size);
        }

        public override void Close()
        {
            Flush();
            base.Close();
        }

        public override void Flush()
        {
            base.Flush();
            CheckFlushBuffer(true);
        }

        public override void Write(byte[] b, int off, int len)
        {
            base.Write(b, off, len);
            CheckFlushBuffer(false);
        }

        public void Write(byte[] b)
        {
            Write(b, 0, b.Length);
            CheckFlushBuffer(false);
        }


        /// <summary>
        /// if it's time to flush data (or if forced==true) calls abstract method
        /// flushBuffer() and cleans those bytes from own buffer
        /// </summary>
        ///
        private void CheckFlushBuffer(bool forced)
        {
            int count = (int)Position;
            byte[] buf = GetBuffer();
            while (forced || count >= size)
            {
                int nb = size;
                if (nb > count)
                    nb = count;
                if (nb == 0)
                    return;
                FlushBuffer(buf, nb);
                countFlushed += nb;
                int bytesleft = count - nb;
                count = bytesleft;
                Position = count;
                if (bytesleft > 0)
                    Array.Copy((Array)(buf), nb, (Array)(buf), 0, bytesleft);
            }
        }

        protected abstract void FlushBuffer(byte[] b, int n);

        public long GetCountFlushed()
        {
            return countFlushed;
        }
    }
}
