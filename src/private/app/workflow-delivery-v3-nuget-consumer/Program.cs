using System.Text.Json.Nodes;

namespace WorkflowDeliveryV3NuGetConsumer;

internal static class Program
{
    internal const string CredentialVariable = "WDV3_NUGET_CONSUMER_READ_TOKEN";

    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            ConsumerRequest.Require(
                arguments is ["restore", _],
                "Unsupported consumer host operation."
            );
            ConsumerRequest request = ConsumerRequest.Read(
                await File.ReadAllBytesAsync(arguments[1]).ConfigureAwait(false)
            );
            string credential =
                Environment.GetEnvironmentVariable(CredentialVariable) ?? string.Empty;
            Environment.SetEnvironmentVariable(CredentialVariable, null);
            // This process only restores. It never evaluates, builds or invokes a product.
            JsonObject result = await NativeRestore
                .RunAsync(request, credential)
                .ConfigureAwait(false);
            Console.WriteLine(result.ToJsonString());
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine("NuGet consumer restore failed: " + error.GetType().Name);
            return 1;
        }
    }
}
