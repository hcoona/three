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

namespace Microsoft.Extensions.Logging.MSTest
{
    /// <summary>
    /// Provides logger instances for MSTest framework integration.
    /// </summary>
    /// <remarks>
    /// This provider creates logger instances that output log messages through the MSTest framework,
    /// making them visible in test results and output windows.
    /// </remarks>
    public sealed class MsTestLoggerProvider : ILoggerProvider
    {
        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="categoryName"/> is null.</exception>
        public ILogger CreateLogger(string categoryName)
        {
            if (categoryName is null)
            {
                throw new ArgumentNullException(nameof(categoryName));
            }

            return new MsTestLogger(categoryName);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // No resources to dispose
        }
    }
}
