using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

// The gem name equals the unit id.
internal sealed record RubyReleaseUnit(string Id, string Declaration, string Preset);

// Static registration of the supported concrete Ruby Release recipes.
internal static class RubyReleaseUnits
{
    internal const string Smoke = "hcoona-release-smoke-ruby";
    internal const string Latexmath = "asciidoctor-latexmath";
    private static readonly RubyReleaseUnit[] Units =
    [
        new(Smoke, "tests/private/app/workflow-delivery/fixtures/products/" + Smoke +
            "/workflow-delivery.release-unit.yml", RubyPackageChecks.Preset),
        new(Latexmath, "src/public/lib/" + Latexmath + "/workflow-delivery.release-unit.yml",
            RubyPackageChecks.RSpecPreset),
    ];

    internal static bool Registered(string id) => Units.Any(unit => unit.Id == id);

    internal static RubyReleaseUnit Named(string id) =>
        Units.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported Ruby Release unit.");

    // The request-path unit check, before any preparation work. Every registered unit
    // accepts both channels.
    internal static RubyReleaseUnit Admit(ReleaseSubject subject) => Named(subject.Unit);

    internal static RubyPackageOperations Package(RubyReleaseUnit unit, RubyFactTools tools,
        string gem, string nbgv, string state) => new(unit, tools, gem, nbgv, state);
}
