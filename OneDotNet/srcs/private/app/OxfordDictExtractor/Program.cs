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

using System.Globalization;
using System.IO.Compression;
using System.Text;
using OxfordDictExtractor;
using OxfordDictExtractor.GenModel;
using OxfordDictExtractor.ParserModel;
using WebMarkupMin.Core;

var words = new List<Word>();
using (var fs = ZipFile.OpenRead("wordlist.tsv.zip").Entries.Single().Open())
using (var sr = new StreamReader(fs))
{
    string? line;
    while ((line = sr.ReadLine()) != null)
    {
        var parts = line.Split('\t', 2, StringSplitOptions.TrimEntries);
        if (parts[1].StartsWith("@@@", StringComparison.InvariantCulture))
        {
            Console.WriteLine("Skip linking word: " + parts[0]);
            continue;
        }

        words.Add(Word.ParseFromDictContent(parts[0], parts[1]));
    }
}

var minifier = new HtmlMinifier(new HtmlMinificationSettings
{
    WhitespaceMinificationMode = WhitespaceMinificationMode.Safe,
    PreserveNewLines = false,
    NewLineStyle = NewLineStyle.Unix,
});

using var ankiFileStream = File.Open(
    $"anki_words.tsv",
    new FileStreamOptions
    {
        Access = FileAccess.Write,
        BufferSize = 4096,
        Mode = FileMode.Create,
        Options = FileOptions.Asynchronous,
        PreallocationSize = 4096 * 1024,
        Share = FileShare.Read,
    });

const int SuperMemoXmlEntryCountPerFileMax = 500;
Task.WaitAll(
    GenerateAnkiImportingFile(ankiFileStream, minifier),
    GenerateCsvImportingFiles(),
    GenerateSuperMemoImportingXmlFiles(SuperMemoXmlEntryCountPerFileMax));

IEnumerable<(CefrLevel, IEnumerable<OxfordDictExtractor.GenModel.WordEntry>)>
GetWordEntriesByCefrLevels()
{
    foreach (var level in Enum.GetValues<CefrLevel>().Where(l => l != CefrLevel.Unspecified))
    {
        // editorconfig-checker-disable
        var wordClassEntries = from word in words
                               from entry in word.Entries
                               where !entry.IsOnlyPhrasalVerb && !entry.IsOnlyIdioms
                               from sense in entry.Senses
                               where !sense.IsXrefOnly
                               where sense.CefrLevel <= level
                                  && sense.CefrLevel != CefrLevel.Unspecified
                               group new { Sense = sense, word.OriginContent }
                               by new { entry.Name, entry.WordClass } into g
                               select new WordClassEntry(
                                   g.Key.Name,
                                   g.Key.WordClass,
                                   g.Select(c => c.Sense).ToList(),
                                   g.First().OriginContent);
        var entries = (from wce in wordClassEntries
                       group wce by wce.Name into g
                       where g.Any(wce => wce.WordSenses.Any(s => s.CefrLevel == level))
                       select new OxfordDictExtractor.GenModel.WordEntry(
                           g.Key, g.ToList(), g.First().OriginContent)).ToList();

        // editorconfig-checker-enable
        yield return (level, entries);
    }
}

async Task GenerateAnkiImportingFile(Stream fs, HtmlMinifier minifier)
{
    using var ankiStreamWriter = new StreamWriter(fs, new UTF8Encoding(false));

    await ankiStreamWriter.WriteLineAsync("#separator:Tab");
    await ankiStreamWriter.WriteLineAsync("#columns:Word\tSenses\tGUID\ttags\tDictContent");
    await ankiStreamWriter.WriteLineAsync("#guid column:3");
    await ankiStreamWriter.WriteLineAsync("#tags column:4");

    foreach (var pair in GetWordEntriesByCefrLevels())
    {
        var level = pair.Item1;
        foreach (var entry in pair.Item2)
        {
            var result = minifier.Minify(entry.OriginContent);
            if (result.Errors.Count != 0)
            {
                foreach (var error in result.Errors)
                {
                    Console.WriteLine(error.Message);
                }

                throw new InvalidDataException($"Failed to minify html for {entry.Name}({level}).");
            }

            if (result.Warnings.Count != 0)
            {
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine(warning.Message);
                }

                throw new InvalidDataException($"Failed to minify html for {entry.Name}({level}).");
            }

            await entry.WriteAnkiTsv(ankiStreamWriter, delimiter: '\t');
            await ankiStreamWriter.WriteAsync('\t');
            await ankiStreamWriter.WriteAsync(Nito.Guids.GuidFactory.CreateSha1(
                Constants.AnkiGuidNamespace,
                Encoding.UTF8.GetBytes($"{entry.Name}_{level}")).ToString());
            await ankiStreamWriter.WriteAsync('\t');
            await ankiStreamWriter.WriteAsync(level.ToString());
            await ankiStreamWriter.WriteAsync('\t');

            // magics only apply to this specific data content.
            await ankiStreamWriter.WriteAsync(
                result.MinifiedContent.ReplaceLineEndings(string.Empty));
            await ankiStreamWriter.WriteLineAsync();
        }
    }
}

async Task GenerateCsvImportingFiles()
{
    foreach (var pair in GetWordEntriesByCefrLevels())
    {
        var level = pair.Item1;
        using (var fs = File.Open(
            $"unstyled_words_{level}.csv",
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                BufferSize = 4096,
                Mode = FileMode.Create,
                Options = FileOptions.Asynchronous,
                PreallocationSize = 4096 * 1024,
                Share = FileShare.Read,
            }))
        using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            foreach (var entry in pair.Item2)
            {
                await entry.WriteUnstyledCsv(sw, delimiter: ',');
                await sw.WriteLineAsync();
            }
        }
    }
}

async Task GenerateSuperMemoImportingXmlFiles(int entriesPerFile)
{
    // editorconfig-checker-disable
    var wordClassEntries = from word in words
                           from entry in word.Entries
                           where !entry.IsOnlyPhrasalVerb && !entry.IsOnlyIdioms
                           from sense in entry.Senses
                           where !sense.IsXrefOnly
                           where sense.CefrLevel != CefrLevel.Unspecified
                           group new { Sense = sense, word.OriginContent }
                           by new { entry.Name, entry.WordClass } into g
                           select new WordClassEntry(
                               g.Key.Name,
                               g.Key.WordClass,
                               g.Select(c => c.Sense).ToList(),
                               g.First().OriginContent);
    var entries = (from wce in wordClassEntries
                   group wce by wce.Name into g
                   select new OxfordDictExtractor.GenModel.WordEntry(
                       g.Key, g.ToList(), g.First().OriginContent)).ToList();

    // editorconfig-checker-enable
    int fileCounter = 0;
    int counter = 0;
    StreamWriter? writer = null;
    foreach (var entry in entries)
    {
        if (counter == 0)
        {
            if (writer != null)
            {
                await writer.WriteLineAsync("</SuperMemoCollection>").ConfigureAwait(false);
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            writer = new StreamWriter(
                File.Open(
                    $"sm_{fileCounter:D4}.xml",
                    new FileStreamOptions
                    {
                        Access = FileAccess.Write,
                        BufferSize = 4096,
                        Mode = FileMode.Create,
                        Options = FileOptions.Asynchronous,
                        PreallocationSize = 4096 * 1024,
                        Share = FileShare.Read,
                    }),
                new UTF8Encoding(false));
            fileCounter++;
            await writer.WriteLineAsync("<SuperMemoCollection>").ConfigureAwait(false);
        }

        await writer!.WriteLineAsync("    <SuperMemoElement>").ConfigureAwait(false);
        await writer!.WriteLineAsync("        <Type>Item</Type>").ConfigureAwait(false);
        await writer!.WriteLineAsync("        <Content>").ConfigureAwait(false);

        await writer!.WriteLineAsync("            <Question><![CDATA[").ConfigureAwait(false);
        await writer!.WriteLineAsync(entry.Name).ConfigureAwait(false);
        await writer!.WriteLineAsync("            ]]></Question>").ConfigureAwait(false);

        await writer!.WriteLineAsync("            <Answer><![CDATA[").ConfigureAwait(false);

        using var stream = new MemoryStream();
        foreach (var wordClassEntry in entry.WordClassEntries)
        {
            stream.Seek(0, SeekOrigin.Begin);
            stream.SetLength(0);
            using (var writer2 = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
            {
                await wordClassEntry.ToNoStyledHtml(writer2);
            }

            var str = Encoding.UTF8.GetString(stream.GetBuffer().AsSpan(0, (int)stream.Length));
            foreach (char ch in str)
            {
                var value = Convert.ToInt32(ch);
                if (value > 127)
                {
                    await writer.WriteAsync("&#").ConfigureAwait(false);
                    await writer
                        .WriteAsync(value.ToString(CultureInfo.InvariantCulture))
                        .ConfigureAwait(false);
                    await writer.WriteAsync(";").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteAsync(ch).ConfigureAwait(false);
                }
            }
        }

        await writer!.WriteLineAsync("            ]]></Answer>").ConfigureAwait(false);

        await writer!.WriteLineAsync("        </Content>").ConfigureAwait(false);
        await writer!.WriteLineAsync("    </SuperMemoElement>").ConfigureAwait(false);

        counter++;
        if (counter == entriesPerFile)
        {
            counter = 0;
        }
    }

    if (writer != null)
    {
        await writer.WriteLineAsync("</SuperMemoCollection>").ConfigureAwait(false);
        await writer.DisposeAsync().ConfigureAwait(false);
    }
}
