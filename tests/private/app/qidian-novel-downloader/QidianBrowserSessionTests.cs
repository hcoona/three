using System.Reflection;
using System.Text.Json;
using Hcoona.QidianNovelDownloader.Browser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class QidianBrowserSessionTests
{
    [Fact]
    public async Task GetLoginStateAsyncTreatsInitialAnonymousSampleAsProvisional()
    {
        FakePage page = new(
            new LoginState(false, null),
            new LoginState(true, "tester"));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            AppConstants.QidianBaseUrl,
            CancellationToken.None);

        Assert.True(loginState.IsValidated);
        Assert.Equal("tester", loginState.UserName);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(2, page.EvaluateLoginStateCalls);
        Assert.Equal(1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncReturnsAnonymousOnlyAfterPollingBudgetExpires()
    {
        FakePage page = new(new LoginState(false, null));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            AppConstants.QidianBaseUrl,
            CancellationToken.None);

        Assert.False(loginState.IsLoggedIn);
        Assert.Null(loginState.UserName);
        Assert.Equal(QidianBrowserSession.LoginStateProbeAttempts, page.EvaluateLoginStateCalls);
        Assert.Equal(QidianBrowserSession.LoginStateProbeAttempts - 1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncSucceedsOnceSessionIsAuthenticated()
    {
        FakePage page = new(
            new LoginState(false, null),
            new LoginState(true, null));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.WaitForManualLoginAsync(CancellationToken.None);

        Assert.True(loginState.IsLoggedIn);
        Assert.False(loginState.IsValidated);
        Assert.Null(loginState.UserName);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(2, page.EvaluateLoginStateCalls);
        Assert.Equal(1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncRequiresValidatedIdentityWhenRequested()
    {
        FakePage page = new(
            new LoginState(false, null),
            new LoginState(true, null),
            new LoginState(true, "tester"));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.WaitForManualLoginAsync(
            CancellationToken.None,
            requireValidatedIdentity: true);

        Assert.True(loginState.IsValidated);
        Assert.Equal("tester", loginState.UserName);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(3, page.EvaluateLoginStateCalls);
        Assert.Equal(2, page.WaitForTimeoutCalls);
    }

    private static QidianBrowserSession CreateSession(IPage page)
        => new(
            NullLogger.Instance,
            CreateProxy<IPlaywright>((method, arguments) => method.Name switch
            {
                nameof(IDisposable.Dispose) => null,
                _ => throw new NotSupportedException($"Unexpected IPlaywright call: {method.Name}"),
            }),
            CreateProxy<IBrowserContext>((method, arguments) => method.Name switch
            {
                nameof(IBrowserContext.CloseAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException($"Unexpected IBrowserContext call: {method.Name}"),
            }),
            page,
            new BrowserLaunchPlan(
                BrowserRuntimeKind.PlaywrightChromium,
                Channel: null,
                ExecutablePath: null,
                DisplayName: "test"));

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        T proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed class FakePage
    {
        private readonly Queue<LoginState> loginStates;
        private LoginState latestState;

        public int EvaluateLoginStateCalls { get; private set; }

        public int WaitForTimeoutCalls { get; private set; }

        public string? LastNavigatedUrl { get; private set; }

        public FakePage(params LoginState[] initialStates)
        {
            loginStates = new Queue<LoginState>(initialStates);
            latestState = initialStates.Length > 0
                ? initialStates[0].WithNormalizedUserName()
                : new LoginState(false, null);
            Page = CreateProxy<IPage>(Invoke);
        }

        public IPage Page { get; }

        private object? Invoke(MethodInfo method, object?[]? arguments)
            => method.Name switch
            {
                nameof(IPage.GotoAsync) => HandleGoto(arguments),
                nameof(IPage.WaitForTimeoutAsync) => HandleWaitForTimeout(),
                nameof(IPage.EvaluateAsync) => HandleEvaluate(method),
                "get_IsClosed" => false,
                _ => throw new NotSupportedException($"Unexpected IPage call: {method.Name}"),
            };

        private Task<IResponse> HandleGoto(object?[]? arguments)
        {
            LastNavigatedUrl = arguments is [string url, ..] ? url : null;
            return Task.FromResult<IResponse>(null!);
        }

        private Task HandleWaitForTimeout()
        {
            WaitForTimeoutCalls++;
            return Task.CompletedTask;
        }

        private Task<string> HandleEvaluate(MethodInfo method)
        {
            EvaluateLoginStateCalls++;
            if (loginStates.TryDequeue(out LoginState? loginState))
            {
                latestState = loginState.WithNormalizedUserName();
            }

            if (!method.IsGenericMethod || method.GetGenericArguments()[0] != typeof(string))
            {
                throw new NotSupportedException($"Unexpected EvaluateAsync signature: {method}");
            }

            string json = JsonSerializer.Serialize(new
            {
                isLoggedIn = latestState.IsLoggedIn,
                userName = latestState.UserName,
            });
            return Task.FromResult(json);
        }
    }

#pragma warning disable CA1852
    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler?.Invoke(
                targetMethod ?? throw new InvalidOperationException("Target method was not provided."),
                args);
    }
#pragma warning restore CA1852
}
