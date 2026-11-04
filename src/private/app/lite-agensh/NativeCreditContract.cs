using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiteAgensh;

// This descriptor is evidence, not a feature switch. No installed Copilot contract is assumed.
public sealed record NativeCreditContract
{
    public required string AgentVersion { get; init; }

    public required string EvidenceReference { get; init; }

    public required string AppliedLimitDocumentation { get; init; }

    public required string UsageReportDocumentation { get; init; }

    public required string InformationalCommand { get; init; }

    public required string AppliedLimitPointer { get; init; }

    public required string SessionUsagePointer { get; init; }

    public required string ReportCreditsPointer { get; init; }

    public string? ReportSessionPointer { get; init; }

    public required string ReportUsageScope { get; init; }

    public bool UsageIsSessionCumulative { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AgentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(EvidenceReference);
        if (!UsageIsSessionCumulative)
        {
            throw new OperationException(
                "unavailable",
                "Native evidence must establish session-cumulative AI Credits, including "
                    + "exact-session loads."
            );
        }

        if (ReportUsageScope is not ("session-cumulative" or "process-incremental"))
        {
            throw new OperationException(
                "unavailable",
                "Native report evidence must identify cumulative-session or "
                    + "incremental-process credit semantics."
            );
        }

        foreach (string source in new[] { AppliedLimitDocumentation, UsageReportDocumentation })
        {
            if (
                !Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != "https"
                || uri.Host is not ("docs.github.com" or "agentclientprotocol.com")
            )
            {
                throw new OperationException(
                    "unavailable",
                    "Native credit fields require explicit official documentary evidence."
                );
            }
        }

        if (InformationalCommand is not ("usage" or "context" or "session info"))
        {
            throw new OperationException(
                "unavailable",
                "The requested preflight command is not a documented informational ACP operation."
            );
        }

        foreach (
            string pointer in new[]
            {
                AppliedLimitPointer,
                SessionUsagePointer,
                ReportCreditsPointer,
            }.Concat(ReportSessionPointer is null ? [] : new[] { ReportSessionPointer })
        )
        {
            if (string.IsNullOrEmpty(pointer) || !pointer.StartsWith('/'))
            {
                throw new OperationException(
                    "unavailable",
                    "Native credit field JSON pointers must be documentary, explicit and absolute."
                );
            }
        }
    }

    public void VerifyVersion(JsonObject initialize)
    {
        Validate();
        if (initialize["agentInfo"]?["version"]?.GetValue<string>() != AgentVersion)
        {
            throw new OperationException(
                "unavailable",
                "The native version differs from the version bound to credit evidence."
            );
        }
    }

    public decimal VerifySession(JsonNode information, decimal allocated)
    {
        decimal limit = Number(information, AppliedLimitPointer);
        decimal usage = Number(information, SessionUsagePointer);
        if (limit != allocated || usage < 0)
        {
            throw new OperationException(
                "credit_unknown",
                "The informational operation did not verify the actual allocated session "
                    + "limit and usage."
            );
        }

        return usage;
    }

    public (decimal Actual, string Identity) ReadFinalReport(string path, string? sessionId)
    {
        byte[] report = File.ReadAllBytes(path);
        JsonNode node =
            JsonNode.Parse(report)
            ?? throw new InvalidDataException("Native final usage report is empty.");
        if (
            ReportSessionPointer is not null
            && At(node, ReportSessionPointer)?.GetValue<string>() != sessionId
        )
        {
            throw new OperationException(
                "credit_unknown",
                "Final native usage does not identify the reserved session."
            );
        }

        decimal actual = Number(node, ReportCreditsPointer);
        if (actual < 0)
        {
            throw new InvalidDataException("Native usage cannot be negative.");
        }

        return (actual, Convert.ToHexString(SHA256.HashData(report)));
    }

    public static NativeCreditContract? Read(RunDatabase database)
    {
        string? stored = database.ReadMetadata("native:credit_contract");
        if (stored is null)
        {
            return null;
        }

        NativeCreditContract result =
            JsonSerializer.Deserialize<NativeCreditContract>(stored, Json.Options)
            ?? throw new InvalidDataException("Native credit evidence is invalid.");
        result.Validate();
        return result;
    }

    private static decimal Number(JsonNode node, string pointer)
    {
        if (At(node, pointer) is not JsonValue value || !value.TryGetValue(out decimal result))
        {
            throw new OperationException(
                "credit_unknown",
                $"Documented native credit field {pointer} is missing or not numeric."
            );
        }

        return result;
    }

    private static JsonNode? At(JsonNode root, string pointer)
    {
        JsonNode? node = root;
        foreach (string part in pointer.Split('/').Skip(1))
        {
            string key = part.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            node =
                node is JsonObject obj ? obj[key]
                : node is JsonArray array
                && int.TryParse(key, out int index)
                && index >= 0
                && index < array.Count
                    ? array[index]
                : null;
            if (node is null)
            {
                break;
            }
        }

        return node;
    }
}
