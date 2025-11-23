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

using System;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace WebHdfs.Extensions.FileProviders
{
    /// <summary>
    /// Provides an implementation of <see cref="IFileProvider"/> for HDFS through WebHDFS protocol.
    /// This file provider allows accessing HDFS files and directories using the REST API provided by WebHDFS.
    /// </summary>
    /// <remarks>
    /// WebHDFS is a REST API to access Hadoop Distributed File System (HDFS).
    /// For more information, see: https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/WebHDFS.html.
    /// </remarks>
    public class WebHdfsFileProvider : IFileProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebHdfsFileProvider"/> class with default polling interval.
        /// </summary>
        /// <param name="nameNodeUri">The URI of the HDFS NameNode, including protocol, host and port (e.g., http://namenode:9870).</param>
        /// <example>
        /// <code>
        /// var provider = new WebHdfsFileProvider(new Uri("http://namenode:9870"));
        /// </code>
        /// </example>
        public WebHdfsFileProvider(Uri nameNodeUri)
            : this(nameNodeUri, TimeSpan.FromSeconds(5))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebHdfsFileProvider"/> class with specified polling interval.
        /// </summary>
        /// <param name="nameNodeUri">The URI of the HDFS NameNode, including protocol, host and port (e.g., http://namenode:9870).</param>
        /// <param name="defaultPollingInterval">The default interval for polling file changes when watching files.</param>
        /// <example>
        /// <code>
        /// var provider = new WebHdfsFileProvider(
        ///     new Uri("http://namenode:9870"),
        ///     TimeSpan.FromSeconds(10));
        /// </code>
        /// </example>
        public WebHdfsFileProvider(Uri nameNodeUri, TimeSpan defaultPollingInterval)
        {
            this.NameNodeUri = nameNodeUri;
            this.DefaultPollingInterval = defaultPollingInterval;
        }

        /// <summary>
        /// Gets the URI of the HDFS NameNode used for WebHDFS API calls.
        /// </summary>
        /// <value>The NameNode URI including protocol, host and port.</value>
        public Uri NameNodeUri { get; }

        /// <summary>
        /// Gets or sets the default polling interval used for file change detection.
        /// </summary>
        /// <value>The default polling interval. Default is 5 seconds.</value>
        public TimeSpan DefaultPollingInterval { get; set; }

        /// <inheritdoc/>
        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            var directoryInfo = new WebHdfsFileInfo(this.NameNodeUri, subpath);
            if (directoryInfo.Exists && directoryInfo.IsDirectory)
            {
                return new WebHdfsDirectoryContents(directoryInfo);
            }
            else
            {
                return NotFoundDirectoryContents.Singleton;
            }
        }

        /// <inheritdoc/>
        public IFileInfo GetFileInfo(string subpath)
        {
            return new WebHdfsFileInfo(this.NameNodeUri, subpath);
        }

        /// <inheritdoc/>
        public IChangeToken Watch(string filter)
        {
            return this.Watch(filter, this.DefaultPollingInterval);
        }

        /// <summary>
        /// Creates a change token for monitoring file changes with a specified polling interval.
        /// </summary>
        /// <param name="filter">The path to monitor for changes. Glob patterns are not supported.</param>
        /// <param name="pollingInterval">The interval between polling operations to detect changes.</param>
        /// <returns>A <see cref="IChangeToken"/> that represents the file change monitoring.</returns>
        /// <exception cref="NotSupportedException">Thrown when the filter contains wildcard patterns (*).</exception>
        /// <remarks>
        /// This method uses polling to detect file changes by comparing modification times.
        /// WebHDFS does not support real-time file change notifications, so polling is the only available method.
        /// Glob pattern monitoring is not supported because it would require inefficient recursive polling
        /// of all matching files through multiple HTTP requests.
        /// </remarks>
        public IChangeToken Watch(string filter, TimeSpan pollingInterval)
        {
#if !NETSTANDARD2_0 && !NET462
            if (filter.Contains('*'))
#else
            if (filter.Contains("*"))
#endif
            {
                // WebHDFS API does not provide real-time file change notifications.
                // Implementing glob pattern watching would require:
                // 1. Recursively enumerate all matching file paths (using LISTSTATUS)
                // 2. Create individual polling monitors for each file (using GETFILESTATUS)
                // 3. Periodically repeat the entire process to detect new/deleted files
                // This approach would generate excessive HTTP requests and be very inefficient for large file systems.
                throw new NotSupportedException("WebHDFS does not support efficient glob pattern watching. " +
                    "The protocol lacks real-time change notifications and would require expensive recursive polling of all matching files.");
            }
            else
            {
                return new PollingFileChangeToken(
                    (WebHdfsFileInfo)this.GetFileInfo(filter),
                    pollingInterval);
            }
        }
    }
}
