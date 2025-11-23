/*
 * ImageOcclusionEditorWinUI3 - A WinUI 3 application for creating image occlusion cards
 * Copyright (C) 2025 Shuai Zhang
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Runtime.InteropServices;

namespace ImageOcclusionEditorWinUI3
{
    internal static partial class NativeHelper
    {
        // Win32 MessageBox constants
        public const uint MB_OK = 0x00000000;
        public const uint MB_ICONERROR = 0x00000010;

        [LibraryImport(
            "User32.dll",
            EntryPoint = "MessageBoxW",
            StringMarshalling = StringMarshalling.Utf16)]
        public static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    }
}