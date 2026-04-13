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
    /// Represents a WebHDFS error response according to the official WebHDFS REST API specification.
    /// </summary>
    /// <remarks>
    /// Based on the RemoteException JSON Schema from:
    /// https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#RemoteException_JSON_Schema
    ///
    /// Only includes essential properties for maximum compatibility across HDFS versions.
    /// </remarks>
    internal class WebHdfsErrorResponse
    {
        /// <summary>
        /// Gets or sets the remote exception details.
        /// </summary>
        [JsonPropertyName(nameof(RemoteException))]
#if !NETSTANDARD2_0 && !NET462
        public WebHdfsRemoteException? RemoteException { get; set; }
#else
        public WebHdfsRemoteException RemoteException { get; set; }
#endif
    }
}
