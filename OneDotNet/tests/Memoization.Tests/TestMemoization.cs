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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Memoization.Tests
{
    [TestClass]
    public class TestMemoization
    {
        static TestMemoization()
        {
            Memoization.DefaultCache = new MemoryCache(new MemoryCacheOptions());
        }

        [TestMethod]
        public void Test1()
        {
            var counter = 0;
            var fib = YCombinator<int>.Fix(f => x =>
            {
                counter++;
                return x < 2 ? 1 : f(x - 1) + f(x - 2);
            });

            counter = 0;
            fib(7);
            Assert.AreEqual(41, counter);

            counter = 0;
            fib(7);
            Assert.AreEqual(41, counter);
        }

        [TestMethod]
        public void Test2()
        {
            var counter = 0;
            var fib = YCombinator<int>.Fix(f => x =>
            {
                counter++;
                return x < 2 ? 1 : f(x - 1) + f(x - 2);
            });
            var m_fib = Memoization.Create(fib);

            counter = 0;
            m_fib(7);
            Assert.AreEqual(41, counter);

            counter = 0;
            m_fib(7);
            Assert.AreEqual(0, counter);
        }

        internal static class YCombinator<T>
        {
            private static Func<Func<Func<T, T>, Func<T, T>>, Func<T, T>> fix =
                f => ((Recursive)(g =>
                    f(x => g(g)(x))))((Recursive)(g => f(x => g(g)(x))));

            private delegate Func<T, T> Recursive(Recursive recursive);

            public static Func<Func<Func<T, T>, Func<T, T>>, Func<T, T>> Fix { get => fix; set => fix = value; }
        }
    }
}
