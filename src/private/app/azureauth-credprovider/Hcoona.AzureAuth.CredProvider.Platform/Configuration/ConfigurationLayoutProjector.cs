using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal static class ConfigurationLayoutProjector
{
    private const string ProductDirectoryName = "azureauth-credprovider";
    private const string NuGetNetCorePluginEntrypointFileName = "azureauth-credprovider.dll";
    private const string WindowsProductCompanyDirectoryName = "AzureAuth";
    private const string WindowsProductNameDirectoryName = "CredProvider";
    private const string NuGetPluginEnvironmentVariable = "NUGET_PLUGIN_PATHS";
    private const string NetCorePluginEnvironmentVariable = "NUGET_NETCORE_PLUGIN_PATHS";

    public static IReadOnlyList<ConfigurationTargetLayoutProjection> ProjectTargets(
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return
        [
            ProjectGitConfig(context),
            ProjectNuGetPluginLayout(context),
            ProjectPythonKeyringBackend(context),
            ProjectKeyringShim(context),
        ];
    }

    public static ConfigurationTargetLayoutProjection ProjectGitConfig(
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        string xdgGitConfigPath = Combine(
            context.Platform,
            GetXdgConfigHome(context),
            "git",
            "config"
        );
        string homeGitConfigPath = Combine(
            context.Platform,
            GetHomeDirectory(context),
            ".gitconfig"
        );
        string targetPath = context.FileExists(homeGitConfigPath)
            ? homeGitConfigPath
            : context.FileExists(xdgGitConfigPath)
                ? xdgGitConfigPath
                : homeGitConfigPath;

        return new ConfigurationTargetLayoutProjection
        {
            TargetKind = ConfigurationTargetKind.GitConfig,
            TargetPath = targetPath,
            ProductDataRoot = GetProductDataRoot(context),
            ProductConfigRoot = GetProductConfigRoot(context),
            ProjectedPaths = [targetPath],
            ActivationGuidance =
            [
                "Use file-level Git config writers for the private product config and the "
                    + "explicitly marked include block in the selected user config file; "
                    + "do not invoke the git config CLI.",
            ],
        };
    }

    public static ConfigurationTargetLayoutProjection ProjectNuGetPluginLayout(
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        string productDataRoot = GetProductDataRoot(context);
        string productConfigRoot = GetProductConfigRoot(context);
        string pluginRoot = Combine(
            context.Platform,
            GetHomeDirectory(context),
            ".nuget",
            "plugins"
        );
        string netCorePluginPath = Combine(
            context.Platform,
            pluginRoot,
            "netcore",
            ProductDirectoryName
        );
        string netCorePluginEntrypointPath = Combine(
            context.Platform,
            netCorePluginPath,
            NuGetNetCorePluginEntrypointFileName
        );

        return new ConfigurationTargetLayoutProjection
        {
            TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
            TargetPath = netCorePluginPath,
            ProductDataRoot = productDataRoot,
            ProductConfigRoot = productConfigRoot,
            ProjectedPaths =
            [
                pluginRoot,
                netCorePluginPath,
                netCorePluginEntrypointPath,
                Combine(context.Platform, productConfigRoot, "nuget-plugin", "manifest.json"),
            ],
            ActivationGuidance =
            [
                "Install under NuGet's official per-user plugin convention directory with the "
                    + "netcore plugin entry file at "
                    + netCorePluginEntrypointPath
                    + ".",
                $"Dry-run may mention optional process-scoped {NuGetPluginEnvironmentVariable} or "
                    + $"{NetCorePluginEnvironmentVariable} overrides for diagnostics only.",
                $"Do not persistently mutate {NuGetPluginEnvironmentVariable} or "
                    + $"{NetCorePluginEnvironmentVariable}.",
            ],
            OptionalProcessEnvironmentVariables =
            [
                NuGetPluginEnvironmentVariable,
                NetCorePluginEnvironmentVariable,
            ],
        };
    }

    public static ConfigurationTargetLayoutProjection ProjectPythonKeyringBackend(
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        string productConfigRoot = GetProductConfigRoot(context);
        string manifestPath = Combine(
            context.Platform,
            productConfigRoot,
            "python-keyring",
            "backend-manifest.json"
        );

        return new ConfigurationTargetLayoutProjection
        {
            TargetKind = ConfigurationTargetKind.PythonKeyringBackend,
            TargetPath = manifestPath,
            ProductDataRoot = GetProductDataRoot(context),
            ProductConfigRoot = productConfigRoot,
            ProjectedPaths = [manifestPath],
            ActivationGuidance =
            [
                "Write only the product-owned Python keyring backend manifest.",
                "Do not write the user's keyringrc.cfg.",
            ],
        };
    }

    public static ConfigurationTargetLayoutProjection ProjectKeyringShim(
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        string productDataRoot = GetProductDataRoot(context);
        string shimDirectory = Combine(context.Platform, productDataRoot, "keyring-shim");
        string shimPath = Combine(
            context.Platform,
            shimDirectory,
            context.Platform == ConfigurationLayoutPlatform.Windows ? "keyring.exe" : "keyring"
        );

        return new ConfigurationTargetLayoutProjection
        {
            TargetKind = ConfigurationTargetKind.KeyringShim,
            TargetPath = shimPath,
            ProductDataRoot = productDataRoot,
            ProductConfigRoot = GetProductConfigRoot(context),
            ProjectedPaths = [shimDirectory, shimPath],
            ActivationGuidance =
            [
                "Install the keyring shim under the product-owned root.",
                "Emit activation guidance for adding the shim directory to PATH in the current "
                    + "shell or process.",
                "Do not mutate global PATH, shell profiles, the registry, or other machine/user "
                    + "profile state.",
            ],
        };
    }

    private static string GetProductDataRoot(ConfigurationLayoutProjectionContext context) =>
        context.Platform switch
        {
            ConfigurationLayoutPlatform.Windows => Combine(
                context.Platform,
                RequireNonEmpty(
                    context.LocalAppDataDirectory,
                    nameof(context.LocalAppDataDirectory)
                ),
                WindowsProductCompanyDirectoryName,
                WindowsProductNameDirectoryName
            ),
            ConfigurationLayoutPlatform.Linux => Combine(
                context.Platform,
                GetXdgDataHome(context),
                ProductDirectoryName
            ),
            ConfigurationLayoutPlatform.MacOs => Combine(
                context.Platform,
                GetHomeDirectory(context),
                "Library",
                "Application Support",
                WindowsProductCompanyDirectoryName,
                WindowsProductNameDirectoryName
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(context),
                context.Platform,
                "Unsupported layout projection platform."
            ),
        };

    private static string GetProductConfigRoot(ConfigurationLayoutProjectionContext context) =>
        context.Platform switch
        {
            ConfigurationLayoutPlatform.Windows => GetProductDataRoot(context),
            ConfigurationLayoutPlatform.Linux => Combine(
                context.Platform,
                GetXdgConfigHome(context),
                ProductDirectoryName
            ),
            ConfigurationLayoutPlatform.MacOs => GetProductDataRoot(context),
            _ => throw new ArgumentOutOfRangeException(
                nameof(context),
                context.Platform,
                "Unsupported layout projection platform."
            ),
        };

    private static string GetXdgDataHome(ConfigurationLayoutProjectionContext context) =>
        context.Platform == ConfigurationLayoutPlatform.Linux
            ? NullIfWhiteSpace(context.XdgDataHomeDirectory)
                ?? Combine(context.Platform, GetHomeDirectory(context), ".local", "share")
            : Combine(context.Platform, GetHomeDirectory(context), ".local", "share");

    private static string GetXdgConfigHome(ConfigurationLayoutProjectionContext context) =>
        NullIfWhiteSpace(context.XdgConfigHomeDirectory)
        ?? Combine(context.Platform, GetHomeDirectory(context), ".config");

    private static string GetHomeDirectory(ConfigurationLayoutProjectionContext context) =>
        RequireNonEmpty(context.HomeDirectory, nameof(context.HomeDirectory));

    private static string Combine(
        ConfigurationLayoutPlatform platform,
        string firstSegment,
        params string[] additionalSegments
    )
    {
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string result = TrimTrailingSeparators(firstSegment, separator);
        foreach (string segment in additionalSegments)
        {
            string trimmedSegment = TrimSeparators(segment, separator);
            result =
                result.Length == 1 && result[0] == separator
                    ? string.Concat(result, trimmedSegment)
                    : string.Concat(result, separator, trimmedSegment);
        }

        return result;
    }

    private static string TrimSeparators(string value, char separator) =>
        value.Trim(separator, AlternateSeparator(separator));

    private static string TrimTrailingSeparators(string value, char separator)
    {
        string trimmed = value.TrimEnd(separator, AlternateSeparator(separator));
        return trimmed.Length == 0 ? value : trimmed;
    }

    private static char AlternateSeparator(char separator) => separator == '\\' ? '/' : '\\';

    private static string RequireNonEmpty(string? value, string parameterName) =>
        NullIfWhiteSpace(value)
        ?? throw new ArgumentException(
            "Layout projection path inputs must be non-empty.",
            parameterName
        );

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

internal sealed record ConfigurationLayoutProjectionContext
{
    public required ConfigurationLayoutPlatform Platform { get; init; }
    public required string HomeDirectory { get; init; }
    public string? LocalAppDataDirectory { get; init; }
    public string? XdgDataHomeDirectory { get; init; }
    public string? XdgConfigHomeDirectory { get; init; }
    public Func<string, bool> FileExists { get; init; } = _ => false;
}

internal enum ConfigurationLayoutPlatform
{
    Windows,
    Linux,
    MacOs,
}

internal sealed record ConfigurationTargetLayoutProjection
{
    public required ConfigurationTargetKind TargetKind { get; init; }
    public required string TargetPath { get; init; }
    public required string ProductDataRoot { get; init; }
    public required string ProductConfigRoot { get; init; }
    public IReadOnlyList<string> ProjectedPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ActivationGuidance { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OptionalProcessEnvironmentVariables { get; init; } =
        Array.Empty<string>();
    public IReadOnlyDictionary<string, string> PersistentEnvironmentMutations { get; init; } =
        ContractMetadata.Empty;
    public IReadOnlyList<string> PersistentProfileOrRegistryMutations { get; init; } =
        Array.Empty<string>();
}
