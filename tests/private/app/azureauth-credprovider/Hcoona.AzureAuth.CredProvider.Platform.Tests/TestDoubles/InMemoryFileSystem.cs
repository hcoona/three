using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;

public sealed class InMemoryFileSystem : IFileSystem
{
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly InMemoryPathSemantics _pathSemantics;
    private readonly string _rootPath;
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileSystemEntryIdentity> _identities = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, UnixFileMode> _unixFileModes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _symbolicLinks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileSystemOwner> _owners = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly Queue<Exception> _failures = [];
    private long _nextIdentity = 1;

    public InMemoryFileSystem(InMemoryPathSemantics pathSemantics = InMemoryPathSemantics.Host)
    {
        _pathSemantics = pathSemantics;
        _rootPath =
            pathSemantics == InMemoryPathSemantics.Posix
                ? "/"
                : NormalizeHostFullPath(Directory.GetCurrentDirectory());
        _directories.Add(_rootPath);
        _identities[_rootPath] = CreateIdentity();
        _owners[_rootPath] = CurrentOwner;
    }

    public List<FileSystemCall> Calls { get; } = [];

    public IReadOnlyDictionary<string, string> Files => _files;

    public IReadOnlySet<string> Directories => _directories;

    public FileSystemOwner CurrentOwner { get; set; } = new("fake:current");

    public void FailNextCall(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _failures.Enqueue(exception);
    }

    public void AddSymbolicLink(string linkPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var normalizedLinkPath = NormalizePath(linkPath);
        Record(nameof(AddSymbolicLink), normalizedLinkPath, NormalizePath(targetPath));
        ThrowIfFailureQueued();
        var resolvedLinkPath = ResolveSymbolicLinkPath(
            normalizedLinkPath,
            followFinalComponent: false
        );
        EnsureParentDirectoryExists(resolvedLinkPath);

        _symbolicLinks[resolvedLinkPath] = NormalizePath(targetPath);
        _identities[resolvedLinkPath] = CreateIdentity();
        _owners.TryAdd(resolvedLinkPath, CurrentOwner);
    }

    public void SetOwner(string path, FileSystemOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var normalizedPath = NormalizePath(path);
        Record(nameof(SetOwner), normalizedPath, owner.Id);
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath, followFinalComponent: false);
        EnsureFileOrDirectoryExists(resolvedPath);

        _owners[resolvedPath] = owner;
    }

    public bool FileExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(FileExists), normalizedPath);
        ThrowIfFailureQueued();

        return _files.ContainsKey(ResolveSymbolicLinkPath(normalizedPath));
    }

    public bool DirectoryExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(DirectoryExists), normalizedPath);
        ThrowIfFailureQueued();

        return _directories.Contains(ResolveSymbolicLinkPath(normalizedPath));
    }

    public string GetFullPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(GetFullPath), normalizedPath);
        ThrowIfFailureQueued();

        return normalizedPath;
    }

    public bool IsPathFullyQualified(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var isFullyQualified =
            _pathSemantics == InMemoryPathSemantics.Posix
                ? path[0] == '/'
                : Path.IsPathFullyQualified(path);
        Record(nameof(IsPathFullyQualified), NormalizePath(path), isFullyQualified.ToString());
        ThrowIfFailureQueued();

        return isFullyQualified;
    }

    public bool IsSymbolicLink(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(IsSymbolicLink), normalizedPath);
        ThrowIfFailureQueued();

        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath, followFinalComponent: false);
        EnsureFileOrDirectoryExists(resolvedPath);
        return _symbolicLinks.ContainsKey(resolvedPath);
    }

    public byte[] ComputeSha256Hash(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(ComputeSha256Hash), normalizedPath);
        ThrowIfFailureQueued();

        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);
        return _files.TryGetValue(resolvedPath, out var contents)
            ? SHA256.HashData(Encoding.UTF8.GetBytes(contents))
            : throw new FileNotFoundException("The in-memory file does not exist.", normalizedPath);
    }

    public FileIntegritySnapshot CaptureFileIntegritySnapshot(string path)
    {
        ThrowIfPathContainsCurrentOrParentDirectoryComponent(path);
        var normalizedPath = NormalizePath(path);
        Record(nameof(CaptureFileIntegritySnapshot), normalizedPath);
        ThrowIfFailureQueued();

        return CaptureFileIntegritySnapshotWithoutRecording(normalizedPath);
    }

    public bool FileMatchesIntegritySnapshot(string path, FileIntegritySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (PathContainsCurrentOrParentDirectoryComponent(path))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        Record(nameof(FileMatchesIntegritySnapshot), normalizedPath, snapshot.Identity.Value);
        ThrowIfFailureQueued();

        try
        {
            var currentSnapshot = CaptureFileIntegritySnapshotWithoutRecording(normalizedPath);
            return string.Equals(
                    currentSnapshot.FullPath,
                    snapshot.FullPath,
                    StringComparison.Ordinal
                )
                && currentSnapshot.Identity == snapshot.Identity
                && currentSnapshot.Owner == snapshot.Owner
                && currentSnapshot.UnixFileMode == snapshot.UnixFileMode
                && currentSnapshot.TrustedParentDirectories.SequenceEqual(
                    snapshot.TrustedParentDirectories
                )
                && currentSnapshot.Sha256Hash.SequenceEqual(snapshot.Sha256Hash);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(
        string path
    )
    {
        ThrowIfPathContainsCurrentOrParentDirectoryComponent(path);
        var normalizedPath = NormalizePath(path);
        Record(nameof(CaptureTrustedParentDirectorySnapshots), normalizedPath);
        ThrowIfFailureQueued();

        return CaptureTrustedParentDirectorySnapshotsWithoutRecording(normalizedPath);
    }

    public FileSystemOwner GetCurrentOwner()
    {
        Record(nameof(GetCurrentOwner), _rootPath);
        ThrowIfFailureQueued();

        return CurrentOwner;
    }

    public FileSystemOwner GetOwner(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(GetOwner), normalizedPath);
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);
        EnsureFileOrDirectoryExists(resolvedPath);

        return _owners.TryGetValue(resolvedPath, out var owner) ? owner : CurrentOwner;
    }

    public string ReadAllText(string path, Encoding? encoding = null)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(ReadAllText), normalizedPath);
        ThrowIfFailureQueued();

        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);
        return _files.TryGetValue(resolvedPath, out var contents)
            ? contents
            : throw new FileNotFoundException("The in-memory file does not exist.", normalizedPath);
    }

    public void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var normalizedPath = NormalizePath(path);
        Record(nameof(WriteAllText), normalizedPath, contents);
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);
        EnsureParentDirectoryExists(resolvedPath);
        ThrowIfDirectoryExists(resolvedPath);

        _files[resolvedPath] = contents;
        _identities.TryAdd(resolvedPath, CreateIdentity());
        _unixFileModes.TryAdd(resolvedPath, OwnerOnlyFileMode);
        _owners.TryAdd(resolvedPath, CurrentOwner);
    }

    public void AtomicWriteAllText(
        string path,
        string contents,
        Encoding? encoding = null,
        AtomicWriteOptions options = AtomicWriteOptions.None
    )
    {
        ArgumentNullException.ThrowIfNull(contents);

        var normalizedPath = NormalizePath(path);
        Record(nameof(AtomicWriteAllText), normalizedPath, contents);
        ThrowIfFailureQueued();
        var replacementPath = ResolveSymbolicLinkPath(normalizedPath, followFinalComponent: false);
        var createdDirectoryMode =
            (options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0
                ? OwnerOnlyDirectoryMode
                : (UnixFileMode?)null;
        AddDirectoryWithParents(GetParentPath(replacementPath), createdDirectoryMode);
        ThrowIfDirectoryExists(replacementPath);

        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);
        var targetExists = _files.ContainsKey(resolvedPath);
        var mode =
            (options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0 || !targetExists
                ? OwnerOnlyFileMode
                : GetUnixFileModeWithoutRecording(resolvedPath);

        var replacingSymbolicLink = _symbolicLinks.Remove(replacementPath);
        _files[replacementPath] = contents;
        _identities[replacementPath] = CreateIdentity();
        _unixFileModes[replacementPath] = mode;
        if (replacingSymbolicLink)
        {
            _owners[replacementPath] = CurrentOwner;
        }
        else
        {
            _owners.TryAdd(replacementPath, CurrentOwner);
        }
    }

    public UnixFileMode GetUnixFileMode(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(GetUnixFileMode), normalizedPath);
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);
        EnsureFileOrDirectoryExists(resolvedPath);

        return GetUnixFileModeWithoutRecording(resolvedPath);
    }

    public void SetUnixFileMode(string path, UnixFileMode mode)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(SetUnixFileMode), normalizedPath, mode.ToString());
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);
        EnsureFileOrDirectoryExists(resolvedPath);

        _unixFileModes[resolvedPath] = mode;
    }

    public void CreateDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(CreateDirectory), normalizedPath);
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath);

        AddDirectoryWithParents(resolvedPath);
        _owners.TryAdd(resolvedPath, CurrentOwner);
    }

    public void DeleteFile(string path)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(DeleteFile), normalizedPath);
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath, followFinalComponent: false);

        _files.Remove(resolvedPath);
        _identities.Remove(resolvedPath);
        _unixFileModes.Remove(resolvedPath);
        _symbolicLinks.Remove(resolvedPath);
        _owners.Remove(resolvedPath);
    }

    public void DeleteDirectory(string path, bool recursive = false)
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(DeleteDirectory), normalizedPath, recursive.ToString());
        ThrowIfFailureQueued();
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath, followFinalComponent: false);

        if (
            _symbolicLinks.TryGetValue(resolvedPath, out var targetPath)
            && _directories.Contains(ResolveSymbolicLinkPath(targetPath))
        )
        {
            _identities.Remove(resolvedPath);
            _unixFileModes.Remove(resolvedPath);
            _symbolicLinks.Remove(resolvedPath);
            _owners.Remove(resolvedPath);
            return;
        }

        if (!_directories.Contains(resolvedPath))
        {
            throw new DirectoryNotFoundException(normalizedPath);
        }

        var nestedDirectories = _directories
            .Where(directory => IsChildPath(resolvedPath, directory))
            .ToArray();
        var nestedFiles = _files.Keys.Where(file => IsChildPath(resolvedPath, file)).ToArray();
        var nestedLinks = _symbolicLinks
            .Keys.Where(link => IsChildPath(resolvedPath, link))
            .ToArray();

        if (
            !recursive
            && (nestedDirectories.Length > 0 || nestedFiles.Length > 0 || nestedLinks.Length > 0)
        )
        {
            throw new IOException("The in-memory directory is not empty.");
        }

        foreach (var file in nestedFiles)
        {
            _files.Remove(file);
            _identities.Remove(file);
            _unixFileModes.Remove(file);
            _symbolicLinks.Remove(file);
            _owners.Remove(file);
        }

        foreach (var link in nestedLinks)
        {
            _identities.Remove(link);
            _unixFileModes.Remove(link);
            _symbolicLinks.Remove(link);
            _owners.Remove(link);
        }

        foreach (var directory in nestedDirectories)
        {
            _directories.Remove(directory);
            _identities.Remove(directory);
            _unixFileModes.Remove(directory);
            _symbolicLinks.Remove(directory);
            _owners.Remove(directory);
        }

        _directories.Remove(resolvedPath);
        _identities.Remove(resolvedPath);
        _unixFileModes.Remove(resolvedPath);
        _symbolicLinks.Remove(resolvedPath);
        _owners.Remove(resolvedPath);
    }

    public IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(EnumerateFiles), normalizedPath, searchPattern);
        ThrowIfFailureQueued();
        var resolvedDirectoryPath = ResolveSymbolicLinkPath(normalizedPath);
        EnsureDirectoryExists(resolvedDirectoryPath);

        var files = _files
            .Keys.Concat(
                _symbolicLinks
                    .Where(link => _files.ContainsKey(ResolveSymbolicLinkPath(link.Key)))
                    .Select(link => link.Key)
            )
            .Where(file =>
                IsInEnumerationScope(resolvedDirectoryPath, file, searchOption)
                && MatchesSearchPattern(file, searchPattern)
            )
            .Select(file => ProjectResolvedPath(normalizedPath, resolvedDirectoryPath, file))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return files;
    }

    public IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        var normalizedPath = NormalizePath(path);
        Record(nameof(EnumerateDirectories), normalizedPath, searchPattern);
        ThrowIfFailureQueued();
        var resolvedDirectoryPath = ResolveSymbolicLinkPath(normalizedPath);
        EnsureDirectoryExists(resolvedDirectoryPath);

        var directories = _directories
            .Where(directory => !string.Equals(directory, _rootPath, StringComparison.Ordinal))
            .Concat(
                _symbolicLinks
                    .Where(link => _directories.Contains(ResolveSymbolicLinkPath(link.Key)))
                    .Select(link => link.Key)
            )
            .Where(directory =>
                IsInEnumerationScope(resolvedDirectoryPath, directory, searchOption)
                && MatchesSearchPattern(directory, searchPattern)
            )
            .Select(directory =>
                ProjectResolvedPath(normalizedPath, resolvedDirectoryPath, directory)
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

        return directories;
    }

    private string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _pathSemantics == InMemoryPathSemantics.Posix
            ? NormalizePosixPath(path)
            : NormalizeHostFullPath(Path.GetFullPath(path));
    }

    private void ThrowIfPathContainsCurrentOrParentDirectoryComponent(string path)
    {
        if (PathContainsCurrentOrParentDirectoryComponent(path))
        {
            throw new IOException(
                $"The helper integrity path '{path}' must not contain '.' or '..' path components "
                    + "or Windows path components with trailing spaces or periods."
            );
        }
    }

    private bool PathContainsCurrentOrParentDirectoryComponent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var componentStart = 0;
        for (var index = 0; index <= path.Length; index++)
        {
            if (index < path.Length && !IsDirectorySeparator(path[index]))
            {
                continue;
            }

            var componentLength = index - componentStart;
            if (IsUnsafePathComponent(path, componentStart, componentLength))
            {
                return true;
            }

            componentStart = index + 1;
        }

        return false;
    }

    private bool IsUnsafePathComponent(string path, int componentStart, int componentLength)
    {
        if (componentLength == 0)
        {
            return false;
        }

        if (IsCurrentOrParentDirectoryComponent(path, componentStart, componentLength))
        {
            return true;
        }

        if (_pathSemantics == InMemoryPathSemantics.Posix || !OperatingSystem.IsWindows())
        {
            return false;
        }

        char lastCharacter = path[componentStart + componentLength - 1];
        return lastCharacter is ' ' or '.';
    }

    private static bool IsCurrentOrParentDirectoryComponent(
        string path,
        int componentStart,
        int componentLength
    )
    {
        return componentLength == 1 && path[componentStart] == '.'
            || componentLength == 2
                && path[componentStart] == '.'
                && path[componentStart + 1] == '.';
    }

    private static string NormalizeHostFullPath(string path)
    {
        var trimmedPath = Path.TrimEndingDirectorySeparator(path);
        if (trimmedPath.Length == 0)
        {
            trimmedPath = Path.GetPathRoot(path) ?? path;
        }

        return trimmedPath;
    }

    private static string NormalizePosixPath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? "/" : "/" + string.Join('/', segments);
    }

    private string GetParentPath(string path)
    {
        if (_pathSemantics == InMemoryPathSemantics.Posix)
        {
            if (path == "/")
            {
                return string.Empty;
            }

            var separatorIndex = path.LastIndexOf('/');
            return separatorIndex <= 0 ? "/" : path[..separatorIndex];
        }

        var parentPath = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(parentPath) ? string.Empty : NormalizeHostFullPath(parentPath);
    }

    private bool IsChildPath(string parentPath, string candidate)
    {
        return parentPath.Length == 0
                ? candidate.Length > 0
                    && !string.Equals(candidate, parentPath, StringComparison.Ordinal)
            : IsRootPath(parentPath)
                ? candidate.Length > parentPath.Length
                    && candidate.StartsWith(parentPath, StringComparison.Ordinal)
            : candidate.StartsWith(parentPath + GetDirectorySeparator(), StringComparison.Ordinal);
    }

    private bool IsRootPath(string path)
    {
        if (_pathSemantics == InMemoryPathSemantics.Posix)
        {
            return path == "/";
        }

        var rootPath = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(rootPath)
            && string.Equals(path, NormalizeHostFullPath(rootPath), StringComparison.Ordinal);
    }

    private bool IsInScope(string directoryPath, string candidate, SearchOption searchOption)
    {
        if (!IsChildPath(directoryPath, candidate))
        {
            return false;
        }

        return searchOption == SearchOption.AllDirectories
            || string.Equals(GetParentPath(candidate), directoryPath, StringComparison.Ordinal);
    }

    private bool MatchesSearchPattern(string path, string searchPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        return FileSystemName.MatchesSimpleExpression(
            searchPattern,
            GetFileName(path),
            ignoreCase: false
        );
    }

    private bool IsInEnumerationScope(
        string resolvedDirectoryPath,
        string candidatePath,
        SearchOption searchOption
    )
    {
        return IsInScope(resolvedDirectoryPath, candidatePath, searchOption);
    }

    private string ResolveSymbolicLinkPath(string path, bool followFinalComponent = true)
    {
        var visitedPaths = new HashSet<string>(StringComparer.Ordinal);
        var currentPath = path;

        while (
            TryFindSymbolicLinkComponent(
                currentPath,
                followFinalComponent,
                out var linkPath,
                out var targetPath
            )
        )
        {
            if (!visitedPaths.Add(linkPath))
            {
                throw new IOException("A symbolic link cycle was detected.");
            }

            currentPath = CombineResolvedPath(targetPath, currentPath[linkPath.Length..]);
        }

        return currentPath;
    }

    private bool TryFindSymbolicLinkComponent(
        string path,
        bool followFinalComponent,
        out string linkPath,
        out string targetPath
    )
    {
        foreach (var candidate in _symbolicLinks.Keys.OrderBy(candidate => candidate.Length))
        {
            if (!followFinalComponent && string.Equals(candidate, path, StringComparison.Ordinal))
            {
                continue;
            }

            if (
                string.Equals(candidate, path, StringComparison.Ordinal)
                || IsChildPath(candidate, path)
            )
            {
                linkPath = candidate;
                targetPath = _symbolicLinks[candidate];
                return true;
            }
        }

        linkPath = string.Empty;
        targetPath = string.Empty;
        return false;
    }

    private string CombineResolvedPath(string targetPath, string suffix)
    {
        if (suffix.Length == 0)
        {
            return targetPath;
        }

        return _pathSemantics == InMemoryPathSemantics.Posix
            ? NormalizePosixPath(targetPath + suffix)
            : NormalizeHostFullPath(Path.GetFullPath(targetPath + suffix));
    }

    private string ProjectResolvedPath(
        string requestedDirectoryPath,
        string resolvedDirectoryPath,
        string originalCandidatePath
    )
    {
        if (string.Equals(requestedDirectoryPath, resolvedDirectoryPath, StringComparison.Ordinal))
        {
            return originalCandidatePath;
        }

        if (!IsChildPath(resolvedDirectoryPath, originalCandidatePath))
        {
            return originalCandidatePath;
        }

        return requestedDirectoryPath + originalCandidatePath[resolvedDirectoryPath.Length..];
    }

    private bool IsRootDirectory(string path)
    {
        return string.Equals(path, _rootPath, StringComparison.Ordinal);
    }

    private UnixFileMode GetUnixFileModeWithoutRecording(string normalizedPath)
    {
        return _unixFileModes.TryGetValue(normalizedPath, out var mode) ? mode : 0;
    }

    private FileIntegritySnapshot CaptureFileIntegritySnapshotWithoutRecording(
        string normalizedPath
    )
    {
        ThrowIfSymbolicLinkParentComponent(normalizedPath);
        var resolvedPath = ResolveSymbolicLinkPath(normalizedPath, followFinalComponent: false);
        if (_symbolicLinks.ContainsKey(resolvedPath))
        {
            throw new IOException("Cannot capture an integrity snapshot for a symbolic link.");
        }

        if (!_files.TryGetValue(resolvedPath, out var contents))
        {
            throw new FileNotFoundException("The in-memory file does not exist.", normalizedPath);
        }

        var owner = _owners.TryGetValue(resolvedPath, out var fileOwner) ? fileOwner : CurrentOwner;
        ThrowIfUntrustedOwner(normalizedPath, owner, "helper file");
        var unixFileMode = GetUnixFileModeWithoutRecording(resolvedPath);
        ThrowIfUnsafeHelperUnixFileMode(normalizedPath, unixFileMode, owner == CurrentOwner);
        return new FileIntegritySnapshot(
            normalizedPath,
            _identities[resolvedPath],
            owner,
            unixFileMode,
            SHA256.HashData(Encoding.UTF8.GetBytes(contents)),
            CaptureTrustedParentDirectorySnapshotsWithoutRecording(resolvedPath)
        );
    }

    private List<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshotsWithoutRecording(
        string resolvedPath
    )
    {
        ThrowIfSymbolicLinkParentComponent(resolvedPath);
        var snapshots = new List<TrustedDirectorySnapshot>();
        var parentPath = GetParentPath(resolvedPath);
        while (parentPath.Length > 0)
        {
            EnsureDirectoryExists(parentPath);
            var owner = _owners.TryGetValue(parentPath, out var directoryOwner)
                ? directoryOwner
                : CurrentOwner;
            ThrowIfUntrustedOwner(parentPath, owner, "trusted parent directory");
            var unixFileMode = GetUnixFileModeWithoutRecording(parentPath);
            ThrowIfUnsafeTrustedParentDirectoryUnixFileMode(parentPath, unixFileMode);
            snapshots.Add(
                new TrustedDirectorySnapshot(
                    parentPath,
                    _identities[parentPath],
                    owner,
                    unixFileMode
                )
            );

            if (IsRootDirectory(parentPath))
            {
                break;
            }

            parentPath = GetParentPath(parentPath);
        }

        return snapshots;
    }

    private void ThrowIfSymbolicLinkParentComponent(string normalizedPath)
    {
        var parentPath = GetParentPath(normalizedPath);
        while (parentPath.Length > 0)
        {
            if (_symbolicLinks.ContainsKey(parentPath))
            {
                throw new IOException("Helper parent directories must not be symbolic links.");
            }

            if (IsRootDirectory(parentPath))
            {
                return;
            }

            parentPath = GetParentPath(parentPath);
        }
    }

    private static void ThrowIfUnsafeHelperUnixFileMode(
        string path,
        UnixFileMode mode,
        bool isCurrentUserOwned
    )
    {
        const UnixFileMode unsafeWriteBits = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        const UnixFileMode executableBits =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((mode & unsafeWriteBits) != 0)
        {
            throw new UnauthorizedAccessException(
                $"The helper file '{path}' must not be writable by group or other users."
            );
        }

        if (isCurrentUserOwned && (mode & UnixFileMode.UserExecute) == 0)
        {
            throw new UnauthorizedAccessException(
                "The current-user-owned helper file "
                    + $"'{path}' must have the user executable bit set."
            );
        }

        if ((mode & executableBits) == 0)
        {
            throw new UnauthorizedAccessException(
                $"The helper file '{path}' must have an executable bit set."
            );
        }
    }

    private void ThrowIfUntrustedOwner(string path, FileSystemOwner owner, string entryKind)
    {
        if (owner != CurrentOwner && owner.Id != "fake:root" && owner.Id != "fake:system")
        {
            throw new UnauthorizedAccessException(
                $"The {entryKind} '{path}' must be owned by a trusted owner."
            );
        }
    }

    private static void ThrowIfUnsafeTrustedParentDirectoryUnixFileMode(
        string path,
        UnixFileMode mode
    )
    {
        const UnixFileMode unsafeWriteBits = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        if ((mode & unsafeWriteBits) != 0)
        {
            throw new UnauthorizedAccessException(
                "The helper parent directory "
                    + $"'{path}' must not be writable by group or other users."
            );
        }
    }

    private FileSystemEntryIdentity CreateIdentity()
    {
        return new FileSystemEntryIdentity($"memory:{_nextIdentity++}");
    }

    private bool IsDirectorySeparator(char value)
    {
        return _pathSemantics == InMemoryPathSemantics.Posix
            ? value == '/'
            : value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
    }

    private char GetDirectorySeparator()
    {
        return _pathSemantics == InMemoryPathSemantics.Posix ? '/' : Path.DirectorySeparatorChar;
    }

    private string GetFileName(string path)
    {
        if (_pathSemantics == InMemoryPathSemantics.Posix)
        {
            var separatorIndex = path.LastIndexOf('/');
            return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
        }

        return Path.GetFileName(path);
    }

    private void Record(string operation, string path, string? value = null)
    {
        Calls.Add(new FileSystemCall(operation, path, value));
    }

    private void ThrowIfFailureQueued()
    {
        if (_failures.Count > 0)
        {
            throw _failures.Dequeue();
        }
    }

    private void EnsureParentDirectoryExists(string path)
    {
        EnsureDirectoryExists(GetParentPath(path));
    }

    private void EnsureDirectoryExists(string path)
    {
        if (IsRootDirectory(path))
        {
            return;
        }

        if (!_directories.Contains(path))
        {
            throw new DirectoryNotFoundException(path);
        }
    }

    private void EnsureFileOrDirectoryExists(string path)
    {
        if (
            !_files.ContainsKey(path)
            && !_directories.Contains(path)
            && !_symbolicLinks.ContainsKey(path)
        )
        {
            throw new FileNotFoundException(
                "The in-memory file system entry does not exist.",
                path
            );
        }
    }

    private void ThrowIfDirectoryExists(string path)
    {
        if (_directories.Contains(path))
        {
            throw new IOException(
                $"Cannot create in-memory file '{path}' because a directory exists at that path."
            );
        }
    }

    private void AddDirectoryWithParents(string path, UnixFileMode? createdDirectoryMode = null)
    {
        if (path.Length == 0 || IsRootDirectory(path))
        {
            return;
        }

        if (_files.ContainsKey(path))
        {
            throw new IOException(
                $"Cannot create in-memory directory '{path}' because a file exists at that path."
            );
        }

        AddDirectoryWithParents(GetParentPath(path), createdDirectoryMode);
        if (_directories.Add(path))
        {
            _identities.TryAdd(path, CreateIdentity());
            _owners.TryAdd(path, CurrentOwner);
            if (createdDirectoryMode is { } mode)
            {
                _unixFileModes[path] = mode;
            }
        }
    }
}
