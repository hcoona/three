using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Ecosystems;

// One concrete npm Release recipe. Buddy and Official names are the published package
// names; preparation sets a Buddy name that differs from the committed source name in the
// disposable checkout immediately before pack. Required is also the native check order.
internal sealed record NpmReleaseUnit(string Id, string Declaration, string SourceName,
    string BuddyName, string OfficialName, bool OfficialLive, string Main,
    bool RegistryDependencies, string[] Required, string Probe)
{
    internal string PackageName(ReleaseSubject subject) =>
        subject.Channel == "buddy" ? BuddyName : OfficialName;

    // Runs from the consumer created by NpmPackageOperations.InstallAsync.
    internal NativeCommand Import(string directory, string package) => new("node",
        Path.Combine(directory, "consumer"),
        ["--input-type=module", "--eval", Probe.Replace("@@PACKAGE@@", package,
            StringComparison.Ordinal)], 60);
}

// Static registration of the supported concrete npm Release recipes.
internal static class NpmReleaseUnits
{
    internal const string Smoke = "hcoona-release-smoke-npm";
    internal const string Hexo = "hexo-renderer-asciidoc";
    private static readonly NpmReleaseUnit[] Units =
    [
        new(Smoke, "tests/private/app/workflow-delivery/fixtures/products/" + Smoke +
                "/workflow-delivery.release-unit.yml", AdoptedNpmPackage.PackageName,
            AdoptedNpmPackage.PackageName, AdoptedNpmPackage.PackageName, false,
            "./dist/index.js", false,
            ["native-input", "build", "test", "pack", "contents", "consumer", "original-bytes"],
            "import { strict as assert } from 'node:assert';"
                + "import { smokeMessage } from '@@PACKAGE@@';"
                + "assert.equal(smokeMessage(), 'hcoona-release-smoke-npm');"),
        // The product tests build, pack and remove dist themselves, so the release build
        // follows them.
        new(Hexo, "src/public/lib/" + Hexo + "/workflow-delivery.release-unit.yml", Hexo,
            "@hcoona/" + Hexo, Hexo, true, "./dist/index.cjs", true,
            ["native-input", "test", "build", "pack", "contents", "consumer", "original-bytes"],
            "import { strict as assert } from 'node:assert';"
                + "import { createRequire } from 'node:module';"
                + "const plugin = await import('@@PACKAGE@@');"
                + "assert.equal(typeof plugin.default, 'function');"
                + "assert.equal(typeof plugin.registerRenderer, 'function');"
                + "const required = createRequire(import.meta.url)('@@PACKAGE@@');"
                + "assert.equal(typeof required.registerRenderer, 'function');"
                + "const html = await plugin.default({ text: '== Probe' });"
                + "assert.ok(html.includes('<h2 id=\"_probe\">Probe</h2>'), html);"),
    ];

    internal static NpmReleaseUnit Named(string id) =>
        Units.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported npm Release unit.");

    // The request-path unit check, before any preparation work.
    internal static NpmReleaseUnit Admit(ReleaseSubject subject)
    {
        NpmReleaseUnit unit = Named(subject.Unit);
        bool buddy = subject.Channel == "buddy" && subject.Destination == "github-packages-npm";
        bool official = subject.Channel == "official" && subject.Destination == "npmjs";
        if (!(buddy || official))
            throw new InvalidDataException("Unsupported selected npm destination and mode.");
        if (official && !subject.DryRun && !unit.OfficialLive)
            throw new InvalidDataException("Official npm Release does not accept this unit.");
        ReleasePolicy.RequireEligible(subject);
        return unit;
    }
}
