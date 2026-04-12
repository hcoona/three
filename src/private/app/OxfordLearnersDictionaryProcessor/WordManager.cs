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
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using RocksDbSharp;

namespace OxfordLearnersDictionaryProcessor
{
    public partial class WordManager(
        ILogger<WordManager> logger,
        IHttpClientFactory httpClientFactory,
        RocksDb rocksDb)
    {
        private readonly ILogger<WordManager> logger = logger; // StyleCop: no underscore
        private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
        private readonly RocksDb rocksDb = rocksDb;

        public async Task<HtmlDocument> LoadWordHtmlDocumentAsync(
            WordListWordMetadata wordMetadata,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var lastSegmentOfLink = wordMetadata.Link.Split('/').Last();

            if (forceRefresh || !this.rocksDb.HasKey($"Word_RawHtml_{lastSegmentOfLink}"))
            {
                Log.Downloading(this.logger, lastSegmentOfLink, forceRefresh);
                await this.DoDownloadWordRawHtmlAsync(wordMetadata, cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }
            else
            {
                Log.CacheHit(this.logger, lastSegmentOfLink);
            }

            var rawHtml = this.rocksDb.Get($"Word_RawHtml_{lastSegmentOfLink}");
            HtmlDocument document = new();
            document.LoadHtml(rawHtml);
            return document;
        }

        private async Task DoDownloadWordRawHtmlAsync(
            WordListWordMetadata wordMetadata,
            CancellationToken cancellationToken)
        {
            var lastSegmentOfLink = wordMetadata.Link.Split('/').Last();
            DateTimeOffset lastWriteTimeUtc = DateTimeOffset.UtcNow;

            using var httpClient = this.httpClientFactory.CreateClient();
            HttpResponseMessage responseMessage = await httpClient
                .GetAsync(
                    requestUri: $"https://www.oxfordlearnersdictionaries.com{wordMetadata.Link}",
                    completionOption: HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
            responseMessage.EnsureSuccessStatusCode();

            string value = await responseMessage.Content
                .ReadAsStringAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            WriteBatch writeBatch = new();
            writeBatch.Put($"Word_RawHtml_{lastSegmentOfLink}", value);
            writeBatch.Put(
                $"Word_RawHtml_{lastSegmentOfLink}_LastWriteTimeUtc",
                DateTime.UtcNow.ToString("o"));
            this.rocksDb.Write(writeBatch);
            Log.Stored(this.logger, lastSegmentOfLink, value.Length);
        }

        private static partial class Log
        {
            [LoggerMessage(
                EventId = 1,
                Level = LogLevel.Information,
                Message = "Downloading raw html for word {Word} (forceRefresh={Force})")]
            public static partial void Downloading(ILogger logger, string Word, bool Force);

            [LoggerMessage(
                EventId = 2,
                Level = LogLevel.Debug,
                Message = "Cache hit for word {Word}")]
            public static partial void CacheHit(ILogger logger, string Word);

            [LoggerMessage(
                EventId = 3,
                Level = LogLevel.Information,
                Message = "Stored raw html for {Word} (bytes={Length})")]
            public static partial void Stored(ILogger logger, string Word, int Length);
        }
    }
}
