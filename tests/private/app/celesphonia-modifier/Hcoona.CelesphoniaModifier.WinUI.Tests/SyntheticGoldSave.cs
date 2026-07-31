using System.Globalization;
using System.Text;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.WinUI.Tests;

internal sealed class SyntheticGoldSave : IAsyncDisposable
{
    private SyntheticGoldSave(string rootPath, string slotPath)
    {
        RootPath = rootPath;
        SlotPath = slotPath;
    }

    internal string RootPath { get; }

    internal string SlotPath { get; }

    internal string BackupPath => SlotPath + ".celesphonia-original.bak";

    internal string BackupStagingPath => BackupPath + ".staging";

    internal string CandidateStagePath => SlotPath + ".celesphonia-stage.tmp";

    internal static async Task<SyntheticGoldSave> CreateAsync(long gold = 7)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "celesphonia-gold-editor-tests",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        string slot = Path.Combine(root, "file1.rpgsave");
        await File.WriteAllBytesAsync(
            slot,
            CreateSaveBytes(gold),
            TestContext.Current.CancellationToken);
        return new SyntheticGoldSave(root, slot);
    }

    internal static byte[] CreateSaveBytes(long gold)
    {
        return CreateSaveBytes(
            gold.ToString(CultureInfo.InvariantCulture),
            gold.ToString(CultureInfo.InvariantCulture));
    }

    internal static byte[] CreateSaveBytes(string partyGold, string variableGold)
    {
        return AtlasLzStringCodec.CompressToBase64(
            CreateGoldJson(partyGold, variableGold),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    internal static byte[] CreateMissingSaveBytes()
    {
        return AtlasLzStringCodec.CompressToBase64(
            "{\"party\":{},\"variables\":{\"_data\":[]}}",
            cancellationToken: TestContext.Current.CancellationToken);
    }

    internal static byte[] CreateAmbiguousSaveBytes()
    {
        return AtlasLzStringCodec.CompressToBase64(
            $"{{\"party\":{{\"_gold\":1,\"_gold\":1}},"
                + $"\"variables\":{{\"_data\":{CreateDataArray("1")}}}}}",
            cancellationToken: TestContext.Current.CancellationToken);
    }

    internal static byte[] CreateWrongShapeSaveBytes()
    {
        return AtlasLzStringCodec.CompressToBase64(
            $"{{\"party\":[],\"variables\":{{\"_data\":{CreateDataArray("1")}}}}}",
            cancellationToken: TestContext.Current.CancellationToken);
    }

    internal async Task WriteAsync(byte[] bytes)
    {
        await File.WriteAllBytesAsync(
            SlotPath,
            bytes,
            TestContext.Current.CancellationToken);
    }

    internal void ReplaceLive(byte[] bytes)
    {
        string replacement = Path.Combine(
            RootPath,
            $"replacement-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(replacement, bytes);
        File.Replace(replacement, SlotPath, null);
    }

    internal static async Task<long> ReadGoldAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        AtlasGoldReadModelResult result = AtlasGoldReadModel.Read(
            AtlasSaveReader.Read(
                bytes,
                AtlasSaveReaderLimits.Default,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        return result.PartyGold.Value
            ?? throw new InvalidOperationException("Synthetic Gold was not present.");
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static string CreateGoldJson(string partyGold, string variableGold)
    {
        return $"{{\"party\":{{\"_gold\":{partyGold}}},"
            + $"\"variables\":{{\"_data\":{CreateDataArray(variableGold)}}}}}";
    }

    private static string CreateDataArray(string gold)
    {
        StringBuilder data = new();
        data.Append('[');
        for (int index = 0; index <= 215; index++)
        {
            if (index > 0)
            {
                data.Append(',');
            }

            data.Append(index == 215 ? gold : "0");
        }

        data.Append(']');
        return data.ToString();
    }
}
