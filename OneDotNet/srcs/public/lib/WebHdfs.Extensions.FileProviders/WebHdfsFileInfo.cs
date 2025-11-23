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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using WebHdfs.Extensions.FileProviders.Models;

namespace WebHdfs.Extensions.FileProviders
{
    /// <summary>
    /// Provides information about a file or directory in HDFS accessed through WebHDFS protocol.
    /// </summary>
    /// <remarks>
    /// This class implements <see cref="IFileInfo"/> to provide file metadata and read access
    /// to files stored in Hadoop Distributed File System (HDFS) via the WebHDFS REST API.
    /// </remarks>
    public class WebHdfsFileInfo : IFileInfo, IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly UriBuilder fileWebHdfsUriBuilder;
#if !NETSTANDARD2_0 && !NET462
        private WebHdfsFileStatus? fileStatus;
#else
        private WebHdfsFileStatus fileStatus;
#endif
        private bool disposedValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebHdfsFileInfo"/> class.
        /// </summary>
        /// <param name="nameNodeUri">The URI of the HDFS NameNode.</param>
        /// <param name="relativePath">The relative path of the file or directory in HDFS.</param>
        /// <example>
        /// <code>
        /// var fileInfo = new WebHdfsFileInfo(
        ///     new Uri("http://namenode:9870"),
        ///     "/user/data/file.txt");
        /// </code>
        /// </example>
        public WebHdfsFileInfo(Uri nameNodeUri, string relativePath)
        {
            this.NameNodeUri = nameNodeUri;
            this.RelativePath = relativePath;
            this.httpClient = new HttpClient();
            this.fileWebHdfsUriBuilder =
                new UriBuilder(new Uri(
                    this.NameNodeUri,
                    $"/webhdfs/v1/{this.RelativePath.Trim('/')}"));

            this.Refresh();
        }

        internal WebHdfsFileInfo(Uri nameNodeUri, string relativePath, WebHdfsFileStatus fileStatus)
        {
            this.NameNodeUri = nameNodeUri;
            this.RelativePath = relativePath;
            this.httpClient = new HttpClient();
            this.fileWebHdfsUriBuilder =
                new UriBuilder(new Uri(
                    this.NameNodeUri,
                    $"/webhdfs/v1/{this.RelativePath.Trim('/')}"));

            this.SetFileStatus(fileStatus);
        }

        /// <summary>
        /// Gets the URI of the HDFS NameNode.
        /// </summary>
        /// <value>The NameNode URI used for WebHDFS API calls.</value>
        public Uri NameNodeUri { get; }

        /// <summary>
        /// Gets the relative path of the file or directory in HDFS.
        /// </summary>
        /// <value>The relative path from the HDFS root.</value>
        public string RelativePath { get; }

        /// <inheritdoc/>
        public bool Exists { get; private set; }

        /// <inheritdoc/>
        public long Length => this.fileStatus?.Length ?? 0;

        /// <inheritdoc/>
#if !NETSTANDARD2_0 && !NET462
        public string? PhysicalPath => null;
#else
        public string PhysicalPath => null;
#endif

        /// <inheritdoc/>
        public string Name => Path.GetFileName(this.RelativePath);

        /// <inheritdoc/>
        public DateTimeOffset LastModified =>
            DateTimeOffset.FromUnixTimeMilliseconds(this.fileStatus?.ModificationTime ?? 0);

        /// <inheritdoc/>
        public bool IsDirectory => this.fileStatus?.Type == WebHdfsFileType.DIRECTORY;

        /// <inheritdoc/>
        public Stream CreateReadStream()
        {
            if (this.IsDirectory)
            {
                throw new InvalidOperationException(
                    "You cannot create read stream against a directory.");
            }

            this.fileWebHdfsUriBuilder.Query = "OP=OPEN";
            return this.httpClient.GetStreamAsync(this.fileWebHdfsUriBuilder.Uri).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Refreshes the file or directory information by querying the HDFS NameNode.
        /// </summary>
        /// <remarks>
        /// This method makes a WebHDFS API call to get the current file status information.
        /// If the file or directory does not exist, the <see cref="Exists"/> property will be set to false.
        /// </remarks>
        public void Refresh()
        {
            try
            {
                this.SetFileStatus(WebHdfsFileStatus.ParseJson(this.GetFileStatus().GetAwaiter().GetResult()));
            }
            catch (AggregateException ex) when (ex.InnerException is FileNotFoundException)
            {
#if !NETSTANDARD2_0 && !NET462
                this.SetFileStatus(null);
#else
                this.SetFileStatus(WebHdfsFileStatus.Empty);
#endif
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        internal async Task<string> GetFileStatuses()
        {
            this.fileWebHdfsUriBuilder.Query = "OP=LISTSTATUS";
            var response = await this.httpClient.GetAsync(this.fileWebHdfsUriBuilder.Uri)
                .ConfigureAwait(false);

            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return responseContent;
            }
            else
            {
                var errorResponse = JsonSerializer.Deserialize<WebHdfsErrorResponse>(responseContent);
                string message = errorResponse?.RemoteException?.Message ?? "Unknown WebHDFS error";
                switch (response.StatusCode)
                {
                    case HttpStatusCode.BadRequest:
                        throw new ArgumentException(message);
                    case HttpStatusCode.Unauthorized:
                        throw new System.Security.SecurityException(message);
                    case HttpStatusCode.Forbidden:
                        throw new IOException(message);
                    case HttpStatusCode.NotFound:
                        throw new FileNotFoundException(message, this.Name);
                    case HttpStatusCode.InternalServerError:
                        throw new InvalidOperationException(message);
                    default:
                        throw new InvalidOperationException(message);
                }
            }
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
                    this.httpClient.Dispose();
                }

                this.disposedValue = true;
            }
        }

#if !NETSTANDARD2_0 && !NET462
        private void SetFileStatus(WebHdfsFileStatus? fileStatus)
#else
        private void SetFileStatus(WebHdfsFileStatus fileStatus)
#endif
        {
            if (fileStatus == null || fileStatus == WebHdfsFileStatus.Empty)
            {
                this.Exists = false;
                this.fileStatus = WebHdfsFileStatus.Empty;
            }
            else
            {
                this.Exists = true;
                this.fileStatus = fileStatus;
            }
        }

        private async Task<string> GetFileStatus()
        {
            this.fileWebHdfsUriBuilder.Query = "OP=GETFILESTATUS";
            var response = await this.httpClient.GetAsync(this.fileWebHdfsUriBuilder.Uri)
                .ConfigureAwait(false);

            var responseContent = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return responseContent;
            }
            else
            {
                var errorResponse = JsonSerializer.Deserialize<WebHdfsErrorResponse>(responseContent);
                string message = errorResponse?.RemoteException?.Message ?? "Unknown WebHDFS error";
                switch (response.StatusCode)
                {
                    case HttpStatusCode.BadRequest:
                        throw new ArgumentException(message);
                    case HttpStatusCode.Unauthorized:
                        throw new System.Security.SecurityException(message);
                    case HttpStatusCode.Forbidden:
                        throw new IOException(message);
                    case HttpStatusCode.NotFound:
                        throw new FileNotFoundException(message, this.Name);
                    case HttpStatusCode.InternalServerError:
                        throw new InvalidOperationException(message);
                    default:
                        throw new InvalidOperationException(message);
                }
            }
        }
    }
}
