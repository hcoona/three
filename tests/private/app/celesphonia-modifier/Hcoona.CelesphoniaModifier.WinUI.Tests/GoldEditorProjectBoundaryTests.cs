using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Hcoona.CelesphoniaModifier.WinUI.Tests;

public sealed partial class GoldEditorProjectBoundaryTests
{
    private static readonly string[] ExpectedApplicationFiles =
    [
        "App.xaml",
        "App.xaml.cs",
        "app.manifest",
        "GoldEditorOperations.cs",
        "GoldEditorViewModel.cs",
        "Hcoona.CelesphoniaModifier.WinUI.csproj",
        "MainWindow.xaml",
        "MainWindow.xaml.cs",
        "packages.lock.json",
    ];

    private static readonly string[] ExpectedTestFiles =
    [
        "GoldEditorOperationsTests.cs",
        "GoldEditorProjectBoundaryTests.cs",
        "GoldEditorViewModelTests.cs",
        "Hcoona.CelesphoniaModifier.WinUI.Tests.csproj",
        "packages.lock.json",
        "SyntheticGoldSave.cs",
    ];

    private static readonly string[] ExpectedTestPackages =
    [
        "coverlet.collector",
        "Microsoft.NET.Test.Sdk",
        "Microsoft.Testing.Extensions.CodeCoverage",
        "Microsoft.Testing.Extensions.TrxReport",
        "xunit.runner.visualstudio",
        "xunit.v3.mtp-v2",
    ];

    private const string AppProjectReference =
        @"$(WinUIRoot)\Hcoona.CelesphoniaModifier.WinUI.csproj";
    private const string AtlasProjectReference =
        @"$(AtlasRoot)\Hcoona.CelesphoniaModifier.Atlas.csproj";
    private const string TelemetryHookId = "98058041-B5B6-4A75-9834-58E6DF796A22";

    [Fact]
    public void ProjectFileInventoryIsExact()
    {
        ProjectPaths paths = ProjectPaths.Create();

        Assert.Equal(
            ExpectedApplicationFiles.Order(StringComparer.Ordinal),
            GetFileNames(paths.ApplicationDirectory));
        Assert.Equal(
            ExpectedTestFiles.Order(StringComparer.Ordinal),
            GetFileNames(paths.TestDirectory));
    }

    [Fact]
    public void ApplicationProjectShapeAndDependenciesAreExact()
    {
        XDocument project = XDocument.Load(ProjectPaths.Create().ApplicationProject);

        Assert.Equal(
            ["Microsoft.Windows.SDK.BuildTools", "Microsoft.WindowsAppSDK"],
            GetIncludes(project, "PackageReference"));
        Assert.Equal(
            [
                @"..\Hcoona.CelesphoniaModifier.Atlas\"
                    + "Hcoona.CelesphoniaModifier.Atlas.csproj",
            ],
            GetIncludes(project, "ProjectReference"));
        Assert.Equal(
            ["Hcoona.CelesphoniaModifier.WinUI.Tests"],
            GetIncludes(project, "InternalsVisibleTo"));

        Assert.Equal(
            "net10.0-windows10.0.22000.0",
            GetProperty(project, "TargetFramework"));
        Assert.Equal("10.0.17763.0", GetProperty(project, "TargetPlatformMinVersion"));
        Assert.Equal("x64", GetProperty(project, "Platforms"));
        Assert.Equal("x64", GetProperty(project, "PlatformTarget"));
        Assert.Equal("win-x64", GetProperty(project, "RuntimeIdentifier"));
        Assert.Equal("true", GetProperty(project, "SelfContained"));
        Assert.Equal("true", GetProperty(project, "UseWinUI"));
        Assert.Equal("true", GetProperty(project, "WindowsAppSDKSelfContained"));
        Assert.Equal("None", GetProperty(project, "WindowsPackageType"));
        Assert.Equal("false", GetProperty(project, "InvariantGlobalization"));
        Assert.Equal("en-US", GetProperty(project, "DefaultLanguage"));
        Assert.Equal("en-US", GetProperty(project, "NeutralLanguage"));
        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName is "PublishAot" or "PublishSingleFile");
    }

    [Fact]
    public void TestProjectShapeDependenciesAndTelemetryGuardAreExact()
    {
        XDocument project = XDocument.Load(ProjectPaths.Create().TestProject);

        Assert.Equal(
            ExpectedTestPackages.Order(StringComparer.Ordinal),
            GetIncludes(project, "PackageReference"));
        Assert.Equal(
            [AtlasProjectReference, AppProjectReference],
            GetIncludes(project, "ProjectReference"));
        Assert.Equal(
            "net10.0-windows10.0.22000.0",
            GetProperty(project, "TargetFramework"));
        Assert.Equal("10.0.17763.0", GetProperty(project, "TargetPlatformMinVersion"));
        Assert.Equal("x64", GetProperty(project, "Platforms"));
        Assert.Equal("x64", GetProperty(project, "PlatformTarget"));
        Assert.Equal("win-x64", GetProperty(project, "RuntimeIdentifier"));
        Assert.Equal("true", GetProperty(project, "SelfContained"));

        XElement removal = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TestingPlatformBuilderHook"
                && element.Attribute("Remove") is not null);
        XElement guard = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "RejectTestingPlatformTelemetry");
        Assert.Equal(TelemetryHookId, removal.Attribute("Remove")?.Value);
        Assert.Equal("CoreCompile", guard.Attribute("BeforeTargets")?.Value);
        Assert.Contains(
            "Microsoft.Testing.Extensions.Telemetry",
            guard.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TraversalTreatsApplicationAndTestsAsWindowsOnly()
    {
        XDocument traversal = XDocument.Load(ProjectPaths.Create().TraversalProject);
        string[] windowsOnly = GetIncludes(traversal, "WindowsOnlyProjectReference");

        Assert.Contains(
            "src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.WinUI/"
                + "Hcoona.CelesphoniaModifier.WinUI.csproj",
            windowsOnly);
        Assert.Contains(
            "tests/private/app/celesphonia-modifier/"
                + "Hcoona.CelesphoniaModifier.WinUI.Tests/"
                + "Hcoona.CelesphoniaModifier.WinUI.Tests.csproj",
            windowsOnly);
        Assert.All(
            traversal
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "WindowsOnlyProjectReference"
                    && element.Attribute("Include")?.Value.Contains(
                        "CelesphoniaModifier.WinUI",
                        StringComparison.Ordinal) == true),
            element => Assert.Equal(
                "win-x64",
                element.Elements().Single(child =>
                    child.Name.LocalName == "RuntimeIdentifier").Value));

        XElement[] nonWindowsReferences = traversal
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "ItemGroup"
                && element.Attribute("Condition")?.Value == "'$(OS)' != 'Windows_NT'")
            .Elements()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .ToArray();
        Assert.Equal(2, nonWindowsReferences.Length);
        Assert.All(
            nonWindowsReferences,
            element => Assert.Equal(
                "@(WindowsOnlyProjectReference)",
                element.Attribute("Exclude")?.Value));

        XElement restoreTarget = traversal
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value
                    == "RestoreWindowsOnlyProjectsOnNonWindows");
        string restoreText = restoreTarget.ToString();
        Assert.Contains("EnableWindowsTargeting=true", restoreText, StringComparison.Ordinal);
        Assert.Contains(
            "RestoreLockedMode=$(RestoreLockedMode)",
            restoreText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PickerUsesWindowsAppSdkPathResultAndNotLegacyPicker()
    {
        string source = File.ReadAllText(ProjectPaths.Create().MainWindowCode);

        Assert.Contains(
            "using Microsoft.Windows.Storage.Pickers;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("new(AppWindow.Id)", source, StringComparison.Ordinal);
        Assert.Contains("PickSingleFileAsync()", source, StringComparison.Ordinal);
        Assert.Contains("result.Path", source, StringComparison.Ordinal);
        Assert.Contains("FileTypeFilter.Add(\".rpgsave\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "using Windows.Storage.Pickers;",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Windows.Storage.Pickers.FileOpenPicker",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void XamlUsesRequiredStandardControlsBindingsAndAutomationContracts()
    {
        ProjectPaths paths = ProjectPaths.Create();
        string xaml = File.ReadAllText(paths.MainWindowXaml);
        string code = File.ReadAllText(paths.MainWindowCode);
        string combined = xaml + "\n" + code;

        foreach (string label in new[] { "Save slot", "Current Gold", "New Gold" })
        {
            Assert.Contains($"Text=\"{label}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("<TextBox", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NewGoldTextBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<NumberBox", xaml, StringComparison.Ordinal);
        Assert.Contains("<InfoBar", xaml, StringComparison.Ordinal);
        Assert.Contains("<ProgressRing", xaml, StringComparison.Ordinal);
        Assert.Contains("ContentDialog dialog", code, StringComparison.Ordinal);
        Assert.Contains("IsPrimaryButtonEnabled = false", code, StringComparison.Ordinal);
        Assert.Contains("XamlRoot = RootContent.XamlRoot", code, StringComparison.Ordinal);
        Assert.Contains(
            "This is a Celesphonia v1.05 Steam build 13624401 save, the game is closed,",
            code,
            StringComparison.Ordinal);

        string[] automationIds =
        [
            "ExperimentalWarningInfoBar",
            "SaveSlotPathTextBox",
            "BrowseButton",
            "CurrentGoldText",
            "NewGoldTextBox",
            "GoldValidationText",
            "ApplyGoldButton",
            "CancelOperationButton",
            "OperationProgressRing",
            "OperationStatusText",
            "ResultInfoBar",
            "ConfirmationCheckBox",
        ];
        foreach (string automationId in automationIds)
        {
            Assert.Contains(automationId, combined, StringComparison.Ordinal);
        }

        Assert.Contains("Key=\"O\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"S\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)",
            code,
            StringComparison.Ordinal);

        MatchCollection bindings = XBindExpression().Matches(xaml);
        Assert.NotEmpty(bindings);
        Assert.All(
            bindings.Cast<Match>(),
            binding => Assert.Contains(
                "Mode=",
                binding.Value,
                StringComparison.Ordinal));
    }

    [Fact]
    public void LayoutThemeWindowAndCloseDeferralContractsArePresent()
    {
        ProjectPaths paths = ProjectPaths.Create();
        string xaml = File.ReadAllText(paths.MainWindowXaml);
        string code = File.ReadAllText(paths.MainWindowCode);

        Assert.Single(Regex.Matches(xaml, "<ScrollViewer\\b").Cast<Match>());
        Assert.Contains(
            "HorizontalScrollBarVisibility=\"Disabled\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ControlTemplate", xaml, StringComparison.Ordinal);
        Assert.DoesNotMatch(HardCodedColor(), xaml);
        Assert.Contains("{ThemeResource", xaml, StringComparison.Ordinal);

        Assert.Contains("InitialWidthDips = 720", code, StringComparison.Ordinal);
        Assert.Contains("InitialHeightDips = 680", code, StringComparison.Ordinal);
        Assert.Contains("GetDpiForWindow", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Resize", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Closing +=", code, StringComparison.Ordinal);
        Assert.Contains("args.Cancel = true", code, StringComparison.Ordinal);
        Assert.Contains("_closePending = true", code, StringComparison.Ordinal);
        Assert.Contains("ViewModel.RequestCancellation()", code, StringComparison.Ordinal);
        Assert.Contains("_allowClose = true", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionBoundaryAddsNoExcludedSubsystemOrDependency()
    {
        ProjectPaths paths = ProjectPaths.Create();
        string production = string.Join(
            "\n",
            Directory
                .EnumerateFiles(paths.ApplicationDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path) is ".cs" or ".xaml" or ".csproj")
                .Select(File.ReadAllText));
        string[] forbidden =
        [
            "Hcoona.CelesphoniaModifier.Atlas.Cli",
            "global.rpgsave",
            "config.rpgsave",
            "journal",
            "ledger",
            "restore service",
            "cleanup service",
            "HttpClient",
            "Microsoft.Extensions.Hosting",
            "Serilog",
            "TelemetryClient",
            "ApplicationData.Current",
        ];

        foreach (string value in forbidden)
        {
            Assert.DoesNotContain(value, production, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string[] GetFileNames(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
    }

    private static string[] GetIncludes(XDocument document, string localName)
    {
        return document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == localName
                && element.Attribute("Include") is not null)
            .Select(element => element.Attribute("Include")!.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetProperty(XDocument project, string localName)
    {
        return project
            .Descendants()
            .Single(element => element.Name.LocalName == localName)
            .Value;
    }

    [GeneratedRegex(@"\{x:Bind[^}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex XBindExpression();

    [GeneratedRegex(
        @"#[0-9a-fA-F]{3,8}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex HardCodedColor();

    private sealed record ProjectPaths(
        string RepositoryRoot,
        string ApplicationDirectory,
        string TestDirectory)
    {
        internal string ApplicationProject => Path.Combine(
            ApplicationDirectory,
            "Hcoona.CelesphoniaModifier.WinUI.csproj");

        internal string TestProject => Path.Combine(
            TestDirectory,
            "Hcoona.CelesphoniaModifier.WinUI.Tests.csproj");

        internal string TraversalProject => Path.Combine(RepositoryRoot, "dirs.proj");

        internal string MainWindowXaml => Path.Combine(ApplicationDirectory, "MainWindow.xaml");

        internal string MainWindowCode => Path.Combine(ApplicationDirectory, "MainWindow.xaml.cs");

        internal static ProjectPaths Create()
        {
            string root = Assembly
                .GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "RepositoryRoot")
                .Value
                ?? throw new InvalidOperationException("RepositoryRoot metadata is missing.");
            string application = Path.Combine(
                root,
                "src",
                "private",
                "app",
                "celesphonia-modifier",
                "Hcoona.CelesphoniaModifier.WinUI");
            string tests = Path.Combine(
                root,
                "tests",
                "private",
                "app",
                "celesphonia-modifier",
                "Hcoona.CelesphoniaModifier.WinUI.Tests");
            return new ProjectPaths(root, application, tests);
        }
    }
}
