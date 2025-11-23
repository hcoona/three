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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace OxfordDictExtractor.ParserModel
{
    public record Word
    {
        public string Key { get; init; } = default!;

        public List<WordEntry> Entries { get; init; } = new();

        public string OriginContent { get; init; } = default!;

        public void PrintDebug(TextWriter writer, int nestLevel = 0)
        {
            writer.WriteLine(new string('\t', nestLevel) + this.Key);
            foreach (var entry in this.Entries)
            {
                entry.PrintDebug(writer, nestLevel + 1);
            }

            writer.WriteLine();
        }

        public static Word ParseFromDictContent(string key, string content)
        {
            HtmlDocument html = new();
            html.LoadHtml(content);

            var htmlEntries =
                html.DocumentNode.SelectNodes("/div[@id='entryContent']/div[@class='entry']");
            return new Word
            {
                Key = key,
                Entries = htmlEntries
                    .Select(WordEntry.ParseFromDictContent)
                    .ToList(),
                OriginContent = content,
            };
        }
    }
}
