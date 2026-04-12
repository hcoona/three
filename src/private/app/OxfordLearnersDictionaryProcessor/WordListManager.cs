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

using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using RocksDbSharp;

namespace OxfordLearnersDictionaryProcessor
{
    public partial class WordListManager(
        ILogger<WordListManager> logger,
        IHttpClientFactory httpClientFactory,
        RocksDb rocksDb)
    {
        private static readonly Dictionary<OxfordLearnersDictionaryWordList, string /*url*/>
            WordListUrlDictionary = new()
            {
                [OxfordLearnersDictionaryWordList.Oxford3000And5000] =
                @"https://www.oxfordlearnersdictionaries.com/wordlists/oxford3000-5000",
                [OxfordLearnersDictionaryWordList.Opal] =
                @"https://www.oxfordlearnersdictionaries.com/wordlists/opal",
            };

        public async Task<HtmlDocument> LoadWordListHtmlDocumentAsync(
            OxfordLearnersDictionaryWordList wordList,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (forceRefresh || !rocksDb.HasKey($"WordList_RawHtml_{wordList}"))
            {
                await this.DoDownloadWordListRawHtmlAsync(wordList, cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }

            var rawHtml = rocksDb.Get($"WordList_RawHtml_{wordList}");

            HtmlDocument document = new();
            document.LoadHtml(rawHtml);
            return document;
        }

        public async IAsyncEnumerable<string> LoadExtractedStructuredDataFromWordListAsync(
            OxfordLearnersDictionaryWordList wordList,
            bool forceRefresh = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (forceRefresh
                || !rocksDb.HasKey($"WordList_WordMetadata_{wordList}_LastWriteTimeUtc"))
            {
                this.LogWordListNeedsToBeDownloaded(wordList);
                await this.DoExtractStructuredDataFromWordListAsync(wordList, cancellationToken);
            }

            var lastWriteTimeUtc =
                rocksDb.Get($"WordList_WordMetadata_{wordList}_LastWriteTimeUtc");

            using Iterator iterator = rocksDb
                .NewIterator(readOptions: new ReadOptions()
                    .SetTotalOrderSeek(true))
                .Seek($"WordList_WordMetadata_{wordList}_{lastWriteTimeUtc:o}");
            while (iterator.Valid() && !cancellationToken.IsCancellationRequested)
            {
                if (!iterator.StringKey().StartsWith(
                        $"WordList_WordMetadata_{wordList}_{lastWriteTimeUtc:o}_"))
                {
                    break;
                }

                yield return iterator.StringValue();
                iterator.Next();
            }
        }

        private async Task DoDownloadWordListRawHtmlAsync(
            OxfordLearnersDictionaryWordList wordList,
            CancellationToken cancellationToken = default)
        {
            if (!WordListUrlDictionary.TryGetValue(wordList, out string? url))
            {
                throw new InvalidOperationException(
                    $"The specific word list URL is not found: {wordList}.");
            }

            DateTimeOffset lastWriteTimeUtc = DateTimeOffset.UtcNow;

            using HttpClient httpClient = httpClientFactory.CreateClient();
            HttpResponseMessage responseMessage = await httpClient
                .GetAsync(
                    url,
                    completionOption: HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
            responseMessage.EnsureSuccessStatusCode();

            string value = await responseMessage.Content
                .ReadAsStringAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            WriteBatch writeBatch = new();
            writeBatch.Put($"WordList_RawHtml_{wordList}", value);
            writeBatch.Put(
                $"WordList_RawHtml_{wordList}_LastWriteTimeUtc",
                lastWriteTimeUtc.ToString("o"));
            rocksDb.Write(writeBatch);
        }

        private async Task DoExtractStructuredDataFromWordListAsync(
            OxfordLearnersDictionaryWordList wordList,
            CancellationToken cancellationToken = default)
        {
            HtmlDocument document = await this
                .LoadWordListHtmlDocumentAsync(wordList, cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            DateTimeOffset lastWriteTimeUtc = DateTimeOffset.UtcNow;

            var words = from node in document.DocumentNode.SelectNodes(
                            "//div[@id='wordlistsContentPanel']/ul/li")
                        select node;
            foreach (var li in words)
            {
                if (!this.TryExtractWordListWordMetadata(li, out var wordMetadata))
                {
                    Console.Error.WriteLine("Ignore failed extraction world: " + li.OuterHtml);
                }

                if (wordMetadata!.Headword == "terror")
                {
                    wordMetadata = wordMetadata with { Link = "/definition/english/terror" };
                }

                byte[] hash = XxHash128.Hash(Encoding.UTF8.GetBytes(li.OuterHtml));
                var hashHex = Convert.ToHexString(hash);

                rocksDb.Put(
                    $"WordList_WordMetadata_{wordList}_{lastWriteTimeUtc:o}_{hashHex}",
                    JsonSerializer.Serialize(wordMetadata));
            }

            rocksDb.Put(
                $"WordList_WordMetadata_{wordList}_LastWriteTimeUtc",
                lastWriteTimeUtc.ToString("o"));
        }

        private bool TryExtractWordListWordMetadata(
            HtmlNode node,
            out WordListWordMetadata? wordMetadata)
        {
            var dataHw = node.GetDataAttribute("hw");
            if (dataHw == null)
            {
                Console.Error.WriteLine($"No data-hw: {node.OuterHtml}");
                wordMetadata = null;
                return false;
            }

            var aNode = node.SelectSingleNode(".//a");
            if (aNode == null)
            {
                Console.Error.WriteLine($"No a node: {node.OuterHtml}");
                wordMetadata = null;
                return false;
            }

            var posSpanNode = node.SelectSingleNode(".//span[@class='pos']");
            if (posSpanNode == null)
            {
                this.LogWordPosAbsence(headword: dataHw.Value.Trim());
            }

            var belongToNode = node.SelectSingleNode(".//span[@class='belong-to']");
            if (belongToNode == null)
            {
                this.LogWordCefrLevelAbsence(
                    headword: dataHw.Value.Trim(),
                    partOfSpeech: posSpanNode?.InnerText.Trim());
            }

            wordMetadata = new WordListWordMetadata(
                Headword: dataHw.Value.Trim(),
                CefrLevel: belongToNode == null ? string.Empty : belongToNode.InnerText.Trim(),
                Link: aNode.GetAttributeValue("href", string.Empty).Trim(),
                PartOfSpeech: posSpanNode?.InnerText.Trim());
            return true;
        }

        [LoggerMessage(
            EventId = Constants.EventIdWordListFileNeedsToBeDownloaded,
            Level = LogLevel.Information,
            Message = "The word list {wordList} needs to be downloaded.")]
        private partial void LogWordListNeedsToBeDownloaded(
            OxfordLearnersDictionaryWordList wordList);

        [LoggerMessage(
            EventId = Constants.EventIdWordListWordCefrLevelAbsence,
            Level = LogLevel.Warning,
            Message =
                "The word {headword} with part of speech {partOfSpeech} have no CEFR level.")]
        private partial void LogWordCefrLevelAbsence(string headword, string? partOfSpeech);

        [LoggerMessage(
            EventId = Constants.EventIdWordListWordPosAbsence,
            Level = LogLevel.Warning,
            Message = "The word {headword} have no part of speech.")]
        private partial void LogWordPosAbsence(string headword);
    }
}
