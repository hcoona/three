using WorkflowDelivery.Release;

namespace WorkflowDelivery.Ecosystems;

// Frameworks is the exact native TargetFrameworks set. A unit without Probe is the
// single-framework smoke recipe; every other unit uses the shared multi-target library recipe,
// whose consumer prints Probe and expects the assembly name. Official accepts only units
// marked here.
internal sealed record NuGetReleaseUnit(string Id, string Declaration, string PackageId,
    string AssemblyName, string[] Frameworks, string? Probe, bool Official)
{
    internal bool Library => Probe is not null;
}

// Static registration of the supported concrete NuGet Release recipes.
internal static class NuGetReleaseUnits
{
    internal const string Smoke = "hcoona-release-smoke-github-packages";
    private const string Ordinary = "netstandard2.0;netstandard2.1;net462;net8.0;net9.0";
    private static readonly NuGetReleaseUnit[] Units =
    [
        new(Smoke, "tests/private/app/workflow-delivery/fixtures/products/" + Smoke +
            "/workflow-delivery.release-unit.yml", "Hcoona.ReleaseSmoke.GithubPackages",
            "Hcoona.ReleaseSmoke.GithubPackages", ["net10.0"], null, false),
        Library("circular-list", "CircularList", "IO.Github.Hcoona.Collections.CircularList",
            "CircularList", Ordinary,
            "typeof(IO.Github.Hcoona.Collections.CircularList<int>).Assembly.GetName().Name"),
        Library("hjg-pngcs", "Hjg.Pngcs", "IO.Github.Hcoona.Pngcs", "Hjg.Pngcs",
            "netstandard2.0;netstandard2.1;net8.0;net9.0",
            "typeof(Hjg.Pngcs.PngReader).Assembly.GetName().Name"),
        // The source generator's output is compiled into the library; binding a generated
        // overload proves that the published assembly carries it.
        Library("memoization", "Memoization", "Memoization.Net", "Memoization", Ordinary,
            "new System.Func<System.Func<int, int>, System.Func<int, int>>(" +
            "Memoization.Memoization.Create<int, int>).Method.DeclaringType!.Assembly" +
            ".GetName().Name"),
        Library("microsoft-extensions-logging-mstest", "MicrosoftExtensions.Logging.MSTest",
            "IO.Github.Hcoona.MicrosoftExtensions.Logging.MSTest",
            "MicrosoftExtensions.Logging.MSTest", Ordinary,
            "typeof(Microsoft.Extensions.Logging.MSTest.MsTestLoggerProvider).Assembly" +
            ".GetName().Name"),
        Library("microsoft-extensions-logging-xunit", "MicrosoftExtensions.Logging.Xunit",
            "MicrosoftExtensions.Logging.Xunit", "MicrosoftExtensions.Logging.Xunit", Ordinary,
            "typeof(Microsoft.Extensions.Logging.Xunit.XunitLoggerProvider).Assembly" +
            ".GetName().Name"),
        Library("microsoft-extensions-options-dedup-change-extensions",
            "MicrosoftExtensions.Options.DedupChangeExtensions",
            "MicrosoftExtensions.Options.DedupChangeExtensions",
            "MicrosoftExtensions.Options.DedupChangeExtensions", Ordinary,
            "typeof(Microsoft.Extensions.Options.OptionsDedupChangeExtensions).Assembly" +
            ".GetName().Name"),
        Library("phi-failure-detector", "PhiFailureDetector", "PhiFailureDetector",
            "PhiFailureDetector", Ordinary,
            "typeof(global::PhiFailureDetector.PhiFailureDetector).Assembly.GetName().Name"),
        Library("webhdfs-extensions-file-providers", "WebHdfs.Extensions.FileProviders",
            "WebHdfs.Extensions.FileProviders", "WebHdfs.Extensions.FileProviders", Ordinary,
            "typeof(WebHdfs.Extensions.FileProviders.WebHdfsFileProvider).Assembly" +
            ".GetName().Name"),
    ];

    internal static IEnumerable<NuGetReleaseUnit> All => Units;

    // Source directories whose changes can alter a registered unit's prepared package: each
    // declaration's project directory and the generator that Memoization compiles in.
    internal static string[] PreparationRoots => [.. Units.Select(unit =>
        unit.Declaration[..unit.Declaration.LastIndexOf('/')]),
        "src/public/lib/Memoization.Generators"];

    internal static bool Registered(string id) => Units.Any(unit => unit.Id == id);

    internal static NuGetReleaseUnit Named(string id) =>
        Units.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported NuGet Release unit.");

    // The request-path unit check, before any preparation work.
    internal static NuGetReleaseUnit Admit(ReleaseSubject subject)
    {
        NuGetReleaseUnit unit = Named(subject.Unit);
        if (subject.Declaration != unit.Declaration ||
            (subject.Channel, subject.Destination) is not (("buddy", "github-packages-nuget") or
                ("official", "nugetorg")))
            throw new InvalidDataException("Unsupported NuGet Release request.");
        if (subject.Channel == "official" && !unit.Official)
            throw new InvalidDataException("Official NuGet Release does not accept this unit.");
        return unit;
    }

    private static NuGetReleaseUnit Library(string id, string directory, string package,
        string assembly, string frameworks, string probe) => new(id,
        "src/public/lib/" + directory + "/workflow-delivery.release-unit.yml", package,
        assembly, frameworks.Split(';'), probe, true);
}
