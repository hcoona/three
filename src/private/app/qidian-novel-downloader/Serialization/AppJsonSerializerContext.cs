using System.Text.Json.Serialization;

namespace Hcoona.QidianNovelDownloader.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CatalogSnapshot))]
[JsonSerializable(typeof(ChapterCacheEntry))]
[JsonSerializable(typeof(ChapterCacheProbe))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;
