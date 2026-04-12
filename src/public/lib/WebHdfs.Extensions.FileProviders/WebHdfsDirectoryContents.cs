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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using WebHdfs.Extensions.FileProviders.Models;

namespace WebHdfs.Extensions.FileProviders
{
    /// <summary>
    /// Represents the contents of a directory in HDFS accessed through WebHDFS protocol.
    /// </summary>
    /// <remarks>
    /// This class implements <see cref="IDirectoryContents"/> to provide enumerable access
    /// to files and subdirectories within an HDFS directory via the WebHDFS REST API.
    /// </remarks>
    public class WebHdfsDirectoryContents : IDirectoryContents, IDisposable
    {
        private readonly WebHdfsFileInfo directoryInfo;
        private bool disposedValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebHdfsDirectoryContents"/> class.
        /// </summary>
        /// <param name="directoryInfo">The <see cref="WebHdfsFileInfo"/> representing the directory to enumerate.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="directoryInfo"/> is null.</exception>
        public WebHdfsDirectoryContents(WebHdfsFileInfo directoryInfo)
        {
            this.directoryInfo = directoryInfo;
        }

        /// <inheritdoc/>
        public bool Exists => this.directoryInfo.Exists;

        /// <inheritdoc/>
        public IEnumerator<IFileInfo> GetEnumerator()
        {
            var responseContent = this.directoryInfo.GetFileStatuses().GetAwaiter().GetResult();
            using (var document = JsonDocument.Parse(responseContent))
            {
                var fileStatusArray = document.RootElement
                    .GetProperty("FileStatuses")
                    .GetProperty("FileStatus");

                var fileStatuses = JsonSerializer.Deserialize<WebHdfsFileStatus[]>(fileStatusArray.GetRawText());

                return (fileStatuses ?? Array.Empty<WebHdfsFileStatus>()).Select(s => new WebHdfsFileInfo(
                    this.directoryInfo.NameNodeUri,
                    Path.Combine(this.directoryInfo.RelativePath, s.PathSuffix ?? string.Empty),
                    s) as IFileInfo).GetEnumerator();
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <param name="disposing">true if disposing; otherwise, false.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.directoryInfo.Dispose();
                }

                this.disposedValue = true;
            }
        }
    }
}
