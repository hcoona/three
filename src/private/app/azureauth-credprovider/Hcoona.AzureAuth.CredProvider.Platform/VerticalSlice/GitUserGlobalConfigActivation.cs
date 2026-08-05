using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

internal enum GitUserGlobalConfigActivationState
{
    Absent = 0,
    Present = 1,
}

internal sealed class GitUserGlobalConfigActivation(IFileSystem fileSystem)
{
    private const string BeginMarker =
        "# BEGIN azureauth-credprovider managed include";
    private const string BeginMarkerWithOwnedSeparator =
        "# BEGIN azureauth-credprovider managed include (owned preceding newline)";
    private const string EndMarker = "# END azureauth-credprovider managed include";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

    public GitUserGlobalConfigActivationState Inspect(
        string userGitConfigPath,
        string productGitConfigPath
    )
    {
        GitConfigText document = Read(userGitConfigPath);
        return document.FindOwnedBlock(productGitConfigPath) is null
            ? GitUserGlobalConfigActivationState.Absent
            : GitUserGlobalConfigActivationState.Present;
    }

    public void EnsurePresent(string userGitConfigPath, string productGitConfigPath)
    {
        string writePath = ResolveWritePath(userGitConfigPath);
        using IGitConfigLockFile configLock = AcquireLock(writePath);
        GitConfigText document = ReadResolved(writePath);
        if (document.FindOwnedBlock(productGitConfigPath) is not null)
        {
            return;
        }

        string updated = document.AppendOwnedBlock(productGitConfigPath);
        Commit(configLock, userGitConfigPath, document, updated);
    }

    public void Remove(string userGitConfigPath, string productGitConfigPath)
    {
        string writePath = ResolveWritePath(userGitConfigPath);
        using IGitConfigLockFile configLock = AcquireLock(writePath);
        GitConfigText document = ReadResolved(writePath);
        (int Start, int Length)? block = document.FindOwnedBlock(productGitConfigPath);
        if (block is null)
        {
            throw new InvalidOperationException(
                "The product-owned Git include block is missing."
            );
        }

        string updated = document.Text.Remove(block.Value.Start, block.Value.Length);
        Commit(configLock, userGitConfigPath, document, updated);
    }

    private IGitConfigLockFile AcquireLock(string writePath)
    {
        if (fileSystem is not IFileSystemGitConfigLock gitConfigLock)
        {
            throw new InvalidOperationException(
                "Git configuration activation requires cooperative lock-file support."
            );
        }

        return gitConfigLock.AcquireGitConfigLock(writePath);
    }

    private GitConfigText Read(string path) => ReadResolved(ResolveWritePath(path));

    private GitConfigText ReadResolved(string writePath)
    {
        if (!fileSystem.FileExists(writePath))
        {
            if (fileSystem.DirectoryExists(writePath))
            {
                throw new InvalidOperationException(
                    "The user-global Git configuration path is a directory."
                );
            }

            return GitConfigText.Missing(writePath);
        }

        return GitConfigText.Parse(fileSystem.ReadAllBytes(writePath), writePath);
    }

    private void Commit(
        IGitConfigLockFile configLock,
        string path,
        GitConfigText document,
        string updated
    )
    {
        if (!PathsEqual(ResolveWritePath(path), document.WritePath))
        {
            throw new IOException(
                "The user-global Git configuration link changed while it was being updated."
            );
        }

        configLock.WriteAllBytes(document.Encode(updated));
        configLock.Commit();
    }

    private string ResolveWritePath(string path) =>
        fileSystem is IFileSystemLinkResolver linkResolver
            ? linkResolver.ResolveFilePathForWrite(path)
            : fileSystem.GetFullPath(path);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private sealed record GitConfigText(
        string Text,
        bool HadBom,
        string NewLine,
        string WritePath
    )
    {
        public static GitConfigText Missing(string writePath) =>
            new(string.Empty, HadBom: false, Environment.NewLine, writePath);

        public static GitConfigText Parse(byte[] bytes, string writePath)
        {
            bool hadBom = bytes is [0xEF, 0xBB, 0xBF, ..];
            string text = Utf8NoBom.GetString(hadBom ? bytes[3..] : bytes);
            string newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            return new GitConfigText(text, hadBom, newLine, writePath);
        }

        public (int Start, int Length)? FindOwnedBlock(string productGitConfigPath)
        {
            string block = RenderOwnedBlock(productGitConfigPath, BeginMarker);
            string blockWithOwnedSeparator = RenderOwnedBlock(
                productGitConfigPath,
                BeginMarkerWithOwnedSeparator
            );
            int beginCount = CountOccurrences(Text, BeginMarker);
            int endCount = CountOccurrences(Text, EndMarker);
            string renderedPath = RenderPath(productGitConfigPath);

            if (beginCount == 0 && endCount == 0)
            {
                if (ContainsProductPath(Text, renderedPath, productGitConfigPath))
                {
                    throw new InvalidOperationException(
                        "The product Git configuration is included without recognized ownership."
                    );
                }

                return null;
            }

            if (beginCount != 1 || endCount != 1)
            {
                throw new InvalidOperationException(
                    "The product-owned Git include block markers are not recognized."
                );
            }

            string ownedSeparatorBlock = NewLine + blockWithOwnedSeparator + NewLine;
            int ownedSeparatorStart = Text.IndexOf(
                ownedSeparatorBlock,
                StringComparison.Ordinal
            );
            if (ownedSeparatorStart >= 0)
            {
                ValidateNoCollisionOutsideBlock(
                    ownedSeparatorStart,
                    ownedSeparatorBlock.Length,
                    renderedPath,
                    productGitConfigPath
                );
                return (ownedSeparatorStart, ownedSeparatorBlock.Length);
            }

            string regularBlock = block + NewLine;
            int regularStart = Text.IndexOf(regularBlock, StringComparison.Ordinal);
            if (
                regularStart >= 0
                && (
                    regularStart == 0
                    || Text.AsSpan(0, regularStart).EndsWith(NewLine.AsSpan())
                )
            )
            {
                ValidateNoCollisionOutsideBlock(
                    regularStart,
                    regularBlock.Length,
                    renderedPath,
                    productGitConfigPath
                );
                return (regularStart, regularBlock.Length);
            }

            throw new InvalidOperationException(
                "The product-owned Git include block was modified or moved."
            );
        }

        public string AppendOwnedBlock(string productGitConfigPath)
        {
            string block = RenderOwnedBlock(productGitConfigPath, BeginMarker);
            if (Text.Length == 0)
            {
                return block + NewLine;
            }

            return Text.EndsWith(NewLine, StringComparison.Ordinal)
                ? Text + block + NewLine
                : Text
                    + NewLine
                    + RenderOwnedBlock(
                        productGitConfigPath,
                        BeginMarkerWithOwnedSeparator
                    )
                    + NewLine;
        }

        public byte[] Encode(string text)
        {
            byte[] contents = Utf8NoBom.GetBytes(text);
            return HadBom ? [0xEF, 0xBB, 0xBF, .. contents] : contents;
        }

        private void ValidateNoCollisionOutsideBlock(
            int start,
            int length,
            string renderedPath,
            string productGitConfigPath
        )
        {
            string remaining = Text.Remove(start, length);
            if (
                remaining.Contains(BeginMarker, StringComparison.Ordinal)
                || remaining.Contains(EndMarker, StringComparison.Ordinal)
                || ContainsProductPath(remaining, renderedPath, productGitConfigPath)
            )
            {
                throw new InvalidOperationException(
                    "The product-owned Git include block collides with other configuration."
                );
            }
        }

        private string RenderOwnedBlock(string productGitConfigPath, string beginMarker) =>
            string.Join(
                NewLine,
                beginMarker,
                "[include]",
                "\tpath = \"" + Escape(RenderPath(productGitConfigPath)) + "\"",
                EndMarker
            );

        private static string RenderPath(string path) =>
            OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

        private static bool ContainsProductPath(
            string text,
            string renderedPath,
            string productGitConfigPath
        ) =>
            OperatingSystem.IsWindows()
                ? text.Contains(renderedPath, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(
                        productGitConfigPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                : text.Contains(renderedPath, StringComparison.Ordinal);

        private static string Escape(string value) =>
            value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

        private static int CountOccurrences(string value, string match)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(match, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += match.Length;
            }

            return count;
        }
    }
}
