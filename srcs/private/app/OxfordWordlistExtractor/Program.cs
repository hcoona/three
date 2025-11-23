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

using System.Collections.Immutable;
using System.Net;
using System.Text;
using HtmlAgilityPack;

var document = new HtmlDocument();
using (var fs = File.OpenText("oxford3000-5000.html"))
{
    document.Load(fs);
}

var words = from node in document.DocumentNode.SelectNodes(
                "//div[@id='wordlistsContentPanel']/ul/li")
            select new WordEntry(
                WebUtility.HtmlDecode(node.GetDataAttribute("hw").Value),
                Enum.Parse<CefrLevel>(
                    node.GetDataAttribute("ox3000")?.Value ?? "Unspecified", true),
                Enum.Parse<CefrLevel>(
                    node.GetDataAttribute("ox5000")?.Value ?? "Unspecified", true));

foreach (var group in words
    .Where(entry => entry.Oxford3000 != CefrLevel.Unspecified)
    .GroupBy(entry => entry.Oxford3000)
    .OrderBy(group => group.Key))
{
    await File
        .WriteAllLinesAsync(
            $"oxford3000_{group.Key}.txt",
            group.Select(entry => entry.Name),
            Encoding.UTF8)
        .ConfigureAwait(/*continueOnCapturedContext=*/false);
}

foreach (var group in words
    .Where(entry => entry.Oxford3000 == CefrLevel.Unspecified
                    && entry.Oxford5000 != CefrLevel.Unspecified)
    .GroupBy(entry => entry.Oxford5000)
    .OrderBy(group => group.Key))
{
    await File
        .WriteAllLinesAsync(
            $"oxford5000_{group.Key}.txt",
            group.Select(entry => entry.Name),
            Encoding.UTF8)
        .ConfigureAwait(/*continueOnCapturedContext=*/false);
}

using (var fs = File.OpenText("oxford-phrase-list.html"))
{
    document.Load(fs);
}

var phases = from node in document.DocumentNode.SelectNodes(
                "//div[@id='wordlistsContentPanel']/ul/li")
            select new PhaseEntry(
                WebUtility.HtmlDecode(node.GetDataAttribute("hw").Value),
                Enum.Parse<CefrLevel>(
                    node.GetDataAttribute("oxford_phrase_list")?.Value ?? "Unspecified", true));

foreach (var group in phases
    .GroupBy(entry => entry.Level)
    .OrderBy(group => group.Key))
{
    await File
        .WriteAllLinesAsync(
            $"oxford_phrase_{group.Key}.txt",
            group.Select(entry => entry.Name),
            Encoding.UTF8)
        .ConfigureAwait(/*continueOnCapturedContext=*/false);
}
