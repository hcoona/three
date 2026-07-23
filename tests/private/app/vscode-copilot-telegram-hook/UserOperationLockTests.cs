using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class UserOperationLockTests
{
    [Fact]
    public async Task AcquireAsyncWaitsUntilExistingOwnerReleasesLock()
    {
        string lockFilePath = Path.Combine(
            Path.GetTempPath(),
            $"user-operation-lock-test-{Guid.NewGuid():N}.lock");
        UserOperationLock firstOwner = await UserOperationLock.AcquireAsync(
            lockFilePath,
            CancellationToken.None);
        using CancellationTokenSource cancellationSource = new(TimeSpan.FromSeconds(5));
        Task<UserOperationLock> pendingAcquisition = UserOperationLock.AcquireAsync(
            lockFilePath,
            cancellationSource.Token);

        try
        {
            await Task.Delay(250, CancellationToken.None);
            Assert.False(pendingAcquisition.IsCompleted);
        }
        finally
        {
            await firstOwner.DisposeAsync();
        }

        await using (UserOperationLock secondOwner = await pendingAcquisition)
        {
        }

        File.Delete(lockFilePath);
    }

    [Fact]
    public async Task AcquireAsyncFailsImmediatelyWhenLockPathIsDirectory()
    {
        DirectoryInfo lockDirectory = Directory.CreateTempSubdirectory();

        try
        {
            IOException exception = await Assert.ThrowsAsync<IOException>(
                () => UserOperationLock.AcquireAsync(
                    lockDirectory.FullName,
                    CancellationToken.None));

            Assert.Contains("is a directory", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            lockDirectory.Delete();
        }
    }
}
