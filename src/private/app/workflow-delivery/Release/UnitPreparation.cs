using WorkflowDelivery.Core;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal static class UnitPreparation
{
    internal static async Task<ReleaseFile[]> QualifyAsync(ReleaseSubject subject,
        ReleaseBuild? declaration, string scratch, ReleaseQualification qualification,
        string[] required, Func<UnitBuildSubject, Task<QualifiedUnitOriginals?>> produce)
    {
        if (qualification.Errors.Length != 0)
        {
            foreach (string check in required.Skip(1))
                await qualification.CheckAsync(check, () => Task.CompletedTask);
            return [];
        }
        ReleaseBuild build = declaration ?? throw new InvalidDataException("Missing native build.");
        var unit = new UnitBuildSubject(subject.Revision, subject.Unit, subject.Declaration,
            build.Id, build.EntryPoint,
            [.. build.Outputs.Select(o => new UnitOutput(o.Id, o.Role, o.Kind))]);
        QualifiedUnitOriginals? result = await produce(unit);
        if (qualification.Errors.Length != 0) return [];
        if (result is null || result.Subject != unit)
            throw new InvalidDataException("Native qualification returned a different unit.");
        return [.. result.Files.Select(file => new ReleaseFile(build.Id, file.Output.Id,
            file.Output.Kind, file.Name, file.Version, OwnedPath.RelativeWithin(scratch, file.Path,
                "Qualified original is outside the prepared unit."), file.Size, file.Sha256))];
    }
}
