using System.Xml;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities;

namespace WorkflowDelivery.Repository;

internal static class DotNetTaskHost
{
    internal const string ControlRoot = "src/private/app/workflow-delivery";
    internal const string ControlProject = ControlRoot + "/WorkflowDelivery.csproj";

    internal static bool IsControlProject(string path) =>
        path.StartsWith(ControlRoot + "/", StringComparison.Ordinal) &&
        path.EndsWith(".csproj", StringComparison.Ordinal);

    internal static string AssemblyPath => typeof(DotNetTaskHost).Assembly.Location;

    internal static string InDistribution(string application)
    {
        string path = Path.Combine(Path.GetDirectoryName(application)!,
            Path.GetFileName(AssemblyPath));
        return File.Exists(path) ? path :
            throw new InvalidDataException(
                "Candidate control distribution lacks its .NET task assembly.");
    }

    internal static string TestReceipt(string directory, int index) =>
        Path.Combine(directory, "entry-" + index + ".json");

    internal static string Escape(string value) => ProjectCollection.Escape(value);

    internal static string[] Properties(Dictionary<string, string> globals)
    {
        var arguments = new List<string>();
        foreach ((string name, string value) in globals.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
        {
            try { XmlConvert.VerifyNCName(name); }
            catch (XmlException exception)
            {
                throw new InvalidDataException("Unsupported native global name: " + name,
                    exception);
            }
            if (value is null || value.Contains('\0') ||
                (name.Equals("RestoreSources", StringComparison.OrdinalIgnoreCase) &&
                    (value.Contains(';') || value.Contains("://", StringComparison.Ordinal))))
                throw new InvalidDataException("Unsupported native global transport: " + name);
            // These are already native escaped strings. Quote the value without escaping again.
            var builder = new CommandLineBuilder();
            builder.AppendSwitchIfNotNull("-property:" + name + "=", value);
            arguments.Add(builder.ToString());
        }
        return arguments.ToArray();
    }
}
