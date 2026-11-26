namespace Authentication.Core;

// The product version that Nerdbank.GitVersioning computes from version.json and the
// Git history of the build. Builds outside the public-release ref carry the tool's
// commit-ID prerelease suffix, so they never collide with a main-v2 version.
public static class ProductVersion
{
    // SemVer 2.0 without build metadata, for product tokens such as User-Agent.
    public const string Semantic = ThisAssembly.NuGetPackageVersion;

    // SemVer 2.0 with the abbreviated source commit as build metadata.
    public static readonly string Full = Semantic + "+" + ThisAssembly.GitCommitId[..10];
}
