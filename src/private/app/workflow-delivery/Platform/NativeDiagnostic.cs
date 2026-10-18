namespace WorkflowDelivery.Platform;

// Bounded caller-visible text for a failed native command; the complete result stays in
// the retained command observation.
internal static class NativeDiagnostic
{
    internal const int TailLength = 4096;

    // The tool and its first operation word, omitting options and absolute local paths.
    internal static string Operation(NativeCommand command) => string.Join(' ',
        new[] { Path.GetFileNameWithoutExtension(command.Executable) }.Concat(
            command.Arguments.Where(argument => !argument.StartsWith('-') &&
                !Path.IsPathRooted(argument)).Take(1)));

    // "<termination>, exit <code>." followed by the native error and a bounded stderr tail.
    internal static string Describe(NativeCommandResult result)
    {
        string text = $"{result.Termination}, exit {result.ExitCode?.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}.";
        if (!string.IsNullOrWhiteSpace(result.Error)) text += " " + Tail(result.Error);
        string stderr = Tail(result.Stderr);
        return stderr.Length == 0 ? text : text + " stderr: " + stderr;
    }

    // The last TailLength characters, trimmed, marked when earlier text was omitted.
    internal static string Tail(string? text)
    {
        string trimmed = (text ?? "").Trim();
        if (trimmed.Length <= TailLength) return trimmed;
        int start = trimmed.Length - TailLength;
        if (char.IsLowSurrogate(trimmed[start])) start++;
        return "[truncated] " + trimmed[start..].TrimStart();
    }
}
