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
using System.Text;

namespace PhiFailureDetector.ConsoleApp
{
    internal static class Program
    {
        private const long ToNano = 1000000L;
        private const long Last = 555 * ToNano;

        public static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("===== Normal Distribution =====");

            var arrivalWindow = new LongIntervalHistoryCollection(1000);
            var mean = TimeSpan.FromSeconds(1).TotalMilliseconds;
            var stdDeviation = mean / 4;
            arrivalWindow.Enqueue((long)(mean - stdDeviation));
            arrivalWindow.Enqueue((long)(mean + stdDeviation));
#if false
            arrivalWindow.Enqueue(1000);
#endif
            Console.WriteLine(Normal(1000, arrivalWindow));
            Console.WriteLine(Normal(2000, arrivalWindow));
            Console.WriteLine(Normal(3000, arrivalWindow));

#if false
            PrintPhi(666, last, arrivalWindow, PhiFailureDetector.Normal);
            PrintPhi(777, last, arrivalWindow, PhiFailureDetector.Normal);
            PrintPhi(888, last, arrivalWindow, PhiFailureDetector.Normal);
            PrintPhi(999, last, arrivalWindow, PhiFailureDetector.Normal);
            PrintPhi(1100, last, arrivalWindow, PhiFailureDetector.Normal);
            PrintPhi(1200, last, arrivalWindow, PhiFailureDetector.Normal);
#endif

            Console.WriteLine();

            Console.WriteLine("===== Exponential Distribution =====");

            arrivalWindow = new LongIntervalHistoryCollection(4);
            arrivalWindow.Enqueue(111 * ToNano);
            arrivalWindow.Enqueue(111 * ToNano);
            arrivalWindow.Enqueue(111 * ToNano);
            arrivalWindow.Enqueue(111 * ToNano);

            PrintPhi(666, Last, arrivalWindow, PhiFailureDetector.Exponential);
            PrintPhi(777, Last, arrivalWindow, PhiFailureDetector.Exponential);
            PrintPhi(888, Last, arrivalWindow, PhiFailureDetector.Exponential);
            PrintPhi(999, Last, arrivalWindow, PhiFailureDetector.Exponential);
            PrintPhi(1100, Last, arrivalWindow, PhiFailureDetector.Exponential);
            PrintPhi(1200, Last, arrivalWindow, PhiFailureDetector.Exponential);
            PrintPhi(3000, Last, arrivalWindow, PhiFailureDetector.Exponential);

            Console.WriteLine();
        }

        private static void PrintPhi(
            int nowTimestamp,
            long lastTimestamp,
            LongIntervalHistoryCollection arrivalWindow,
            PhiFailureDetector.PhiFunc phiFunc)
        {
            Console.WriteLine(
                "Phi({0}) = {1:F3}",
                nowTimestamp,
                phiFunc(nowTimestamp * ToNano, lastTimestamp, arrivalWindow));
        }

        private static double CDF(double phi)
        {
            return 1 - Math.Pow(10, -phi);
        }

        private static double Normal(long duration, LongIntervalHistoryCollection intervalHistory)
        {
            var y = (duration - intervalHistory.Avg) / intervalHistory.StdDeviation;
            var exp = Math.Exp(-y * (1.5976 + (0.070566 * y * y)));
            if (duration > intervalHistory.Avg)
            {
                return -Math.Log10(exp / (1 + exp));
            }
            else
            {
                return -Math.Log10(1 - (1 / (1 + exp)));
            }
        }
    }
}
