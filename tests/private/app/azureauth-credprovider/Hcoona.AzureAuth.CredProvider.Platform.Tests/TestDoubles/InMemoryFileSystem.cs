using System.IO.Enumeration;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;

public sealed class InMemoryFileSystem : IFileSystem, IFileSystemMutationLock
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private readonly Dictionary<string, byte[]> files;
    private readonly HashSet<string> directories;
    private readonly HashSet<string> heldLocks;
    private readonly InMemoryPathSemantics pathSemantics;
    private readonly StringComparer pathComparer;
    private readonly List<ScheduledFailure> scheduledFailures = [];
    private readonly Dictionary<string, UnixFileMode> unixFileModes;
    private readonly Queue<Exception> failures = [];
    private readonly string rootPath;

    public InMemoryFileSystem(InMemoryPathSemantics pathSemantics = InMemoryPathSemantics.Host)
    {
        this.pathSemantics = pathSemantics;
        pathComparer =
            pathSemantics == InMemoryPathSemantics.Windows
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        files = new Dictionary<string, byte[]>(pathComparer);
        directories = new HashSet<string>(pathComparer);
        heldLocks = new HashSet<string>(pathComparer);
        unixFileModes = new Dictionary<string, UnixFileMode>(pathComparer);
        rootPath =
            pathSemantics == InMemoryPathSemantics.Posix ? "/"
            : pathSemantics == InMemoryPathSemantics.Windows ? @"C:\"
            : Path.GetPathRoot(Path.GetFullPath("."))!;
        directories.Add(rootPath);
    }

    public List<FileSystemCall> Calls { get; } = [];

    public UnixFileMode? DefaultCreateDirectoryMode { get; set; }

    public IReadOnlyDictionary<string, string> Files =>
        files.ToDictionary(pair => pair.Key, pair => Utf8NoBom.GetString(pair.Value), pathComparer);

    public IReadOnlySet<string> Directories => directories;

    public void FailNextCall(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        failures.Enqueue(exception);
    }

    public void FailMatchingCall(
        string operation,
        string path,
        int occurrence,
        Exception exception
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(occurrence, 1);
        ArgumentNullException.ThrowIfNull(exception);
        scheduledFailures.Add(
            new ScheduledFailure(operation, NormalizePath(path), occurrence, exception)
        );
    }

    public bool FileExists(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(FileExists), normalizedPath);
        return files.ContainsKey(normalizedPath);
    }

    public bool IsExecutableFile(string path)
    {
        string normalizedPath = NormalizePath(path);
        bool exists = files.ContainsKey(normalizedPath);
        bool windowsSemantics =
            pathSemantics == InMemoryPathSemantics.Windows
            || (pathSemantics == InMemoryPathSemantics.Host && OperatingSystem.IsWindows());
        const UnixFileMode executeModes =
            UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute;
        bool result =
            exists
            && (
                windowsSemantics
                || (
                    unixFileModes.TryGetValue(normalizedPath, out UnixFileMode mode)
                    && (mode & executeModes) != 0
                )
            );
        Record(nameof(IsExecutableFile), normalizedPath, result.ToString());
        return result;
    }

    public bool DirectoryExists(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(DirectoryExists), normalizedPath);
        return directories.Contains(normalizedPath);
    }

    public string GetFullPath(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(GetFullPath), normalizedPath);
        return normalizedPath;
    }

    public bool IsPathFullyQualified(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        bool result =
            pathSemantics == InMemoryPathSemantics.Posix ? path.StartsWith('/')
            : pathSemantics == InMemoryPathSemantics.Windows
                ? path.Length >= 3
                    && char.IsAsciiLetter(path[0])
                    && path[1] == ':'
                    && IsSeparator(path[2])
            : Path.IsPathFullyQualified(path);
        Record(nameof(IsPathFullyQualified), path, result.ToString());
        return result;
    }

    public string ReadAllText(string path, Encoding? encoding = null)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(ReadAllText), normalizedPath);
        return (encoding ?? Utf8NoBom).GetString(GetFile(normalizedPath));
    }

    public byte[] ReadAllBytes(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(ReadAllBytes), normalizedPath);
        return GetFile(normalizedPath).ToArray();
    }

    public long GetFileLength(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(GetFileLength), normalizedPath);
        return GetFile(normalizedPath).LongLength;
    }

    public void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        string normalizedPath = NormalizePath(path);
        Record(nameof(WriteAllText), normalizedPath, contents);
        EnsureParentDirectoryExists(normalizedPath);
        ThrowIfDirectory(normalizedPath);
        files[normalizedPath] = (encoding ?? Utf8NoBom).GetBytes(contents);
        unixFileModes.TryAdd(
            normalizedPath,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead
        );
    }

    public void AtomicWriteAllText(
        string path,
        string contents,
        Encoding? encoding = null,
        AtomicWriteOptions options = AtomicWriteOptions.None
    )
    {
        ArgumentNullException.ThrowIfNull(contents);
        string normalizedPath = NormalizePath(path);
        Record(nameof(AtomicWriteAllText), normalizedPath, contents);
        AtomicWrite(normalizedPath, (encoding ?? Utf8NoBom).GetBytes(contents), options);
    }

    public void AtomicWriteAllBytes(
        string path,
        byte[] contents,
        AtomicWriteOptions options = AtomicWriteOptions.None
    )
    {
        ArgumentNullException.ThrowIfNull(contents);
        string normalizedPath = NormalizePath(path);
        Record(nameof(AtomicWriteAllBytes), normalizedPath);
        AtomicWrite(normalizedPath, contents, options);
    }

    public UnixFileMode GetUnixFileMode(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(GetUnixFileMode), normalizedPath);
        EnsureEntryExists(normalizedPath);
        return unixFileModes.TryGetValue(normalizedPath, out UnixFileMode mode) ? mode : default;
    }

    public void SetUnixFileMode(string path, UnixFileMode mode)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(SetUnixFileMode), normalizedPath, mode.ToString());
        EnsureEntryExists(normalizedPath);
        unixFileModes[normalizedPath] = mode;
    }

    public void CreateDirectory(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(CreateDirectory), normalizedPath);
        AddDirectoryWithParents(normalizedPath);
    }

    public void DeleteFile(string path)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(DeleteFile), normalizedPath);
        ThrowIfDirectory(normalizedPath);
        files.Remove(normalizedPath);
        unixFileModes.Remove(normalizedPath);
    }

    public void DeleteDirectory(string path, bool recursive = false)
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(DeleteDirectory), normalizedPath, recursive.ToString());
        if (!directories.Contains(normalizedPath))
        {
            throw new DirectoryNotFoundException(normalizedPath);
        }

        string prefix = AppendSeparator(normalizedPath);
        string[] nestedFiles = files
            .Keys.Where(path => path.StartsWith(prefix, Comparison))
            .ToArray();
        string[] nestedDirectories = directories
            .Where(path =>
                !pathComparer.Equals(path, normalizedPath) && path.StartsWith(prefix, Comparison)
            )
            .ToArray();
        if (!recursive && (nestedFiles.Length != 0 || nestedDirectories.Length != 0))
        {
            throw new IOException($"Directory '{normalizedPath}' is not empty.");
        }

        foreach (string file in nestedFiles)
        {
            files.Remove(file);
            unixFileModes.Remove(file);
        }

        foreach (string directory in nestedDirectories)
        {
            directories.Remove(directory);
            unixFileModes.Remove(directory);
        }

        directories.Remove(normalizedPath);
        unixFileModes.Remove(normalizedPath);
    }

    public IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(EnumerateFiles), normalizedPath, searchPattern);
        EnsureDirectoryExists(normalizedPath);
        return files
            .Keys.Where(candidate => IsIncluded(candidate, normalizedPath, searchOption))
            .Where(candidate => Matches(searchPattern, GetFileName(candidate)))
            .OrderBy(static candidate => candidate, pathComparer)
            .ToArray();
    }

    public IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        string normalizedPath = NormalizePath(path);
        Record(nameof(EnumerateDirectories), normalizedPath, searchPattern);
        EnsureDirectoryExists(normalizedPath);
        return directories
            .Where(candidate => !pathComparer.Equals(candidate, normalizedPath))
            .Where(candidate => IsIncluded(candidate, normalizedPath, searchOption))
            .Where(candidate => Matches(searchPattern, GetFileName(candidate)))
            .OrderBy(static candidate => candidate, pathComparer)
            .ToArray();
    }

    IDisposable IFileSystemMutationLock.AcquireMutationLock(string directory)
    {
        string normalizedPath = NormalizePath(directory);
        Record(nameof(IFileSystemMutationLock.AcquireMutationLock), normalizedPath);
        AddDirectoryWithParents(normalizedPath);
        if (!heldLocks.Add(normalizedPath))
        {
            throw new IOException($"The mutation lock '{normalizedPath}' is already held.");
        }

        return new ActionDisposable(() => heldLocks.Remove(normalizedPath));
    }

    private StringComparison Comparison =>
        pathSemantics == InMemoryPathSemantics.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private void AtomicWrite(string path, byte[] contents, AtomicWriteOptions options)
    {
        AddDirectoryWithParents(GetParentPath(path));
        ThrowIfDirectory(path);
        files[path] = contents.ToArray();
        if ((options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0)
        {
            unixFileModes[path] = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        else
        {
            unixFileModes.TryAdd(
                path,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead
                    | UnixFileMode.OtherRead
            );
        }
    }

    private void AddDirectoryWithParents(string path)
    {
        if (files.ContainsKey(path))
        {
            throw new IOException($"A file already exists at '{path}'.");
        }

        if (directories.Contains(path))
        {
            return;
        }

        string parent = GetParentPath(path);
        if (!pathComparer.Equals(parent, path))
        {
            AddDirectoryWithParents(parent);
        }

        directories.Add(path);
        unixFileModes[path] =
            DefaultCreateDirectoryMode
            ?? (
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute
            );
    }

    private void EnsureParentDirectoryExists(string path) =>
        EnsureDirectoryExists(GetParentPath(path));

    private void EnsureDirectoryExists(string path)
    {
        if (!directories.Contains(path))
        {
            throw new DirectoryNotFoundException(path);
        }
    }

    private void EnsureEntryExists(string path)
    {
        if (!files.ContainsKey(path) && !directories.Contains(path))
        {
            throw new FileNotFoundException("The in-memory path does not exist.", path);
        }
    }

    private byte[] GetFile(string path)
    {
        if (directories.Contains(path))
        {
            throw new UnauthorizedAccessException($"'{path}' is a directory.");
        }

        return files.TryGetValue(path, out byte[]? contents)
            ? contents
            : throw new FileNotFoundException("The in-memory file does not exist.", path);
    }

    private void ThrowIfDirectory(string path)
    {
        if (directories.Contains(path))
        {
            throw new UnauthorizedAccessException($"'{path}' is a directory.");
        }
    }

    private bool IsIncluded(string candidate, string parent, SearchOption searchOption)
    {
        string prefix = AppendSeparator(parent);
        if (!candidate.StartsWith(prefix, Comparison))
        {
            return false;
        }

        string relative = candidate[prefix.Length..];
        return searchOption == SearchOption.AllDirectories || !relative.Any(IsSeparator);
    }

    private bool Matches(string pattern, string name) =>
        FileSystemName.MatchesSimpleExpression(
            pattern,
            name,
            ignoreCase: pathSemantics == InMemoryPathSemantics.Windows
        );

    private string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (pathSemantics == InMemoryPathSemantics.Host)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        char separator = pathSemantics == InMemoryPathSemantics.Windows ? '\\' : '/';
        string replaced = path.Replace('\\', separator).Replace('/', separator);
        string prefix;
        string remainder;
        if (pathSemantics == InMemoryPathSemantics.Windows)
        {
            bool rooted =
                replaced.Length >= 3
                && char.IsAsciiLetter(replaced[0])
                && replaced[1] == ':'
                && replaced[2] == separator;
            prefix = rooted ? char.ToUpperInvariant(replaced[0]) + @":\" : rootPath;
            remainder = rooted ? replaced[3..] : replaced;
        }
        else
        {
            prefix = "/";
            remainder = replaced.TrimStart(separator);
        }

        var components = new List<string>();
        foreach (
            string component in remainder.Split(separator, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (components.Count > 0)
                {
                    components.RemoveAt(components.Count - 1);
                }
                continue;
            }

            components.Add(component);
        }

        return components.Count == 0 ? prefix : prefix + string.Join(separator, components);
    }

    private string GetParentPath(string path)
    {
        int separatorIndex = path.LastIndexOfAny(['/', '\\']);
        if (separatorIndex <= 0)
        {
            return rootPath;
        }

        if (pathSemantics == InMemoryPathSemantics.Windows && separatorIndex == 2)
        {
            return path[..3];
        }

        return path[..separatorIndex];
    }

    private static string GetFileName(string path)
    {
        int separatorIndex = path.LastIndexOfAny(['/', '\\']);
        return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
    }

    private static bool IsSeparator(char character) => character is '/' or '\\';

    private string AppendSeparator(string path)
    {
        if (path.EndsWith('/') || path.EndsWith('\\'))
        {
            return path;
        }

        char separator =
            pathSemantics == InMemoryPathSemantics.Windows ? '\\'
            : pathSemantics == InMemoryPathSemantics.Posix ? '/'
            : Path.DirectorySeparatorChar;
        return path + separator;
    }

    private void Record(string operation, string path, string? value = null)
    {
        int scheduledIndex = scheduledFailures.FindIndex(failure =>
            string.Equals(failure.Operation, operation, StringComparison.Ordinal)
            && pathComparer.Equals(failure.Path, path)
        );
        if (scheduledIndex >= 0)
        {
            ScheduledFailure failure = scheduledFailures[scheduledIndex];
            if (failure.RemainingOccurrences == 1)
            {
                scheduledFailures.RemoveAt(scheduledIndex);
                throw failure.Exception;
            }

            scheduledFailures[scheduledIndex] = failure with
            {
                RemainingOccurrences = failure.RemainingOccurrences - 1,
            };
        }

        if (failures.TryDequeue(out Exception? exception))
        {
            throw exception;
        }

        Calls.Add(new FileSystemCall(operation, path, value));
    }

    private sealed record ScheduledFailure(
        string Operation,
        string Path,
        int RemainingOccurrences,
        Exception Exception
    );

    private sealed class ActionDisposable(Action dispose) : IDisposable
    {
        private Action? dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
    }
}
