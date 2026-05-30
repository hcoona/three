using System.Collections.Concurrent;
using System.Reflection;
using Hcoona.QidianNovelDownloader.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class QidianBrowserManagerTests
{
    [Fact]
    public async Task OpenAsyncCancelsPromptlyWhilePlaywrightCreateIsPending()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IPlaywright> create = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(create.Task));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(create.Task.IsCompleted);
    }

    [Fact]
    public async Task OpenAsyncDisposesPlaywrightWhenCanceledCreateLaterSucceeds()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IPlaywright> create = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => disposed.TrySetResult(true));
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(create.Task));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        create.SetResult(playwright);

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenAsyncDeletesIsolatedAnonymousProfileWhenCanceledBeforeSessionReturn()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IPlaywright> create = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(create.Task));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token,
            isolatedAnonymous: true);
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        AssertNoAnonymousBrowserProfiles(configuration.Paths.StateRoot);
    }

    [Fact]
    public async Task OpenAsyncCancelsPromptlyWhilePersistentContextLaunchIsPending()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IBrowserContext> launch = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? launch.Task
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => disposed.TrySetResult(true));
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(launch.Task.IsCompleted);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenAsyncClosesContextWhenCanceledLaunchLaterSucceeds()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IBrowserContext> launch = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> closeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        int disposeAttempts = 0;
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: null,
            closeHandler: () =>
            {
                closeAttempted.TrySetResult(true);
                return Task.FromException(new InvalidOperationException("Close failed."));
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? launch.Task
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(
            chromium,
            () =>
            {
                disposeAttempts++;
                disposed.TrySetResult(true);
            });
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        launch.SetResult(context);

        await closeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, disposeAttempts);
    }

    [Fact]
    public async Task OpenAsyncBoundsAbandonedContextCloseWhenCanceledLaunchLaterSucceeds()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IBrowserContext> launch = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> close = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        TestLogger<QidianBrowserManager> logger = new();
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: null,
            closeHandler: () => close.Task);
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? launch.Task
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => { });
        QidianBrowserManager manager = new(
            logger,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        launch.SetResult(context);

        await WaitUntilAsync(
            () => logger.Entries.Any(
                entry => entry.EventId.Name == nameof(LogMessages.IgnoreBrowserCloseFailure)
                    && entry.Exception is TimeoutException));
    }

    [Fact]
    public async Task OpenAsyncCancelsPromptlyWhileNewPageIsPendingAndClosesContext()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IPage> newPage = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        bool contextClosed = false;
        bool playwrightDisposed = false;
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: newPage.Task,
            closeHandler: () =>
            {
                contextClosed = true;
                return Task.CompletedTask;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => playwrightDisposed = true);
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(newPage.Task.IsCompleted);
        Assert.True(contextClosed);
        Assert.True(playwrightDisposed);
    }

    [Fact]
    public async Task OpenAsyncDisposesPlaywrightWhenCanceledNewPageCleanupCloseHangs()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IPage> newPage = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> closeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> close = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: newPage.Task,
            closeHandler: () =>
            {
                closeAttempted.TrySetResult(true);
                return close.Task;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => disposed.TrySetResult(true));
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(newPage.Task.IsCompleted);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await closeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenAsyncClosesPageWhenCanceledNewPageLaterSucceeds()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IPage> newPage = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> pageClosed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        bool contextClosed = false;
        bool playwrightDisposed = false;
        IPage page = CreateStub<IPage>(
            (method, _) =>
            {
                if (method.Name == nameof(IPage.CloseAsync))
                {
                    pageClosed.TrySetResult(true);
                    return Task.CompletedTask;
                }

                throw new NotSupportedException(method.Name);
            });
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: newPage.Task,
            closeHandler: () =>
            {
                contextClosed = true;
                return Task.CompletedTask;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => playwrightDisposed = true);
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(contextClosed);
        Assert.True(playwrightDisposed);
        newPage.SetResult(page);

        await pageClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenAsyncBoundsAbandonedPageCloseWhenCanceledNewPageLaterSucceeds()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        TaskCompletionSource<IPage> newPage = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> pageClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        TestLogger<QidianBrowserManager> logger = new();
        IPage page = CreateStub<IPage>(
            (method, _) => method.Name == nameof(IPage.CloseAsync)
                ? pageClose.Task
                : throw new NotSupportedException(method.Name));
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: newPage.Task,
            closeHandler: () => Task.CompletedTask);
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => { });
        QidianBrowserManager manager = new(
            logger,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        Task<IQidianBrowserSession> open = manager.OpenAsync(
            configuration.Settings,
            configuration.Paths,
            headless: true,
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => open.WaitAsync(TimeSpan.FromSeconds(5)));
        newPage.SetResult(page);

        await WaitUntilAsync(
            () => logger.Entries.Any(
                entry => entry.EventId.Name == nameof(LogMessages.IgnoreBrowserCloseFailure)
                    && entry.Exception is TimeoutException));
    }

    [Fact]
    public async Task OpenAsyncClosesContextWhenNewPageFailsAfterContextCreation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        ResolvedAppSettings settings = configuration.Settings with
        {
            BrowserPath = Path.Combine(temporaryDirectory.FullPath, "browser.exe"),
        };
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(settings.BrowserPath, string.Empty);
        bool contextClosed = false;
        bool playwrightDisposed = false;
        InvalidOperationException expected = new("New page failed.");
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: Task.FromException<IPage>(expected),
            closeHandler: () =>
            {
                contextClosed = true;
                return Task.CompletedTask;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => playwrightDisposed = true);
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None));

        Assert.True(contextClosed);
        Assert.True(playwrightDisposed);
        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Same(expected, Assert.Single(aggregate.InnerExceptions));
    }

    [Fact]
    public async Task OpenAsyncDoesNotWaitForHangingContextCloseAfterNewPageFailure()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        ResolvedAppSettings settings = configuration.Settings with
        {
            BrowserPath = Path.Combine(temporaryDirectory.FullPath, "browser.exe"),
        };
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(settings.BrowserPath, string.Empty);
        InvalidOperationException expected = new("New page failed.");
        TaskCompletionSource<bool> closeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> closeCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: Task.FromException<IPage>(expected),
            closeHandler: () =>
            {
                closeAttempted.TrySetResult(true);
                return closeCanComplete.Task;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => disposed.TrySetResult(true));
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await closeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Same(expected, Assert.Single(aggregate.InnerExceptions));
    }

    [Fact]
    public async Task OpenAsyncIgnoresDisposeFailureAfterStartupFailureAndContinuesLaunchPlans()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        InvalidOperationException firstFailure = new("New page failed.");
        InvalidOperationException secondFailure = new("Edge startup failed.");
        InvalidOperationException thirdFailure = new("Chrome startup failed.");
        bool firstContextClosed = false;
        bool firstPlaywrightDisposeAttempted = false;
        int launchAttempts = 0;
        IBrowserContext firstContext = CreateBrowserContext(
            pages: [],
            newPageTask: Task.FromException<IPage>(firstFailure),
            closeHandler: () =>
            {
                firstContextClosed = true;
                return Task.CompletedTask;
            });
        IPlaywright firstPlaywright = CreatePlaywright(
            CreateStub<IBrowserType>(
                (method, _) =>
                {
                    if (method.Name == nameof(IBrowserType.LaunchPersistentContextAsync))
                    {
                        launchAttempts++;
                        return Task.FromResult(firstContext);
                    }

                    throw new NotSupportedException(method.Name);
                }),
            () =>
            {
                firstPlaywrightDisposeAttempted = true;
                throw new InvalidOperationException("Dispose failed.");
            });
        IPlaywright secondPlaywright = CreatePlaywright(
            CreateFailingBrowserType(secondFailure, () => launchAttempts++),
            () => { });
        IPlaywright thirdPlaywright = CreatePlaywright(
            CreateFailingBrowserType(thirdFailure, () => launchAttempts++),
            () => { });
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new SequencedPlaywrightFactory(firstPlaywright, secondPlaywright, thirdPlaywright));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                configuration.Settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None));

        Assert.True(firstContextClosed);
        Assert.True(firstPlaywrightDisposeAttempted);
        Assert.Equal(3, launchAttempts);
        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Equal(
            [firstFailure, secondFailure, thirdFailure],
            aggregate.InnerExceptions);
    }

    [Fact]
    public async Task OpenAsyncWaitsForCreatedContextCleanupBeforeTryingNextLaunchPlan()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        InvalidOperationException firstFailure = new("New page failed.");
        InvalidOperationException secondFailure = new("Edge startup failed.");
        InvalidOperationException thirdFailure = new("Chrome startup failed.");
        TaskCompletionSource<bool> firstContextCloseAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> firstContextCloseCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int launchAttempts = 0;
        IBrowserContext firstContext = CreateBrowserContext(
            pages: [],
            newPageTask: Task.FromException<IPage>(firstFailure),
            closeHandler: () =>
            {
                firstContextCloseAttempted.TrySetResult(true);
                return firstContextCloseCanComplete.Task;
            });
        IPlaywright firstPlaywright = CreatePlaywright(
            CreateStub<IBrowserType>(
                (method, _) =>
                {
                    if (method.Name == nameof(IBrowserType.LaunchPersistentContextAsync))
                    {
                        launchAttempts++;
                        return Task.FromResult(firstContext);
                    }

                    throw new NotSupportedException(method.Name);
                }),
            () => { });
        IPlaywright secondPlaywright = CreatePlaywright(
            CreateFailingBrowserType(secondFailure, () => launchAttempts++),
            () => { });
        IPlaywright thirdPlaywright = CreatePlaywright(
            CreateFailingBrowserType(thirdFailure, () => launchAttempts++),
            () => { });
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new SequencedPlaywrightFactory(firstPlaywright, secondPlaywright, thirdPlaywright));

        Task<OperationalException> open = Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                configuration.Settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None));
        await firstContextCloseAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, launchAttempts);

        firstContextCloseCanComplete.SetResult(true);
        OperationalException exception = await open.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, launchAttempts);
        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Equal(
            [firstFailure, secondFailure, thirdFailure],
            aggregate.InnerExceptions);
    }

    [Fact]
    public async Task OpenAsyncDoesNotTryNextLaunchPlanWhenCreatedContextCleanupFails()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        InvalidOperationException firstFailure = new("New page failed.");
        TaskCompletionSource<bool> firstContextCloseAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool firstPlaywrightDisposed = false;
        int launchAttempts = 0;
        IBrowserContext firstContext = CreateBrowserContext(
            pages: [],
            newPageTask: Task.FromException<IPage>(firstFailure),
            closeHandler: () =>
            {
                firstContextCloseAttempted.TrySetResult(true);
                return Task.FromException(new InvalidOperationException("Close failed."));
            });
        IPlaywright firstPlaywright = CreatePlaywright(
            CreateStub<IBrowserType>(
                (method, _) =>
                {
                    if (method.Name == nameof(IBrowserType.LaunchPersistentContextAsync))
                    {
                        launchAttempts++;
                        return Task.FromResult(firstContext);
                    }

                    throw new NotSupportedException(method.Name);
                }),
            () => firstPlaywrightDisposed = true);
        IPlaywright secondPlaywright = CreatePlaywright(
            CreateFailingBrowserType(new InvalidOperationException("Edge startup failed."), () => launchAttempts++),
            () => { });
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new SequencedPlaywrightFactory(firstPlaywright, secondPlaywright));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                configuration.Settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None));

        await firstContextCloseAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(firstPlaywrightDisposed);
        Assert.Equal(1, launchAttempts);
        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Same(firstFailure, Assert.Single(aggregate.InnerExceptions));
    }

    [Fact]
    public async Task OpenAsyncDeletesIsolatedAnonymousProfileWhenStartupFailsBeforeSessionReturn()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        ResolvedAppSettings settings = configuration.Settings with
        {
            BrowserPath = Path.Combine(temporaryDirectory.FullPath, "browser.exe"),
        };
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(settings.BrowserPath, string.Empty);
        InvalidOperationException expected = new("Launch failed.");
        IPlaywright playwright = CreatePlaywright(
            CreateFailingBrowserType(expected, () => { }),
            () => { });
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None,
                isolatedAnonymous: true));

        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Same(expected, Assert.Single(aggregate.InnerExceptions));
        AssertNoAnonymousBrowserProfiles(configuration.Paths.StateRoot);
    }

    [Fact]
    public async Task OpenAsyncClosesContextWhenNewPageFailsWithProfileLockConflict()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        ResolvedAppSettings settings = configuration.Settings with
        {
            BrowserPath = Path.Combine(temporaryDirectory.FullPath, "browser.exe"),
            BrowserProfileDir = Path.Combine(temporaryDirectory.FullPath, "profile"),
        };
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(settings.BrowserPath, string.Empty);
        bool contextClosed = false;
        bool playwrightDisposed = false;
        InvalidOperationException expected = new("SingletonLock profile appears to be in use.");
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: Task.FromException<IPage>(expected),
            closeHandler: () =>
            {
                contextClosed = true;
                return Task.CompletedTask;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => playwrightDisposed = true);
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None));

        Assert.True(contextClosed);
        Assert.True(playwrightDisposed);
        Assert.Same(expected, exception.InnerException);
    }

    [Fact]
    public async Task OpenAsyncDoesNotWaitForHangingContextCloseAfterProfileLockFailure()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        ResolvedAppSettings settings = configuration.Settings with
        {
            BrowserPath = Path.Combine(temporaryDirectory.FullPath, "browser.exe"),
            BrowserProfileDir = Path.Combine(temporaryDirectory.FullPath, "profile"),
        };
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(settings.BrowserPath, string.Empty);
        InvalidOperationException expected = new("SingletonLock profile appears to be in use.");
        TaskCompletionSource<bool> closeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> closeCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IBrowserContext context = CreateBrowserContext(
            pages: [],
            newPageTask: Task.FromException<IPage>(expected),
            closeHandler: () =>
            {
                closeAttempted.TrySetResult(true);
                return closeCanComplete.Task;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => disposed.TrySetResult(true));
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await closeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(expected, exception.InnerException);
    }

    [Fact]
    public async Task OpenAsyncDisposesPlaywrightWhenCreatedContextCloseDoesNotComplete()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> closeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> closeCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IPage page = CreateStub<IPage>((method, _) => throw new NotSupportedException(method.Name));
        IBrowserContext context = CreateBrowserContext(
            pagesFactory: () =>
            {
                cancellation.Cancel();
                return [page];
            },
            closeHandler: () =>
            {
                closeAttempted.TrySetResult(true);
                return closeCanComplete.Task;
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => disposed.TrySetResult(true));
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.OpenAsync(
                configuration.Settings,
                configuration.Paths,
                headless: true,
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await closeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenAsyncPreservesCancellationWhenDetachedContextCloseFails()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> closeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IPage page = CreateStub<IPage>((method, _) => throw new NotSupportedException(method.Name));
        IBrowserContext context = CreateBrowserContext(
            pagesFactory: () =>
            {
                cancellation.Cancel();
                return [page];
            },
            closeHandler: () =>
            {
                closeAttempted.TrySetResult(true);
                return Task.FromException(new InvalidOperationException("Close failed."));
            });
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) => method.Name == nameof(IBrowserType.LaunchPersistentContextAsync)
                ? Task.FromResult(context)
                : throw new NotSupportedException(method.Name));
        IPlaywright playwright = CreatePlaywright(chromium, () => disposed.TrySetResult(true));
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.OpenAsync(
                configuration.Settings,
                configuration.Paths,
                headless: true,
                cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await closeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenAsyncTreatsOperationCanceledExceptionFromPlaywrightCreateAsStartupFailure()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        ResolvedAppSettings settings = configuration.Settings with
        {
            BrowserPath = Path.Combine(temporaryDirectory.FullPath, "browser.exe"),
        };
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(settings.BrowserPath, string.Empty);
        OperationCanceledException expected = new("Startup was canceled.");
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromException<IPlaywright>(expected)));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => manager.OpenAsync(
                settings,
                configuration.Paths,
                headless: true,
                CancellationToken.None));

        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Same(expected, Assert.Single(aggregate.InnerExceptions));
    }

    [Fact]
    public async Task FetchChapterAsyncFailsClosedWhenPageScriptReportsRejection()
    {
        const string chapterUrl = "https://www.qidian.com/chapter/100/1/";
        const string rejectedChapterJson = """
            {
                "pageUrl": "https://www.qidian.com/chapter/100/1/",
                "contentSelector": ".chapter-content p",
                "isPreview": false,
                "rejected": true,
                "paragraphs": ["clean paragraph"]
            }
            """;
        IPage page = CreateStub<IPage>(
            (method, _) => method.Name switch
            {
                "get_Url" => chapterUrl,
                nameof(IPage.GotoAsync) => Task.FromResult<IResponse?>(null),
                nameof(IPage.WaitForTimeoutAsync) => Task.CompletedTask,
                nameof(IPage.WaitForSelectorAsync) => Task.FromResult<IElementHandle?>(null),
                nameof(IPage.EvaluateAsync) => Task.FromResult(rejectedChapterJson),
                _ => throw new NotSupportedException(method.Name),
            });
        IBrowserContext context = CreateBrowserContext(
            pages: [page],
            newPageTask: null,
            closeHandler: () => Task.CompletedTask);
        IPlaywright playwright = CreatePlaywright(
            CreateStub<IBrowserType>((method, _) => throw new NotSupportedException(method.Name)),
            () => { });
        QidianBrowserSession session = new(
            NullLogger<QidianBrowserManager>.Instance,
            playwright,
            context,
            page,
            new BrowserLaunchPlan(
                BrowserRuntimeKind.PlaywrightChromium,
                Channel: null,
                ExecutablePath: null,
                DisplayName: "Test"));

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchChapterAsync(
                "100",
                new ChapterDescriptor(
                    "1",
                    "Chapter One",
                    chapterUrl,
                    IsVip: false,
                    CatalogWordCount: 100,
                    CatalogChapterAccessState.Accessible),
                CancellationToken.None));

        Assert.Contains("contained login, captcha, error, or interstitial markers", exception.Message);
    }

    [Fact]
    public async Task OpenAsyncPrefersCancellationWhenStartupFailureRacesWithCancellation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        (ResolvedAppSettings Settings, AppStoragePaths Paths) configuration =
            CreateBrowserConfiguration(temporaryDirectory.FullPath);
        using CancellationTokenSource cancellation = new();
        bool playwrightDisposed = false;
        IBrowserType chromium = CreateStub<IBrowserType>(
            (method, _) =>
            {
                if (method.Name == nameof(IBrowserType.LaunchPersistentContextAsync))
                {
                    cancellation.Cancel();
                    return Task.FromException<IBrowserContext>(
                        new InvalidOperationException("Startup failed."));
                }

                throw new NotSupportedException(method.Name);
            });
        IPlaywright playwright = CreateStub<IPlaywright>(
            (method, _) =>
            {
                if (method.Name == $"get_{nameof(IPlaywright.Chromium)}")
                {
                    return chromium;
                }

                if (method.Name == "Dispose"
                    || method.Name.EndsWith(".Dispose", StringComparison.Ordinal))
                {
                    playwrightDisposed = true;
                    return null;
                }

                throw new NotSupportedException(method.Name);
            });
        QidianBrowserManager manager = new(
            NullLogger<QidianBrowserManager>.Instance,
            new StubPlaywrightFactory(Task.FromResult(playwright)));

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.OpenAsync(
                configuration.Settings,
                configuration.Paths,
                headless: true,
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(playwrightDisposed);
    }

    [Fact]
    public async Task OpenAsyncDoesNotCreateBrowserProfileUnderReparseAncestor()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside");
        string linkRoot = Path.Combine(temporaryDirectory.FullPath, "linked-profile-root");
        Directory.CreateDirectory(outsideRoot);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory.FullPath))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            Directory.CreateSymbolicLink(linkRoot, outsideRoot);
            ResolvedAppSettings settings = new(
                BrowserPath: null,
                BrowserProfileDir: Path.Combine(linkRoot, "profile"),
                OutputDir: null,
                ReadingSpeed: 5000,
                MinimumRequestDelaySeconds: 5,
                MaximumRequestDelaySeconds: 12,
                RetryCount: 3,
                CatalogCacheTtlHours: 24,
                DefaultBooks: []);
            AppStoragePaths paths = new(
                stateRoot,
                Path.Combine(stateRoot, AppConstants.ConfigFileName),
                Path.Combine(stateRoot, AppConstants.CacheDirectoryName),
                Path.Combine(stateRoot, AppConstants.LogsDirectoryName),
                Path.Combine(stateRoot, AppConstants.OutputDirectoryName),
                Path.Combine(stateRoot, AppConstants.BrowserProfileDirectoryName),
                BrowserProfileDirectory: null);
            QidianBrowserManager manager = new(
                NullLogger<QidianBrowserManager>.Instance);

            await Assert.ThrowsAsync<IOException>(
                () => manager.OpenAsync(
                    settings,
                    paths,
                    headless: true,
                    CancellationToken.None));
        }
        finally
        {
            DeleteReparseDirectoryIfExists(linkRoot);
        }

        Assert.False(Directory.Exists(Path.Combine(outsideRoot, "profile")));
    }

    [Fact]
    public async Task OpenAsyncRejectsResolvedProfileDirectoryReparsePoint()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string userDataDir = Path.Combine(temporaryDirectory.FullPath, "User Data");
        string profileDirectory = Path.Combine(userDataDir, "Profile 7");
        string outsideProfileDirectory = Path.Combine(temporaryDirectory.FullPath, "outside-profile");
        Directory.CreateDirectory(userDataDir);
        Directory.CreateDirectory(outsideProfileDirectory);
        File.WriteAllText(Path.Combine(userDataDir, "Local State"), "{}");
        File.WriteAllText(Path.Combine(outsideProfileDirectory, "Preferences"), "{}");
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory.FullPath))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            Directory.CreateSymbolicLink(profileDirectory, outsideProfileDirectory);
            ResolvedAppSettings settings = new(
                BrowserPath: null,
                BrowserProfileDir: profileDirectory,
                OutputDir: null,
                ReadingSpeed: 5000,
                MinimumRequestDelaySeconds: 5,
                MaximumRequestDelaySeconds: 12,
                RetryCount: 3,
                CatalogCacheTtlHours: 24,
                DefaultBooks: []);
            AppStoragePaths paths = new(
                stateRoot,
                Path.Combine(stateRoot, AppConstants.ConfigFileName),
                Path.Combine(stateRoot, AppConstants.CacheDirectoryName),
                Path.Combine(stateRoot, AppConstants.LogsDirectoryName),
                Path.Combine(stateRoot, AppConstants.OutputDirectoryName),
                Path.Combine(stateRoot, AppConstants.BrowserProfileDirectoryName),
                BrowserProfileDirectory: null);
            QidianBrowserManager manager = new(
                NullLogger<QidianBrowserManager>.Instance);

            await Assert.ThrowsAsync<IOException>(
                () => manager.OpenAsync(
                    settings,
                    paths,
                    headless: true,
                    CancellationToken.None));
        }
        finally
        {
            DeleteReparseDirectoryIfExists(profileDirectory);
        }

        Assert.Equal(
            ["Preferences"],
            Directory.GetFiles(outsideProfileDirectory).Select(Path.GetFileName));
    }

    private static (ResolvedAppSettings Settings, AppStoragePaths Paths) CreateBrowserConfiguration(
        string root)
    {
        string stateRoot = Path.Combine(root, "state");
        ResolvedAppSettings settings = new(
            BrowserPath: null,
            BrowserProfileDir: null,
            OutputDir: null,
            ReadingSpeed: 5000,
            MinimumRequestDelaySeconds: 5,
            MaximumRequestDelaySeconds: 12,
            RetryCount: 3,
            CatalogCacheTtlHours: 24,
            DefaultBooks: []);
        AppStoragePaths paths = new(
            stateRoot,
            Path.Combine(stateRoot, AppConstants.ConfigFileName),
            Path.Combine(stateRoot, AppConstants.CacheDirectoryName),
            Path.Combine(stateRoot, AppConstants.LogsDirectoryName),
            Path.Combine(stateRoot, AppConstants.OutputDirectoryName),
            Path.Combine(stateRoot, AppConstants.BrowserProfileDirectoryName),
            BrowserProfileDirectory: null);
        return (settings, paths);
    }

    private static void AssertNoAnonymousBrowserProfiles(string stateRoot)
    {
        Assert.Empty(
            Directory.Exists(stateRoot)
                ? Directory.GetDirectories(stateRoot, "anonymous-browser-profile-*")
                : []);
    }

    private static T CreateStub<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        T stub = DispatchProxy.Create<T, StubDispatchProxy>();
        ((StubDispatchProxy)(object)stub).Handler = handler;
        return stub;
    }

    private static IPlaywright CreatePlaywright(IBrowserType chromium, Action disposeHandler)
        => CreateStub<IPlaywright>(
            (method, _) =>
            {
                if (method.Name == $"get_{nameof(IPlaywright.Chromium)}")
                {
                    return chromium;
                }

                if (method.Name == "Dispose"
                    || method.Name.EndsWith(".Dispose", StringComparison.Ordinal))
                {
                    disposeHandler();
                    return null;
                }

                throw new NotSupportedException(method.Name);
            });

    private static IBrowserType CreateFailingBrowserType(Exception failure, Action launchHandler)
        => CreateStub<IBrowserType>(
            (method, _) =>
            {
                if (method.Name == nameof(IBrowserType.LaunchPersistentContextAsync))
                {
                    launchHandler();
                    return Task.FromException<IBrowserContext>(failure);
                }

                throw new NotSupportedException(method.Name);
            });

    private static IBrowserContext CreateBrowserContext(
        IReadOnlyList<IPage> pages,
        Task<IPage>? newPageTask,
        Func<Task> closeHandler)
        => CreateBrowserContext(() => pages, closeHandler, newPageTask);

    private static IBrowserContext CreateBrowserContext(
        Func<IReadOnlyList<IPage>> pagesFactory,
        Func<Task> closeHandler,
        Task<IPage>? newPageTask = null)
        => CreateStub<IBrowserContext>(
            (method, _) =>
            {
                if (method.Name == $"get_{nameof(IBrowserContext.Pages)}")
                {
                    return pagesFactory();
                }

                if (method.Name == nameof(IBrowserContext.NewPageAsync))
                {
                    return newPageTask
                        ?? throw new NotSupportedException(method.Name);
                }

                if (method.Name == nameof(IBrowserContext.CloseAsync))
                {
                    return closeHandler();
                }

                throw new NotSupportedException(method.Name);
            });

    private static bool CanCreateDirectorySymbolicLink(string root)
    {
        string target = Path.Combine(root, "symlink-target");
        string link = Path.Combine(root, "symlink-link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Directory.Delete(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static void DeleteReparseDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string FullPath { get; } = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
    }

    private sealed class StubPlaywrightFactory(Task<IPlaywright> createTask)
        : IQidianPlaywrightFactory
    {
        public Task<IPlaywright> CreateAsync() => createTask;
    }

    private sealed class SequencedPlaywrightFactory(params IPlaywright[] playwrights)
        : IQidianPlaywrightFactory
    {
        private int index;

        public Task<IPlaywright> CreateAsync() => Task.FromResult(playwrights[index++]);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1852:Seal internal types",
        Justification = "DispatchProxy proxy base types cannot be sealed.")]
    private class StubDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            MethodInfo method =
                targetMethod ?? throw new ArgumentNullException(nameof(targetMethod));
            return Handler is null
                ? throw new NotSupportedException(method.Name)
                : Handler.Invoke(method, args);
        }
    }
}
