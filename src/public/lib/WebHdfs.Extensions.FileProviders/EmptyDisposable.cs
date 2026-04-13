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

namespace WebHdfs.Extensions.FileProviders
{
    /// <summary>
    /// Provides a reusable empty implementation of <see cref="IDisposable"/> that performs no operations.
    /// </summary>
    /// <remarks>
    /// This class is used as a placeholder for scenarios where an <see cref="IDisposable"/> is required
    /// but no actual cleanup is needed, such as in the <see cref="PollingFileChangeToken.RegisterChangeCallback"/> method.
    /// </remarks>
    internal sealed class EmptyDisposable : IDisposable
    {
        private EmptyDisposable()
        {
        }

        /// <summary>
        /// Gets a singleton instance of the <see cref="EmptyDisposable"/> class.
        /// </summary>
        /// <value>A reusable instance that can be returned when no disposal action is needed.</value>
        public static EmptyDisposable Instance { get; } = new EmptyDisposable();

        /// <inheritdoc/>
        public void Dispose()
        {
            // No operation needed
        }
    }
}
