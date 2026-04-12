// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebHdfs.Extensions.FileProviders.Models
{
    /// <summary>
    /// File type enumeration based on WebHDFS API specification.
    /// </summary>
    internal enum WebHdfsFileType
    {
        /// <summary>
        /// Unspecified or unknown file type.
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// Regular file.
        /// </summary>
        FILE = 1,

        /// <summary>
        /// Directory.
        /// </summary>
        DIRECTORY = 2,

        /// <summary>
        /// Symbolic link.
        /// </summary>
        SYMLINK = 3,
    }

    /// <summary>
    /// Represents the status of a file or directory in WebHDFS.
    /// Only includes essential fields for maximum compatibility across HDFS versions.
    /// </summary>
    internal class WebHdfsFileStatus
    {
        /// <summary>
        /// Gets or sets the number of bytes in a file. Zero for directories.
        /// </summary>
        [JsonPropertyName("length")]
        public long Length { get; set; }

        /// <summary>
        /// Gets or sets the modification time as Unix timestamp in milliseconds.
        /// </summary>
        [JsonPropertyName("modificationTime")]
        public long ModificationTime { get; set; }

        /// <summary>
        /// Gets or sets the path suffix (relative to the requested path).
        /// May be null for some API responses.
        /// </summary>
        [JsonPropertyName("pathSuffix")]
#if !NETSTANDARD2_0 && !NET462
        public string? PathSuffix { get; set; }
#else
        public string PathSuffix { get; set; }
#endif

        /// <summary>
        /// Gets or sets the type of the path object (FILE, DIRECTORY, SYMLINK).
        /// </summary>
        [JsonPropertyName("type")]
        public WebHdfsFileType Type { get; set; }

        /// <summary>
        /// Gets an empty WebHdfsFileStatus instance for use as a default value.
        /// </summary>
        internal static WebHdfsFileStatus Empty { get; } = new WebHdfsFileStatus
        {
            PathSuffix = string.Empty,
            Type = WebHdfsFileType.Unspecified,
        };

        /// <summary>
        /// Parses a JSON string containing a FileStatus object from WebHDFS API response.
        /// </summary>
        /// <param name="json">The JSON string to parse.</param>
        /// <returns>A WebHdfsFileStatus object, or null if parsing fails.</returns>
#if !NETSTANDARD2_0 && !NET462
        internal static WebHdfsFileStatus? ParseJson(string json)
#else
        internal static WebHdfsFileStatus ParseJson(string json)
#endif
        {
            var document = JsonDocument.Parse(json);
            var fileStatusElement = document.RootElement.GetProperty("FileStatus");
            return JsonSerializer.Deserialize<WebHdfsFileStatus>(fileStatusElement.GetRawText());
        }
    }
}
