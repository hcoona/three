using WorkflowDelivery.Release;

namespace WorkflowDelivery.Ecosystems;

// The distribution name equals the unit id. A unit with a test path also requires its adopted
// pytest suite in Release preparation.
internal sealed record PythonReleaseUnit(string Id, string Declaration, bool StaticVersion,
    bool RuntimeClosure, string? Tests);

// Static registration of the supported concrete Python Release recipes.
internal static class PythonReleaseUnits
{
    internal const string Smoke = "hcoona-release-smoke-python";
    internal const string Nbgv = "nbgv-python";
    private static readonly PythonReleaseUnit[] Units =
    [
        new(Smoke, "tests/private/app/workflow-delivery/fixtures/products/" + Smoke +
            "/workflow-delivery.release-unit.yml", StaticVersion: false, RuntimeClosure: false,
            Tests: null),
        new(Nbgv, "src/public/lib/" + Nbgv + "/workflow-delivery.release-unit.yml",
            StaticVersion: true, RuntimeClosure: true, Tests: "src/public/lib/" + Nbgv + "/tests"),
    ];

    internal static bool Registered(string id) => Units.Any(unit => unit.Id == id);

    internal static PythonReleaseUnit Named(string id) =>
        Units.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported Python Release unit.");

    // The request-path unit check, before any preparation work. Every registered unit
    // accepts both channels.
    internal static PythonReleaseUnit Admit(ReleaseSubject subject) => Named(subject.Unit);

    internal static PythonPackageOperations Package(string id, PythonPackageTools tools,
        string root) => new(Named(id), tools, root);
}
