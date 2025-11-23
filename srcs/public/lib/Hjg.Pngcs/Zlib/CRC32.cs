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

    public class CRC32
    { // based on http://damieng.com/blog/2006/08/08/calculating_crc32_in_c_and_net

        private const uint defaultPolynomial = 0xedb88320;
        private const uint defaultSeed = 0xffffffff;
        private static uint[] defaultTable;

        private uint hash;
        private uint seed;
        private uint[] table;

        public CRC32()
            : this(defaultPolynomial, defaultSeed)
        {
        }

        public CRC32(uint polynomial, uint seed)
        {
            table = InitializeTable(polynomial);
            this.seed = seed;
            this.hash = seed;
        }

        public void Update(byte[] buffer)
        {
            Update(buffer, 0, buffer.Length);
        }

        public void Update(byte[] buffer, int start, int length)
        {
            for (int i = 0, j = start; i < length; i++, j++)
            {
                unchecked
                {
                    hash = (hash >> 8) ^ table[buffer[j] ^ hash & 0xff];
                }
            }
        }

        public uint GetValue()
        {
            return ~hash;
        }

        public void Reset()
        {
            this.hash = seed;
        }

        private static uint[] InitializeTable(uint polynomial)
        {
            if (polynomial == defaultPolynomial && defaultTable != null)
                return defaultTable;
            uint[] createTable = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                uint entry = (uint)i;
                for (int j = 0; j < 8; j++)
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ polynomial;
                    else
                        entry = entry >> 1;
                createTable[i] = entry;
            }
            if (polynomial == defaultPolynomial)
                defaultTable = createTable;
            return createTable;
        }

    }
}
