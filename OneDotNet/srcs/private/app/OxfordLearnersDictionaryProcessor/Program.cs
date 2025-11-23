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

using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using OxfordLearnersDictionaryProcessor;
using RocksDbSharp;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .MinimumLevel.Debug()
    .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen)
    .CreateLogger();

#pragma warning disable SKEXP0010
IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
#if true
var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
    ?? throw new InvalidOperationException(
        "You must set AZURE_OPENAI_API_KEY environment variable.");
kernelBuilder.AddAzureOpenAIChatCompletion(
        deploymentName: "gpt-4o-mini",
        endpoint: "https://shuaizhang-oai-westus.openai.azure.com/",
        apiKey: azureOpenAiApiKey);
#elif true
kernelBuilder.AddOpenAIChatCompletion(
        modelId: "phi4",
        endpoint: new Uri("http://127.0.0.1:11434/v1"),
        apiKey: null);
#endif
#pragma warning restore SKEXP0010

IServiceCollection services = kernelBuilder.Services;

// Raw HTML:                  WordList_RawHtml_<WordListName>
// Raw HTML Update Time:      WordList_RawHtml_<WordListName>_LastWriteTimeUtc
// Word Metadata:             WordList_WordMetadata_<WordListName>_<LastWriteTimeUtc>_<ContentHash>
// Word Metadata Update Time: WordList_WordMetadata_<WordListName>_LastWriteTimeUtc
// Word Raw HTML:             Word_RawHtml_<LastSegementOfLink>
// Word Raw HTML Update Time: Word_RawHtml_<LastSegementOfLink>_LastWriteTimeUtc
var cache = Cache.CreateLru(100 * 1024 * 1024);
var options = new DbOptions()
    .SetCreateIfMissing(true)
    .SetCompression(Compression.Lz4)
    .SetBlockBasedTableFactory(
        new BlockBasedTableOptions()
            .SetBlockCache(cache)
            .SetFilterPolicy(BloomFilterPolicy.Create(
                bits_per_key: 10,
                use_block_based_builder: false))
            .SetCacheIndexAndFilterBlocks(true)
            .SetPinL0FilterAndIndexBlocksInCache(true)
            .SetFormatVersion(6)
            .SetWholeKeyFiltering(false))
    .SetLevelCompactionDynamicLevelBytes(true)
    .SetPrefixExtractor(SliceTransform.CreateFixedPrefix(5));
using var db = RocksDb.Open(options, "cache");
services.AddSingleton(db);

services.AddLogging(builder => builder.AddSerilog(dispose: true));

services.ConfigureHttpClientDefaults(builder =>
{
    builder.AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(300);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3000);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(600);
    });
    builder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseCookies = false,
    });
});

services.AddHttpClient();

services.AddHttpClient("long-lived")
    .UseSocketsHttpHandler((handler, _) =>
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(15))
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
    .AddStandardResilienceHandler();

services.AddSingleton<WordListManager>();
services.AddSingleton<WordManager>();

Kernel kernel = kernelBuilder.Build();
var serviceProvider = kernel.Services;

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var wordListManager = serviceProvider.GetRequiredService<WordListManager>();
var wordManager = serviceProvider.GetRequiredService<WordManager>();

#pragma warning disable SKEXP0010
AzureOpenAIPromptExecutionSettings executionSettings = new()
{
    ResponseFormat = typeof(WordMetadata),
};
#pragma warning restore SKEXP0010

KernelFunction questionAndAnswer = kernel.CreateFunctionFromPrompt(
    """
    Extract meaningful information into exact JSON format without any additional commentary.
    ox3ksym_b1 in the class attribute means oxford 3000 word list with CEFR B1 level.
    We have 3k & 5k word list, CEFR levels A1, A2, B1, B2, C1, C2.

    ------

    {{$input}}
    """,
    executionSettings: executionSettings);

CancellationTokenSource cts = new();
cts.CancelAfter(TimeSpan.FromSeconds(3000));

var wordListTag = OxfordLearnersDictionaryWordList.Oxford3000And5000;

var counter = 0;
await foreach (var wordMetadataJson in wordListManager
    .LoadExtractedStructuredDataFromWordListAsync(wordListTag, forceRefresh: false, cts.Token)
    .ConfigureAwait(continueOnCapturedContext: false))
{
    if (DateTimeOffset.TryParse(wordMetadataJson, out var _))
    {
        continue;
    }

    counter++;
    if (counter == 3)
    {
        var wordMetadata = JsonSerializer.Deserialize<WordListWordMetadata>(wordMetadataJson)
            ?? throw new InvalidOperationException("Impossible");
        var document = await wordManager
            .LoadWordHtmlDocumentAsync(wordMetadata, forceRefresh: false, cts.Token)
            .ConfigureAwait(continueOnCapturedContext: false);
        document.OptionEmptyCollection = true;

        // TODO(shuaizhang): Cleanup the HTML content before feeding it to the model.
        var divNode = document.DocumentNode.SelectSingleNode("//div[@id='entryContent']");

        // Remove span[@unbox='mlt'].
        foreach (var node in divNode.SelectNodes("//*[@unbox='mlt']"))
        {
            node.Remove();
        }

        // Remove span[@unbox='snippet'].
        foreach (var node in divNode.SelectNodes("//*[@unbox='snippet']"))
        {
            node.Remove();
        }

        // Remove all attributes except for "sensenum", "class" and "id".
        foreach (var node in divNode.SelectNodes(".//*"))
        {
            foreach (var attribute in node.Attributes.ToList())
            {
                if (attribute.Name != "sensenum"
                        && attribute.Name != "class"
                        && attribute.Name != "id")
                {
                    node.Attributes.Remove(attribute);
                }
            }
        }

        // Remove div[@class='idioms'] and div[@class='dictlink-g'] and div[@class='pron-link'].
        foreach (var node in divNode.SelectNodes("//*[@class='idioms']"))
        {
            node.Remove();
        }

        foreach (var node in divNode.SelectNodes("//*[@class='dictlink-g']"))
        {
            node.Remove();
        }

        foreach (var node in divNode.SelectNodes("//*[@class='pron-link']"))
        {
            node.Remove();
        }

        // Remove div[@id='ring-links-box'].
        foreach (var node in divNode.SelectNodes("//*[@id='ring-links-box']"))
        {
            node.Remove();
        }

        Console.WriteLine(divNode.InnerHtml);

        FunctionResult result = await kernel
            .InvokeAsync(
                function: questionAndAnswer,
                arguments: new KernelArguments
                {
                    { "input", divNode.InnerHtml },
                },
                cancellationToken: cts.Token)
            .ConfigureAwait(continueOnCapturedContext: false);
        Console.WriteLine(result.GetValue<string>()!);
    }
}

Console.WriteLine($"Word Count: {counter}");

internal static partial class LogExtensions
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Word list downloaded successfully.")]
    internal static partial void LogDownloadSuccessfully(this ILogger<Program> logger);
}
