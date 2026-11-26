using System.Globalization;
using System.Text.Json;
using WindowsResultValidation;

// This entry processes one result. Its caller owns process control and the receipt time.
// This entry runs only on Windows. Native inputs stay Windows-local; an admitted
// WSL caller may return its confidential result here through the standard stdin pipe.
try
{
    if (args.Length < 8 || !OperatingSystem.IsWindows()) throw new InvalidResultException();
    uint exit = uint.Parse(args[0], NumberStyles.None, CultureInfo.InvariantCulture);
    DateTimeOffset received = DateTimeOffset.ParseExact(args[1], "O", CultureInfo.InvariantCulture);
    var request = new ResultExpectation
    {
        Email = args[2],
        ExactResultTenant = args[3] == "common" ? null : Guid.ParseExact(args[3], "D"),
        InteractionAllowed = args[4] switch
        {
            "non-interactive-only" => false,
            "interactive-if-needed" => true,
            _ => throw new InvalidResultException(),
        },
        ExpectedOutcome = Enum.Parse<Outcome>(args[5], ignoreCase: false),
        ExpectedRoute = Enum.Parse<Route>(args[6], ignoreCase: false),
        // Only the reviewed Azure DevOps association is admitted by this entry.
        DefaultAssociationIndependentlyAccepted = args[7..].SequenceEqual(
            ["499b84ac-1321-427f-aa17-267ca6975798/.default"], StringComparer.Ordinal),
        Scopes = args[7..],
    };
    byte[] buffer = new byte[1048577];
    try
    {
        using Stream input = Console.OpenStandardInput();
        int count = 0;
        while (count < buffer.Length)
        {
            int read = input.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > 1048576) throw new InvalidResultException();
        ResultChecks checks = ProtocolResult.Validate(buffer.AsMemory(0, count), exit, request, received);
        // Serialize an explicit projection: never serialize the request, arguments or input.
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Outcome = checks.Outcome.ToString(), Route = checks.Route.ToString(),
            checks.Passed, checks.ProtocolValid, checks.MetadataValid,
            checks.PersistenceUnconfirmed, checks.PersistenceFailed,
        }));
        return 0; // Validating a failed authentication is distinct from accepting a scenario.
    }
    finally { Array.Clear(buffer); }
}
catch
{
    Console.WriteLine("{\"ValidationFailed\":true}");
    return 2;
}
