/*
 * ImageOcclusionEditorWinUI3 - A WinUI 3 application for creating image occlusion cards
 * Copyright (C) 2025 Shuai Zhang
 *
 * This file contains code derived from ImageOcclusionEditor by SuperMemo Community,
 * which is licensed under the MIT License.
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
using System.IO;
using System.Text;

namespace ImageOcclusionEditorWinUI3.Services
{
    internal static class SvgEditorNavigationBuilder
    {
        private const string SvgEditorPath = "svg-edit/index.html";

        public static Uri Build(string backgroundFilePath, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(backgroundFilePath))
            {
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(backgroundFilePath));
            }

            Uri baseUri = new Uri(Path.Combine(AppContext.BaseDirectory, SvgEditorPath));
            string query = BuildQuery(backgroundFilePath, width, height);

            return string.IsNullOrEmpty(query) ? baseUri : new Uri($"{baseUri}?{query}");
        }

        private static string BuildQuery(string backgroundFilePath, int width, int height)
        {
            var builder = new StringBuilder();

            AppendUrlParam(builder, "bkgd_url", backgroundFilePath);
            AppendUrlParam(builder, "dimensions", $"{width},{height}");
            AppendUrlParam(builder, "initFill[color]", ImageOcclusionEditorWinUI3.Settings.FillColor);
            AppendUrlParam(builder, "initFill[opacity]", "1");
            AppendUrlParam(builder, "initStroke[color]", ImageOcclusionEditorWinUI3.Settings.StrokeColor);
            AppendUrlParam(builder, "initStroke[width]", ImageOcclusionEditorWinUI3.Settings.StrokeWidth);
            AppendUrlParam(builder, "initStroke[opacity]", "1");
            AppendUrlParam(builder, "storagePrompt", "false");

            return builder.ToString().TrimStart('&');
        }

        private static void AppendUrlParam(StringBuilder builder, string key, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(value));
        }
    }
}
