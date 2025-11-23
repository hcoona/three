// Pngcs is a CSharp implementation of PNG binary reader and writer.
// Copyright (C) 2025 Shuai Zhang <zhangshuai.ustc@gmail.com>
//
// Based on original work:
//   Copyright 2012    Hernán J. González    hgonzalez@gmail.com
//   Licensed under the Apache License, Version 2.0
//
//   You should have received a copy of the Apache License 2.0
//   along with the program.
//   If not, see <http://www.apache.org/licenses/LICENSE-2.0>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics.CodeAnalysis;

namespace Hjg.Pngcs
{
    /// <summary>
    /// Internal PNG predictor filter, or a strategy to select it.
    /// </summary>
    [SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Keep Public API compatibility")]
    public enum FilterType
    {
        /// <summary>
        /// No filtering
        /// </summary>
        FILTER_NONE = 0,
        /// <summary>
        /// SUB filter: uses same row
        /// </summary>
        FILTER_SUB = 1,
        /// <summary>
        ///  UP filter: uses previous row
        /// </summary>
        FILTER_UP = 2,
        /// <summary>
        ///AVERAGE filter: uses neighbors
        /// </summary>
        FILTER_AVERAGE = 3,
        /// <summary>
        /// PAETH predictor
        /// </summary>
        FILTER_PAETH = 4,

        /// <summary>
        /// Default strategy: select one of the standard filters depending on global image parameters
        /// </summary>
        FILTER_DEFAULT = -1, //


        /// <summary>
        /// Aggressive strategy: select dinamically the filters, trying every 8 rows
        /// </summary>
        FILTER_AGGRESSIVE = -2,

        /// <summary>
        /// Very aggressive and slow strategy: tries all filters for each row
        /// </summary>
        FILTER_VERYAGGRESSIVE = -3,

        /// <summary>
        /// Uses all fiters, one for lines, in cyclic way. Only useful for testing.
        /// </summary>
        FILTER_CYCLIC = -50,

        /// <summary>
        /// Not specified, placeholder for unknown or NA filters.
        /// </summary>
        FILTER_UNKNOWN = -100
    }


}
