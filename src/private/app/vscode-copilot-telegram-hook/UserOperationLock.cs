using System.Security.Cryptography;
using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class UserOperationLock : IAsyncDisposable
{
    private const int UnixAccessDenied = 13;
    private const int UnixTryAgain = 11;
    private const int WindowsLockViolation = 33;
    private const int WindowsSharingViolation = 32;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly FileStream stream;

    private UserOperationLock(FileStream stream)
    {
        this.stream = stream;
    }

    public static async Task<UserOperationLock> AcquireAsync(
        CancellationToken cancellationToken)
        => await AcquireAsync(GetLockFilePath(), cancellationToken);

    internal static async Task<UserOperationLock> AcquireAsync(
        string lockFilePath,
        CancellationToken cancellationToken)
    {
        lockFilePath = Path.GetFullPath(lockFilePath);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(lockFilePath))
            {
                throw new IOException(
                    $"The user operation lock path is a directory: {lockFilePath}");
            }

            try
            {
                FileStreamOptions options = new()
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                return new UserOperationLock(new FileStream(lockFilePath, options));
            }
            catch (IOException ex) when (IsLockContention(ex))
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"Cannot access the user operation lock file '{lockFilePath}'.",
                    ex);
            }
            catch (IOException ex)
            {
                throw new IOException(
                    $"Cannot acquire the user operation lock file '{lockFilePath}': "
                        + ex.Message,
                    ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync();
    }

    internal static string GetLockFilePath()
    {
        string userIdentity =
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userIdentity))
        {
            userIdentity = Environment.UserName;
        }

        string identityHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity)))[..16];
        return Path.Combine(
            Path.GetTempPath(),
            $"{AppConstants.UserOperationLockFilePrefix}-{identityHash}.lock");
    }

    private static bool IsLockContention(IOException exception)
    {
        int errorCode = exception.HResult & 0xFFFF;
        return errorCode
            is UnixTryAgain
                or UnixAccessDenied
                or WindowsSharingViolation
                or WindowsLockViolation;
    }
}
