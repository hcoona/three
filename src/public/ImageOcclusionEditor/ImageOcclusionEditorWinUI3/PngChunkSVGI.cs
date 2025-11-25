/*
 * ImageOcclusionEditorWinUI3 - A WinUI 3 application for creating image occlusion cards
 * Copyright (C) 2025 Shuai Zhang
 *
 * This file contains code derived from ImageOcclusionEditor by SuperMemo Community,
 * which is licensed under the MIT License.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;

namespace ImageOcclusionEditorWinUI3
{
    class PngChunkSVGI(ImageInfo info) : PngChunkSingle(ID, info)
    {
        // ID must follow the PNG conventions: four ascii letters,
        // ID[0] : lowercase (ancillary)
        // ID[1] : lowercase if private, upppecase if public
        // ID[3] : uppercase if "safe to copy"
        public readonly static string ID = "svGi";

        private string svg = string.Empty;

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            // change this if you don't require this chunk to be before IDAT, etc
            return ChunkOrderingConstraint.BEFORE_IDAT;
        }

        // in this case, we have that the chunk data corresponds to the serialized object
        public override ChunkRaw CreateRawChunk()
        {
            byte[] arr = Encoding.UTF8.GetBytes(svg);
            ChunkRaw c = createEmptyChunk(arr.Length, true);
            c.Data = arr;

            return c;
        }

        public override void ParseFromRaw(ChunkRaw c)
        {
            svg = Encoding.UTF8.GetString(c.Data);
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkSVGI otherx = (PngChunkSVGI)other;
            this.svg = otherx.svg; // shallow clone, we could implement other copying
        }

        public string GetSVG()
        {
            return svg;
        }

        public void SetSVG(string osvg)
        {
            this.svg = osvg;
        }

    }
}
