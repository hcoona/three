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

namespace Hjg.Pngcs.Chunks
{

    using System;
    using Hjg.Pngcs;

    /// <summary>
    /// tIME chunk: http://www.w3.org/TR/PNG/#11tIME
    /// </summary>
    public class PngChunkTIME : PngChunkSingle
    {
        public const string ID = ChunkHelper.tIME;

        private int year, mon, day, hour, min, sec;

        public PngChunkTIME(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.NONE;
        }

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = createEmptyChunk(7, true);
            PngHelperInternal.WriteInt2tobytes(year, c.Data, 0);
            c.Data[2] = (byte)mon;
            c.Data[3] = (byte)day;
            c.Data[4] = (byte)hour;
            c.Data[5] = (byte)min;
            c.Data[6] = (byte)sec;
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            if (c.Len != 7)
                throw new PngjException("bad chunk " + c);
            year = PngHelperInternal.ReadInt2fromBytes(c.Data, 0);
            mon = PngHelperInternal.ReadInt1fromByte(c.Data, 2);
            day = PngHelperInternal.ReadInt1fromByte(c.Data, 3);
            hour = PngHelperInternal.ReadInt1fromByte(c.Data, 4);
            min = PngHelperInternal.ReadInt1fromByte(c.Data, 5);
            sec = PngHelperInternal.ReadInt1fromByte(c.Data, 6);
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkTIME x = (PngChunkTIME)other;
            year = x.year;
            mon = x.mon;
            day = x.day;
            hour = x.hour;
            min = x.min;
            sec = x.sec;
        }

        public void SetNow(int secsAgo)
        {
            DateTime d1 = DateTime.Now;
            year = d1.Year;
            mon = d1.Month;
            day = d1.Day;
            hour = d1.Hour;
            min = d1.Minute;
            sec = d1.Second;
        }

        internal void SetYMDHMS(int yearx, int monx, int dayx, int hourx, int minx, int secx)
        {
            year = yearx;
            mon = monx;
            day = dayx;
            hour = hourx;
            min = minx;
            sec = secx;
        }

        public int[] GetYMDHMS()
        {
            return new int[] { year, mon, day, hour, min, sec };
        }

        /** format YYYY/MM/DD HH:mm:SS */
        public string GetAsString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:D4}/{1:D2}/{2:D2} {3:D2}:{4:D2}:{5:D2}",
                year, mon, day, hour, min, sec);
        }

    }
}
