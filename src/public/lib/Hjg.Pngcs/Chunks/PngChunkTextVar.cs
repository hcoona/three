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
    using System.Diagnostics.CodeAnalysis;
    using Hjg.Pngcs;
    /// <summary>
    /// general class for textual chunks
    /// </summary>
    public abstract class PngChunkTextVar : PngChunkMultiple
    {
        protected internal string key; // key/val: only for tEXt. lazy computed
        protected internal string val;

        protected internal PngChunkTextVar(string id, ImageInfo info)
            : base(id, info)
        {
        }

        // CA1707: Identifiers should not contain underscores
        // Justification: Keep Public API compatibility
#pragma warning disable CA1707
        public const string KEY_Title = "Title"; // Short (one line) title or caption for image
        public const string KEY_Author = "Author"; // Name of image's creator
        public const string KEY_Description = "Description"; // Description of image (possibly long)
        public const string KEY_Copyright = "Copyright"; // Copyright notice
        public const string KEY_Creation_Time = "Creation Time"; // Time of original image creation
        public const string KEY_Software = "Software"; // Software used to create the image
        public const string KEY_Disclaimer = "Disclaimer"; // Legal disclaimer
        public const string KEY_Warning = "Warning"; // Warning of nature of content
        public const string KEY_Source = "Source"; // Device used to create the image
        public const string KEY_Comment = "Comment"; // Miscellaneous comment
#pragma warning restore CA1707

        [SuppressMessage(
            "Design",
            "CA1051:Do not declare visible instance fields",
            Justification = "Allow data class.")]
        public class PngTxtInfo
        {
            public string title;
            public string author;
            public string description;
            public string creation_time;// = (new Date()).toString();
            public string software;
            public string disclaimer;
            public string warning;
            public string source;
            public string comment;
        }

        public override ChunkOrderingConstraint GetOrderingConstraint()
        {
            return ChunkOrderingConstraint.NONE;
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public string GetKey()
        {
            return key;
        }

        public string GetVal()
        {
            return val;
        }

        public void SetKeyVal(string key, string val)
        {
            this.key = key;
            this.val = val;
        }
    }
}
