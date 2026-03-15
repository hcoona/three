using System.Reflection;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class InstructionTemplateProvider
{
    private readonly Lazy<string> content = new(
        ReadContent,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public string GetTemplate() => content.Value;

    private static string ReadContent()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using Stream? stream = assembly.GetManifestResourceStream(
            AppConstants.ManagedInstructionLogicalName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded instruction resource '{AppConstants.ManagedInstructionLogicalName}' "
                + "could not be found.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
