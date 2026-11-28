using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WorkflowDelivery.Core;

internal sealed record UnitOutput(string Id, string Role, string Kind);
internal sealed record UnitBuildSubject(string Revision, string Unit, string Declaration,
    string Build, string EntryPoint, UnitOutput[] Outputs)
{
    internal UnitOutput ForKind(string kind) => Outputs.Single(output => output.Kind == kind);

    internal QualifiedUnitOriginals Complete(params UnitOriginal[] files)
    {
        var outputs = new HashSet<UnitOutput>(files.Select(file => file.Output));
        if (outputs.Count == 0 || outputs.Count != files.Length || files.Length != Outputs.Length ||
            !outputs.SetEquals(Outputs))
            throw new InvalidDataException(
                "Native qualification did not return the complete unit.");
        var paths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (UnitOriginal file in files)
            if (!Path.IsPathFullyQualified(file.Path) || !paths.Add(Path.GetFullPath(file.Path)) ||
                string.IsNullOrWhiteSpace(file.Name) || string.IsNullOrWhiteSpace(file.Version) ||
                file.Size <= 0 || file.Sha256.Length != 64 ||
                !file.Sha256.All(char.IsAsciiHexDigitLower))
                throw new InvalidDataException(
                    "Native qualification returned an incomplete or aliased original.");
        return new(this, files);
    }
}

internal sealed record UnitOriginal(UnitOutput Output, string Name, string Version,
    string Path, long Size, string Sha256);
internal sealed record QualifiedUnitOriginals(UnitBuildSubject Subject, UnitOriginal[] Files);

// The caller records independent CI/Release evidence; skipped or failed checks produce no set.
internal delegate Task RecordNativeCheck(string name, Func<Task> operation);
