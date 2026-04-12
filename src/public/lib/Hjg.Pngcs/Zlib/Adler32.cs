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

namespace Hjg.Pngcs.Zlib
{

    public class Adler32
    {
        private uint a = 1;
        private uint b = 0;
        private const int _base = 65521; /* largest prime smaller than 65536 */
        private const int _nmax = 5550;
        private int pend = 0; // how many bytes have I read witouth computing modulus


        public void Update(byte data)
        {
            if (pend >= _nmax) updateModulus();
            a += data;
            b += a;
            pend++;
        }

        public void Update(byte[] data)
        {
            Update(data, 0, data.Length);
        }

        public void Update(byte[] data, int offset, int length)
        {
            int nextJToComputeModulus = _nmax - pend;
            for (int j = 0; j < length; j++)
            {
                if (j == nextJToComputeModulus)
                {
                    updateModulus();
                    nextJToComputeModulus = j + _nmax;
                }
                unchecked
                {
                    a += data[j + offset];
                }
                b += a;
                pend++;
            }
        }

        public void Reset()
        {
            a = 1;
            b = 0;
            pend = 0;
        }

        private void updateModulus()
        {
            a %= _base;
            b %= _base;
            pend = 0;
        }

        public uint GetValue()
        {
            if (pend > 0) updateModulus();
            return (b << 16) | a;
        }
    }
}
