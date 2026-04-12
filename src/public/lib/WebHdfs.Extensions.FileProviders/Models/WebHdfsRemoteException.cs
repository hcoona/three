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

using System.Text.Json.Serialization;

namespace WebHdfs.Extensions.FileProviders.Models
{
    /// <summary>
    /// Represents the remote exception details from a WebHDFS error response.
    /// </summary>
    /// <remarks>
    /// Contains only the essential 'message' property as specified in the WebHDFS documentation.
    /// The 'exception' and 'javaClassName' properties are optional and omitted for compatibility.
    /// </remarks>
    internal class WebHdfsRemoteException
    {
        /// <summary>
        /// Gets or sets the exception message.
        /// </summary>
        /// <remarks>
        /// This is the primary error message returned by the WebHDFS server.
        /// </remarks>
        [JsonPropertyName("message")]
#if !NETSTANDARD2_0 && !NET462
        public string? Message { get; set; }
#else
        public string Message { get; set; }
#endif
    }
}
