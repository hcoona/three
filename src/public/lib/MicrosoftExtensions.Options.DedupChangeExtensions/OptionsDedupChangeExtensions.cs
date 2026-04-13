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
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Threading;

namespace Microsoft.Extensions.Options
{
    /// <summary>
    /// Provides extension methods for IOptionsMonitor to enable deduplication of option change notifications.
    /// Only triggers the listener when the option values have actually changed, preventing redundant notifications.
    /// </summary>
    public static class OptionsDedupChangeExtensions
    {
        // BinaryFormatter is used to serialize objects for hash comparison
        private static readonly ThreadLocal<IFormatter> FormatterLocal =
            new ThreadLocal<IFormatter>(() => new BinaryFormatter(), trackAllValues: false);

        // SHA1 provides sufficient hash quality for deduplication purposes
        private static readonly ThreadLocal<HashAlgorithm> HashAlgorithmLocal =
            new ThreadLocal<HashAlgorithm>(SHA1.Create, trackAllValues: false);

        /// <summary>
        /// Registers a change callback that only fires when the option values have actually changed.
        /// Uses hash-based comparison to detect changes and prevent duplicate notifications.
        /// </summary>
        /// <typeparam name="TOptions">The type of options being monitored.</typeparam>
        /// <param name="monitor">The options monitor to extend.</param>
        /// <param name="name">The name of the named options instance to monitor.</param>
        /// <param name="listener">The callback to invoke when options change.</param>
        /// <returns>An IDisposable that removes the change callback when disposed.</returns>
#if !NETSTANDARD2_0 && !NET462
        public static IDisposable? OnChangeDedup<TOptions>(
#else
        public static IDisposable OnChangeDedup<TOptions>(
#endif
            this IOptionsMonitor<TOptions> monitor,
            string name,
            Action<TOptions, string> listener)
        {
            var originValueHashToken = GetHashToken(monitor.Get(name));
            return monitor.OnChange((newValue, key) =>
            {
                if (key == name)
                {
                    var newValueHashToken = GetHashToken(newValue);
                    var oldValueHashToken = Interlocked.Exchange(
                        ref originValueHashToken,
                        newValueHashToken);

                    // Only invoke listener if the hash has actually changed
                    if (!IsHashTokenEqual(oldValueHashToken, newValueHashToken))
                    {
                        listener(newValue, key);
                    }
                }
            });
        }

        /// <summary>
        /// Registers a change callback that only fires when the default option values have actually changed.
        /// This is a convenience overload that monitors the default named options instance.
        /// </summary>
        /// <typeparam name="TOptions">The type of options being monitored.</typeparam>
        /// <param name="monitor">The options monitor to extend.</param>
        /// <param name="listener">The callback to invoke when options change.</param>
        /// <returns>An IDisposable that removes the change callback when disposed.</returns>
#if !NETSTANDARD2_0 && !NET462
        public static IDisposable? OnChangeDedup<TOptions>(
#else
        public static IDisposable OnChangeDedup<TOptions>(
#endif
            this IOptionsMonitor<TOptions> monitor,
            Action<TOptions> listener)
        {
            // Delegate to the named overload using the default options name
            return OnChangeDedup(
                monitor,
                Options.DefaultName,
                (options, _) => listener(options));
        }

        /// <summary>
        /// Computes a hash token for the given object to enable change detection.
        /// Uses binary serialization followed by SHA1 hashing for consistent comparison.
        /// </summary>
        /// <param name="graph">The object to hash, can be null.</param>
        /// <returns>A byte array representing the hash of the serialized object.</returns>
#if !NETSTANDARD2_0 && !NET462
        private static byte[] GetHashToken(object? graph)
#else
        private static byte[] GetHashToken(object graph)
#endif
        {
            // Handle null case by returning a consistent hash for null values
            if (graph is null)
            {
#if !NETSTANDARD2_0 && !NET462
                return HashAlgorithmLocal.Value!.ComputeHash(Array.Empty<byte>());
#else
                return HashAlgorithmLocal.Value.ComputeHash(Array.Empty<byte>());
#endif
            }

            // Serialize the object to a memory stream, then compute its hash
            using (var stream = new MemoryStream())
            {
#if !NETSTANDARD2_0 && !NET462
                FormatterLocal.Value!.Serialize(stream, graph);
                stream.Seek(0, SeekOrigin.Begin);
                return HashAlgorithmLocal.Value!.ComputeHash(stream);
#else
                FormatterLocal.Value.Serialize(stream, graph);
                stream.Seek(0, SeekOrigin.Begin);
                return HashAlgorithmLocal.Value.ComputeHash(stream);
#endif
            }
        }

        /// <summary>
        /// Compares two hash tokens for equality using constant-time comparison.
        /// This prevents timing attacks and ensures consistent performance.
        /// </summary>
        /// <param name="lhs">The first hash token to compare.</param>
        /// <param name="rhs">The second hash token to compare.</param>
        /// <returns>True if the hash tokens are equal, false otherwise.</returns>
        private static bool IsHashTokenEqual(byte[] lhs, byte[] rhs)
        {
#if !NETSTANDARD2_0 && !NET462
            return MemoryExtensions.SequenceEqual<byte>(lhs, rhs);
#else
            return System.Linq.Enumerable.SequenceEqual(lhs, rhs);
#endif
        }
    }
}
