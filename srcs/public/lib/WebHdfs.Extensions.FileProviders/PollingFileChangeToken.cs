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
using Microsoft.Extensions.Primitives;

namespace WebHdfs.Extensions.FileProviders
{
    internal class PollingFileChangeToken : IChangeToken
    {
        private readonly WebHdfsFileInfo fileInfo;
        private readonly TimeSpan pollingInterval;
        private DateTime previousWriteTimeUtc;
        private DateTime lastCheckedTimeUtc;
        private bool hasChanged;

        public PollingFileChangeToken(WebHdfsFileInfo fileInfo, TimeSpan pollingInterval)
        {
            this.fileInfo = fileInfo;
            this.pollingInterval = pollingInterval;
            this.previousWriteTimeUtc = this.GetLastWriteTimeUtc();
        }

        public bool HasChanged
        {
            get
            {
                if (this.hasChanged)
                {
                    return this.hasChanged;
                }

                var currentTime = DateTime.UtcNow;
                if (currentTime - this.lastCheckedTimeUtc < this.pollingInterval)
                {
                    return this.hasChanged;
                }

                var lastWriteTimeUtc = this.GetLastWriteTimeUtc();
                if (this.previousWriteTimeUtc != lastWriteTimeUtc)
                {
                    this.previousWriteTimeUtc = lastWriteTimeUtc;
                    this.hasChanged = true;
                }

                this.lastCheckedTimeUtc = currentTime;
                return this.hasChanged;
            }
        }

        public bool ActiveChangeCallbacks => false;

#if !NETSTANDARD2_0 && !NET462
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
#else
        public IDisposable RegisterChangeCallback(Action<object> callback, object state)
#endif
        {
            return EmptyDisposable.Instance;
        }

        private DateTime GetLastWriteTimeUtc()
        {
            this.fileInfo.Refresh();
            return this.fileInfo.Exists
                ? this.fileInfo.LastModified.UtcDateTime
                : DateTime.MinValue;
        }
    }
}
