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
    using System.IO;
    using Hjg.Pngcs;

    /// <summary>
    /// iCCP Chunk: see http://www.w3.org/TR/PNG/#11iCCP
    /// </summary>
    public class PngChunkICCP : PngChunkSingle
    {
        public const string ID = ChunkHelper.iCCP;

        private string profileName;

        private byte[] compressedProfile;

        public PngChunkICCP(ImageInfo info)
            : base(ID, info)
        {
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.BEFORE_PLTE_AND_IDAT;
        }


        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = createEmptyChunk(profileName.Length + compressedProfile.Length + 2, true);
            Array.Copy((Array)(ChunkHelper.ToBytes(profileName)), 0, (Array)(c.Data), 0, profileName.Length);
            c.Data[profileName.Length] = 0;
            c.Data[profileName.Length + 1] = 0;
            Array.Copy((Array)(compressedProfile), 0, (Array)(c.Data), profileName.Length + 2, compressedProfile.Length);
            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            int pos0 = ChunkHelper.PosNullByte(c.Data);
            profileName = PngHelperInternal.charsetLatin1.GetString(c.Data, 0, pos0);
            int comp = (c.Data[pos0 + 1] & 0xff);
            if (comp != 0)
                throw new PngjInputException("bad compression for ChunkTypeICCP");
            int compdatasize = c.Data.Length - (pos0 + 2);
            compressedProfile = new byte[compdatasize];
            Array.Copy((Array)(c.Data), pos0 + 2, (Array)(compressedProfile), 0, compdatasize);
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkICCP otherx = (PngChunkICCP)other;
            profileName = otherx.profileName;
            compressedProfile = new byte[otherx.compressedProfile.Length];
            Array.Copy(otherx.compressedProfile, compressedProfile, compressedProfile.Length);

        }

        /// <summary>
        /// Sets profile name and profile
        /// </summary>
        /// <param name="name">profile name </param>
        /// <param name="profile">profile (latin1 string)</param>
        public void SetProfileNameAndContent(string name, string profile)
        {
            SetProfileNameAndContent(name, ChunkHelper.ToBytes(profileName));
        }

        /// <summary>
        /// Sets profile name and profile
        /// </summary>
        /// <param name="name">profile name </param>
        /// <param name="profile">profile (uncompressed)</param>
        public void SetProfileNameAndContent(string name, byte[] profile)
        {
            profileName = name;
            compressedProfile = ChunkHelper.compressBytes(profile, true);
        }


        public string GetProfileName()
        {
            return profileName;
        }

        /// <summary>
        /// This uncompresses the string!
        /// </summary>
        /// <returns></returns>
        public byte[] GetProfile()
        {
            return ChunkHelper.compressBytes(compressedProfile, false);
        }

        public string GetProfileAsString()
        {
            return ChunkHelper.ToString(GetProfile());
        }

    }
}
