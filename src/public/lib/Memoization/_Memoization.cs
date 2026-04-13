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
using Microsoft.Extensions.Caching.Memory;

namespace Memoization
{
    public static partial class Memoization
    {
        /// <summary>
        /// Gets or sets the default memory cache instance used by memoization methods that don't specify a cache.
        /// </summary>
        /// <value>
        /// The default <see cref="IMemoryCache"/> instance. Must be set before calling any Create methods
        /// that rely on the default cache, otherwise an <see cref="InvalidOperationException"/> will be thrown.
        /// </value>
        /// <remarks>
        /// This property should be initialized once during application startup, typically with a
        /// <c>MemoryCache</c> instance configured with appropriate <c>MemoryCacheOptions</c>.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Initialize during application startup
        /// Memoization.DefaultCache = new MemoryCache(new MemoryCacheOptions
        /// {
        ///     SizeLimit = 1000,
        ///     CompactionPercentage = 0.25
        /// });
        /// </code>
        /// </example>
#if !NETSTANDARD2_0 && !NET462
        public static IMemoryCache? DefaultCache { get; set; }
#else
        public static IMemoryCache DefaultCache { get; set; }
#endif
    }
}
