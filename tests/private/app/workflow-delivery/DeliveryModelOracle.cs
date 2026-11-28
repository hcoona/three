namespace WorkflowDelivery.Tests;

internal static class DeliveryModelOracle
{
    internal static IEnumerable<string[]> Rows(string module)
    {
        string directory = Environment.GetEnvironmentVariable("WORKFLOW_DELIVERY_TLC_ORACLES")
            ?? throw new InvalidOperationException(
                "Run eng/scripts/check_workflow_delivery_models.py.");
        return File.ReadLines(
            Path.Combine(directory, module + ".txt")).Select(row => row.Split('|'));
    }
}
