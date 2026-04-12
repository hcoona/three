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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PhiFailureDetector.UnitTest
{
    [TestClass]
    public class ExponentialPhi_UnitTest
    {
        [TestMethod]
        public void Test_CassandraCase()
        {
            const long toNano = 1000000L;

            var intervalHistory = new LongIntervalHistoryCollection(4);
            intervalHistory.Enqueue(111 * toNano);
            intervalHistory.Enqueue(111 * toNano);
            intervalHistory.Enqueue(111 * toNano);
            intervalHistory.Enqueue(111 * toNano);
            intervalHistory.Enqueue(111 * toNano);

            Assert.AreEqual(1.0, PhiFailureDetector.Exponential(666 * toNano, 555 * toNano, intervalHistory));
            Assert.AreEqual(22.03, PhiFailureDetector.Exponential(3000 * toNano, 555 * toNano, intervalHistory), 0.01);
        }
    }
}
