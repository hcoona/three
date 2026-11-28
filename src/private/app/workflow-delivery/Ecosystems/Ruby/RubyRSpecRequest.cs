namespace WorkflowDelivery.CI;

internal sealed record RubyRSpecRequest(string Checkout, string Ruby, string Bundle,
    string BundlerVersion, string Gemfile, string Scratch, int DeadlineSeconds,
    IReadOnlyDictionary<string, string?> Environment);
