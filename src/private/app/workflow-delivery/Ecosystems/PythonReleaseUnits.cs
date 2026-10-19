using WorkflowDelivery.Release;

namespace WorkflowDelivery.Ecosystems;

// The distribution name equals the unit id. Official accepts only units marked here; a unit
// with a test path also requires its adopted pytest suite in Release preparation.
internal sealed record PythonReleaseUnit(string Id, string Declaration, bool Official,
    string? Tests);

// Static registration of the supported concrete Python Release recipes.
internal static class PythonReleaseUnits
{
    internal const string Smoke = "hcoona-release-smoke-python";
    internal const string Nbgv = "nbgv-python";
    private static readonly PythonReleaseUnit[] Units =
    [
        new(Smoke, "tests/private/app/workflow-delivery/fixtures/products/" + Smoke +
            "/workflow-delivery.release-unit.yml", true, null),
        new(Nbgv, "src/public/lib/" + Nbgv + "/workflow-delivery.release-unit.yml", false,
            "src/public/lib/" + Nbgv + "/tests"),
    ];

    internal static bool Registered(string id) => Units.Any(unit => unit.Id == id);

    internal static PythonReleaseUnit Named(string id) =>
        Units.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported Python Release unit.");

    // The request-path unit check, before any preparation work.
    internal static PythonReleaseUnit Admit(ReleaseSubject subject)
    {
        PythonReleaseUnit unit = Named(subject.Unit);
        if (subject.Channel == "official" && !unit.Official)
            throw new InvalidDataException("Official Python Release does not accept this unit.");
        return unit;
    }

    internal static PythonSmokePackageOperations Package(string id, PythonPackageTools tools,
        string root) => id switch
        {
            Smoke => new PythonSmokePackageOperations(tools, root),
            Nbgv => new PythonNbgvPackageOperations(tools, root),
            _ => throw new InvalidDataException("Unsupported Python Release unit."),
        };
}
