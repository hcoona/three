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
using Xunit.Abstractions;

namespace Microsoft.Extensions.Logging.Xunit
{
    /// <summary>
    /// Logger provider that creates loggers which output to xUnit test output.
    /// </summary>
    public sealed class XunitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper testOutputHelper;

        /// <summary>
        /// Initializes a new instance of the <see cref="XunitLoggerProvider"/> class.
        /// </summary>
        /// <param name="testOutputHelper">
        /// The xUnit test output helper used to write log messages.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="testOutputHelper"/> is null.
        /// </exception>
        public XunitLoggerProvider(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper
                ?? throw new ArgumentNullException(nameof(testOutputHelper));
        }

        /// <summary>
        /// Creates a new logger instance for the specified category.
        /// </summary>
        /// <param name="categoryName">
        /// The name of the category for which to create the logger.
        /// </param>
        /// <returns>A new <see cref="XunitLogger"/> instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="categoryName"/> is null.
        /// </exception>
        public ILogger CreateLogger(string categoryName)
        {
            if (categoryName is null)
            {
                throw new ArgumentNullException(nameof(categoryName));
            }

            return new XunitLogger(this.testOutputHelper, categoryName);
        }

        /// <summary>
        /// Disposes the logger provider. Since this provider doesn't hold any unmanaged resources,
        /// this method is intentionally left empty.
        /// </summary>
        public void Dispose()
        {
            // No resources to dispose for this implementation
        }
    }
}
