using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Release;

internal static class DestinationBindings
{
    internal static PackageDestination NuGet(PreparedRelease prepared) =>
        new(prepared.Subject, prepared.Files, ReleaseUnitCatalog.AdmitNuGet(prepared.Subject)
            .PackageId);

    internal static PackageDestination Npm(PreparedRelease prepared)
    {
        NpmReleaseUnit unit = ReleaseUnitCatalog.AdmitNpm(prepared.Subject);
        return new(prepared.Subject, prepared.Files,
            prepared.Subject.Channel == "buddy" ? unit.BuddyName : unit.OfficialName);
    }

    internal static PackageDestination Python(PreparedRelease prepared) =>
        new(prepared.Subject, prepared.Files, ReleaseUnitCatalog.AdmitPython(prepared.Subject).Id);

    internal static PackageDestination Ruby(PreparedRelease prepared) =>
        new(prepared.Subject, prepared.Files, ReleaseUnitCatalog.AdmitRuby(prepared.Subject).Id);

    internal static PreparedGithubAnchor GithubAnchor(ReleaseSubject subject,
        IReadOnlyList<ReleaseFile> files, string notes, string? browserVersion = null)
    {
        if (subject.Channel != "official")
            throw new InvalidDataException("A GitHub Release anchor requires an Official request.");
        if (subject.Destination is StoreArtifacts.Chrome or StoreArtifacts.Firefox
            or StoreArtifacts.Edge)
        {
            if (string.IsNullOrWhiteSpace(browserVersion))
                throw new InvalidDataException("Missing browser anchor version.");
            var archives = BrowserExtensionPackage.Archives(subject.Unit, browserVersion)
                .ToDictionary(item => item.Output.Id, StringComparer.Ordinal);
            ReleaseFile Asset(ReleaseFile file)
            {
                if (!archives.TryGetValue(file.Output, out var item) ||
                    item.Output.Kind != file.Kind ||
                    item.Name != Path.GetFileName(file.Path))
                    throw new InvalidDataException(
                        "Browser anchor differs from its qualified archive.");
                return file with { Name = item.Name, Version = browserVersion };
            }
            string[] selected = StoreArtifacts.For(subject.Destination);
            return new([.. files.Select(Asset)], notes, [.. archives.Values
                .Where(item => !selected.Contains(item.Output.Id)).Select(item => item.Name)]);
        }
        return new([.. files.Select(file => file with
        {
            Name = subject.Destination switch
            {
                "npmjs" => $"{NpmReleaseUnit.UnscopedName(file.Name)}-{file.Version}.tgz",
                "pypi" => Path.GetFileName(file.Path),
                "rubygems" => $"{file.Name}-{file.Version}.gem",
                _ => throw new InvalidDataException(
                    "This destination has no GitHub Release anchor."),
            }
        })], notes);
    }

    internal static GithubReleaseTarget GithubRelease(PreparedRelease prepared,
        IReadOnlyCollection<string>? allowedSiblingAssets = null)
    {
        if (prepared.Files.Length == 0)
            throw new InvalidDataException("A GitHub Release requires prepared assets.");
        string version = prepared.Files[0].Version;
        return new(prepared.Subject, prepared.Files, GithubReleaseUnit.Tag(prepared.Subject,
            version), GithubReleaseUnit.Title(prepared.Subject.Unit, version),
            prepared.ReleaseNotes, allowedSiblingAssets);
    }
}
