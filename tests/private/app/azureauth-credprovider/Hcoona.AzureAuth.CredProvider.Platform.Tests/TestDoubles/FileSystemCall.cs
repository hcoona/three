namespace Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;

public sealed record FileSystemCall(string Operation, string Path, string? Value = null);
