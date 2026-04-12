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

using IV2TestOutputHelper = Xunit.Abstractions.ITestOutputHelper;
using IV3TestOutputHelper = Xunit.ITestOutputHelper;

namespace Microsoft.Extensions.Logging.Xunit.Tests
{
    internal sealed class V2TestOutputHelperAdapter(IV3TestOutputHelper v3TestOutputHelper) : IV2TestOutputHelper
    {
        public void WriteLine(string message)
        {
            v3TestOutputHelper.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            v3TestOutputHelper.WriteLine(format, args);
        }
    }
}
