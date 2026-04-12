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

namespace PhiFailureDetector
{
    /// <summary>
    /// Provides statistical information about a collection of values.
    /// </summary>
    public interface IWithStatistics
    {
        /// <summary>
        /// Gets the average (arithmetic mean) of all values in the collection.
        /// </summary>
        /// <value>The average value as a double precision floating-point number.</value>
        double Avg { get; }

        /// <summary>
        /// Gets the number of values in the collection.
        /// </summary>
        /// <value>The count of values as an integer.</value>
        int Count { get; }

        /// <summary>
        /// Gets the standard deviation of all values in the collection.
        /// </summary>
        /// <value>The standard deviation as a double precision floating-point number.</value>
        /// <remarks>
        /// Standard deviation measures the amount of variation or dispersion in the dataset.
        /// </remarks>
        double StdDeviation { get; }

        /// <summary>
        /// Gets the sum of all values in the collection.
        /// </summary>
        /// <value>The sum of all values as a long integer.</value>
        long Sum { get; }

        /// <summary>
        /// Gets the variance of all values in the collection.
        /// </summary>
        /// <value>The variance as a double precision floating-point number.</value>
        /// <remarks>
        /// Variance measures how far the values are spread out from the average.
        /// It is calculated as the average of the squared differences from the mean.
        /// </remarks>
        double Variance { get; }
    }
}
