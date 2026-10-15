namespace WorkflowDelivery.Repository;

// Concrete native helper tests consume their implementation, generated wire and product fixture.
internal static class RubyNativeTestInputs
{
    internal const string Directory = "tests/private/app/workflow-delivery/Native/Ruby";
    internal static bool Consumes(string path) =>
        path.StartsWith("src/private/app/workflow-delivery/Native/Ruby/",
            StringComparison.Ordinal) ||
        (path.StartsWith("src/private/app/workflow-delivery/Native/contracts/ruby-",
            StringComparison.Ordinal) && path.EndsWith(".schema.json", StringComparison.Ordinal)) ||
        path.StartsWith("tests/private/app/workflow-delivery/fixtures/products/" +
            "hcoona-release-smoke-ruby/", StringComparison.Ordinal);
}
