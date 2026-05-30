using System.Text.Json;
using Hcoona.QidianNovelDownloader.Browser;
using Jint;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class PageScriptsTests
{
    [Fact]
    public void LoginStateJsonMarksMissingLoginDomIncomplete()
    {
        Engine engine = new();
        engine.Execute(
            """
            globalThis.document = {
                getElementById: () => null,
            };
            """);

        string json = engine.Evaluate($"({PageScripts.LoginStateJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("isLoggedIn").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("userName").ValueKind);
        Assert.False(document.RootElement.GetProperty("isProbeComplete").GetBoolean());
    }

    [Fact]
    public void LoginStateJsonRecognizesHiddenSignInAsCompleteLoggedOutEvidence()
    {
        Engine engine = new();
        engine.Execute(
            """
            const elements = {
                'sign-in': {
                    classList: {
                        contains: (className) => className === 'hidden',
                    },
                },
            };
            globalThis.document = {
                getElementById: (id) => elements[id] || null,
            };
            """);

        string json = engine.Evaluate($"({PageScripts.LoginStateJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("isLoggedIn").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("userName").ValueKind);
        Assert.True(document.RootElement.GetProperty("isProbeComplete").GetBoolean());
    }

    [Fact]
    public void LoginStateJsonRecognizesUserNamePlaceholderAsCompleteLoggedOutEvidence()
    {
        Engine engine = new();
        engine.Execute(
            """
            const elements = {
                'user-name': {
                    textContent: ' 用户名 ',
                },
            };
            globalThis.document = {
                getElementById: (id) => elements[id] || null,
            };
            """);

        string json = engine.Evaluate($"({PageScripts.LoginStateJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("isLoggedIn").GetBoolean());
        Assert.Equal("用户名", document.RootElement.GetProperty("userName").GetString());
        Assert.True(document.RootElement.GetProperty("isProbeComplete").GetBoolean());
    }

    [Fact]
    public void ChapterContentJsonPreservesNumericOnlyParagraphs()
    {
        Engine engine = new();
        engine.Execute(
            """
            const paragraphNodes = [
                { textContent: ' 12345 ' },
                { textContent: 'Text paragraph' },
            ];
            globalThis.window = {
                location: {
                    href: 'https://www.qidian.com/chapter/1045928363/1/',
                },
            };
            globalThis.document = {
                body: { innerText: '' },
                querySelectorAll: (selector) => selector === 'span.content-text'
                    ? paragraphNodes
                    : [],
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "https://www.qidian.com/chapter/1045928363/1/",
            document.RootElement.GetProperty("pageUrl").GetString());
        Assert.Equal(
            "span.content-text",
            document.RootElement.GetProperty("contentSelector").GetString());
        JsonElement paragraphs = document.RootElement.GetProperty("paragraphs");
        Assert.Equal(2, paragraphs.GetArrayLength());
        Assert.Equal("12345", paragraphs[0].GetString());
        Assert.Equal("Text paragraph", paragraphs[1].GetString());
    }

    [Fact]
    public void ChapterContentJsonRejectsGenericMainParagraphsAtSamePageUrl()
    {
        Engine engine = new();
        engine.Execute(
            """
            const paragraphNodes = [
                { textContent: 'error/login/captcha' },
            ];
            globalThis.mainParagraphQueryCount = 0;
            globalThis.recognizedContentQueryCount = 0;
            globalThis.window = {
                location: {
                    href: 'https://www.qidian.com/chapter/1045928363/1/',
                },
            };
            globalThis.document = {
                body: { innerText: 'Legit page text' },
                querySelectorAll: (selector) => {
                    if (selector === 'main p') {
                        globalThis.mainParagraphQueryCount++;
                        return paragraphNodes;
                    }

                    if ([
                        'span.content-text',
                        '.read-content p',
                        '.chapter-content p',
                        '#j_chapterContent p',
                    ].includes(selector)) {
                        globalThis.recognizedContentQueryCount++;
                    }

                    return [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "https://www.qidian.com/chapter/1045928363/1/",
            document.RootElement.GetProperty("pageUrl").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
        Assert.Equal(0, engine.Evaluate("globalThis.mainParagraphQueryCount").AsNumber());
        Assert.True(engine.Evaluate("globalThis.recognizedContentQueryCount").AsNumber() > 0);
    }

    [Theory]
    [InlineData("span.content-text", "请登录")]
    [InlineData("span.content-text", "请登录后继续阅读")]
    [InlineData(".read-content p", "captcha")]
    [InlineData(".chapter-content p", "access denied")]
    [InlineData("#j_chapterContent p", "interstitial")]
    public void ChapterContentJsonRejectsMarkerTextInRecognizedContentContainer(
        string contentSelector,
        string markerText)
    {
        Engine engine = new();
        string contentSelectorJson = JsonSerializer.Serialize(contentSelector);
        string markerTextJson = JsonSerializer.Serialize(markerText);
        engine.Execute(
            $$"""
            const selectorUnderTest = {{contentSelectorJson}};
            const markerText = {{markerTextJson}};
            globalThis.markerNodeTextReadCount = 0;
            const paragraphNodes = [
                {
                    get textContent() {
                        globalThis.markerNodeTextReadCount++;
                        return ` ${markerText} `;
                    },
                    cloneNode: () => ({
                        get textContent() {
                            globalThis.markerNodeTextReadCount++;
                            return ` ${markerText} `;
                        },
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.window = {
                location: {
                    href: 'https://www.qidian.com/chapter/1045928363/1/',
                },
            };
            globalThis.document = {
                body: { innerText: '' },
                querySelectorAll: (selector) => selector === selectorUnderTest
                    ? paragraphNodes
                    : [],
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
        Assert.True(engine.Evaluate("globalThis.markerNodeTextReadCount").AsNumber() > 0);
    }

    [Theory]
    [InlineData("登录后才能阅读")]
    [InlineData("登录后才能查看")]
    [InlineData("登录后才能访问")]
    [InlineData("阅读正文需登录后确认")]
    public void ChapterContentJsonRejectsPageLevelLoginAfterPromptBeforeRecognizedParagraphs(
        string markerText)
    {
        Engine engine = new();
        string markerTextJson = JsonSerializer.Serialize(markerText);
        engine.Execute(
            $$"""
            const markerText = {{markerTextJson}};
            const paragraphNodes = [
                {
                    textContent: ' Stale recognized paragraph ',
                    cloneNode: () => ({
                        textContent: ' Stale recognized paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: `${markerText} Stale recognized paragraph` },
                querySelectorAll: (selector) => selector === '.chapter-content p'
                    ? paragraphNodes
                    : [],
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
    }

    [Fact]
    public void ChapterContentJsonRejectsMarkerTextOutsideExtractedParagraphs()
    {
        Engine engine = new();
        engine.Execute(
            """
            globalThis.containerTextReadCount = 0;
            const containerNode = {
                get textContent() {
                    globalThis.containerTextReadCount++;
                    return ' 请登录后继续阅读 Legit paragraph ';
                },
                querySelectorAll: () => [],
            };
            const paragraphNodes = [
                {
                    textContent: ' Legit paragraph ',
                    cloneNode: () => ({
                        textContent: ' Legit paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: 'Legit paragraph' },
                querySelectorAll: (selector) => {
                    if (selector === '.chapter-content') {
                        return [containerNode];
                    }

                    return selector === '.chapter-content p'
                        ? paragraphNodes
                        : [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
        Assert.True(engine.Evaluate("globalThis.containerTextReadCount").AsNumber() > 0);
    }

    [Fact]
    public void ChapterContentJsonRejectsSiblingMarkerTextForContentTextSpans()
    {
        Engine engine = new();
        engine.Execute(
            """
            globalThis.containerTextReadCount = 0;
            const containerNode = {
                get textContent() {
                    globalThis.containerTextReadCount++;
                    return 'Legit span 请登录后继续阅读';
                },
                parentElement: null,
                matches: (selector) => selector === '.chapter-content',
                querySelectorAll: () => [],
            };
            const spanNode = {
                textContent: ' Legit span ',
                parentElement: containerNode,
                matches: () => false,
            };
            globalThis.document = {
                body: { innerText: 'Legit span' },
                querySelectorAll: (selector) => selector === 'span.content-text'
                    ? [spanNode]
                    : [],
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
        Assert.True(engine.Evaluate("globalThis.containerTextReadCount").AsNumber() > 0);
    }

    [Fact]
    public void ChapterContentJsonRejectsDomMarkerOutsideExtractedParagraphs()
    {
        Engine engine = new();
        engine.Execute(
            """
            const containerNode = {
                textContent: ' Legit paragraph ',
                querySelectorAll: (selector) => selector === '.captcha'
                    ? [{ textContent: '' }]
                    : [],
            };
            const paragraphNodes = [
                {
                    textContent: ' Legit paragraph ',
                    cloneNode: () => ({
                        textContent: ' Legit paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: 'Legit paragraph' },
                querySelectorAll: (selector) => {
                    if (selector === '.read-content') {
                        return [containerNode];
                    }

                    return selector === '.read-content p'
                        ? paragraphNodes
                        : [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
    }

    [Fact]
    public void ChapterContentJsonRejectsSelfMarkedRecognizedContentContainer()
    {
        Engine engine = new();
        engine.Execute(
            """
            const containerNode = {
                textContent: ' Legit paragraph ',
                matches: (selector) => selector === '.error'
                    || selector === '[class*="error"]',
                querySelectorAll: () => [],
            };
            const paragraphNodes = [
                {
                    textContent: ' Legit paragraph ',
                    cloneNode: () => ({
                        textContent: ' Legit paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: 'Legit paragraph' },
                querySelectorAll: (selector) => {
                    if (selector === '.read-content') {
                        return [containerNode];
                    }

                    return selector === '.read-content p'
                        ? paragraphNodes
                        : [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
    }

    [Theory]
    [InlineData("ErrorPage", true)]
    [InlineData("Interstitial", false)]
    [InlineData("ACCESS-DENIED", false)]
    public void ChapterContentJsonRejectsCaseInsensitiveDomMarkersInRecognizedContentContainer(
        string markerClass,
        bool markContainer)
    {
        Engine engine = new();
        string markerClassJson = JsonSerializer.Serialize(markerClass);
        string markContainerJson = JsonSerializer.Serialize(markContainer);
        engine.Execute(
            $$"""
            const markerClass = {{markerClassJson}};
            const markContainer = {{markContainerJson}};
            const markerNode = {
                textContent: '',
                className: markerClass,
                getAttribute: (name) => name === 'class' ? markerClass : '',
            };
            const containerNode = {
                textContent: ' Legit paragraph ',
                className: markContainer ? markerClass : 'chapter-content',
                getAttribute: (name) => name === 'class'
                    ? (markContainer ? markerClass : 'chapter-content')
                    : '',
                querySelectorAll: (selector) => selector === '[id], [class]'
                    && !markContainer
                    ? [markerNode]
                    : [],
            };
            const paragraphNodes = [
                {
                    textContent: ' Legit paragraph ',
                    cloneNode: () => ({
                        textContent: ' Legit paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: 'Legit paragraph' },
                querySelectorAll: (selector) => {
                    if (selector === '.chapter-content') {
                        return [containerNode];
                    }

                    return selector === '.chapter-content p'
                        ? paragraphNodes
                        : [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
    }

    [Fact]
    public void ChapterContentJsonRejectsSelfMarkedSpanParent()
    {
        Engine engine = new();
        engine.Execute(
            """
            const parentNode = {
                textContent: ' Legit span ',
                parentElement: null,
                matches: (selector) => selector === '#login'
                    || selector === '[id*="login"]',
                querySelectorAll: () => [],
            };
            const spanNode = {
                textContent: ' Legit span ',
                parentElement: parentNode,
                matches: () => false,
            };
            globalThis.document = {
                body: { innerText: 'Legit span' },
                querySelectorAll: (selector) => selector === 'span.content-text'
                    ? [spanNode]
                    : [],
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
    }

    [Fact]
    public void ChapterContentJsonPreservesRecognizedFallbackParagraphs()
    {
        Engine engine = new();
        engine.Execute(
            """
            const paragraphNodes = [
                {
                    textContent: ' Legit paragraph ',
                    cloneNode: () => ({
                        textContent: ' Legit paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: '' },
                querySelectorAll: (selector) => selector === '.read-content p'
                    ? paragraphNodes
                    : [],
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            ".read-content p",
            document.RootElement.GetProperty("contentSelector").GetString());
        JsonElement paragraphs = document.RootElement.GetProperty("paragraphs");
        Assert.Equal(1, paragraphs.GetArrayLength());
        Assert.Equal("Legit paragraph", paragraphs[0].GetString());
    }

    [Fact]
    public void ChapterContentJsonRejectsAdjacentVisiblePageMarker()
    {
        Engine engine = new();
        engine.Execute(
            """
            const markerNode = {
                textContent: '请登录后继续阅读',
                innerText: '请登录后继续阅读',
                className: 'login-modal',
                parentElement: null,
                offsetParent: {},
                getAttribute: (name) => name === 'class' ? 'login-modal' : null,
                getClientRects: () => [{ width: 1, height: 1 }],
            };
            const containerNode = {
                textContent: ' Legit paragraph ',
                querySelectorAll: () => [],
            };
            const paragraphNodes = [
                {
                    textContent: ' Legit paragraph ',
                    cloneNode: () => ({
                        textContent: ' Legit paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: 'Legit paragraph 请登录后继续阅读' },
                querySelectorAll: (selector) => {
                    if (selector === '[id], [class]') {
                        return [markerNode];
                    }

                    if (selector === '.chapter-content') {
                        return [containerNode];
                    }

                    return selector === '.chapter-content p'
                        ? paragraphNodes
                        : [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
    }

    [Theory]
    [InlineData("class", "error")]
    [InlineData("id", "error")]
    [InlineData("class", "error-wrapper")]
    [InlineData("id", "error_panel")]
    [InlineData("class", "server-error")]
    public void ChapterContentJsonRejectsVisibleGenericErrorWrapper(
        string markerAttribute,
        string markerName)
    {
        Engine engine = new();
        string markerAttributeJson = JsonSerializer.Serialize(markerAttribute);
        string markerNameJson = JsonSerializer.Serialize(markerName);
        engine.Execute(
            $$"""
            const markerAttribute = {{markerAttributeJson}};
            const markerName = {{markerNameJson}};
            const markerNode = {
                id: markerAttribute === 'id' ? markerName : '',
                textContent: 'Service unavailable',
                innerText: 'Service unavailable',
                className: markerAttribute === 'class' ? markerName : '',
                parentElement: null,
                offsetParent: {},
                getAttribute: (name) => name === markerAttribute ? markerName : null,
                getClientRects: () => [{ width: 1, height: 1 }],
            };
            const containerNode = {
                textContent: ' Service unavailable ',
                className: 'read-content',
                parentElement: markerNode,
                getAttribute: (name) => name === 'class' ? 'read-content' : null,
                querySelectorAll: () => [],
            };
            const paragraphNodes = [
                {
                    textContent: ' Service unavailable ',
                    parentElement: containerNode,
                    cloneNode: () => ({
                        textContent: ' Service unavailable ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: 'Service unavailable' },
                querySelectorAll: (selector) => {
                    if (selector === '[id], [class]') {
                        return [markerNode, containerNode];
                    }

                    if (selector === '.read-content') {
                        return [containerNode];
                    }

                    return selector === '.read-content p'
                        ? paragraphNodes
                        : [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("contentSelector").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("paragraphs").GetArrayLength());
    }

    [Theory]
    [InlineData("captcha")]
    [InlineData("error")]
    public void ChapterContentJsonIgnoresHiddenPageMarkerTemplate(string markerClass)
    {
        Engine engine = new();
        string markerClassJson = JsonSerializer.Serialize(markerClass);
        engine.Execute(
            $$"""
            const markerClass = {{markerClassJson}};
            const markerNode = {
                textContent: '',
                className: markerClass,
                hidden: true,
                parentElement: null,
                getAttribute: (name) => name === 'class' ? markerClass : null,
                getClientRects: () => [],
            };
            const paragraphNodes = [
                {
                    textContent: ' Legit paragraph ',
                    cloneNode: () => ({
                        textContent: ' Legit paragraph ',
                        querySelectorAll: () => [],
                    }),
                },
            ];
            globalThis.document = {
                body: { innerText: 'Legit paragraph' },
                querySelectorAll: (selector) => {
                    if (selector === '[id], [class]') {
                        return [markerNode];
                    }

                    return selector === '.read-content p'
                        ? paragraphNodes
                        : [];
                },
            };
            """);

        string json = engine.Evaluate($"({PageScripts.ChapterContentJson})()").AsString();

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal(
            ".read-content p",
            document.RootElement.GetProperty("contentSelector").GetString());
        JsonElement paragraphs = document.RootElement.GetProperty("paragraphs");
        Assert.Equal(1, paragraphs.GetArrayLength());
        Assert.Equal("Legit paragraph", paragraphs[0].GetString());
    }

    [Fact]
    public void CatalogJsonIncludesPurchaseRequiredIconParsing()
    {
        Assert.Contains("catalogAccessState", PageScripts.CatalogJson, StringComparison.Ordinal);
        Assert.Contains("em.iconfont", PageScripts.CatalogJson, StringComparison.Ordinal);
        Assert.Contains("", PageScripts.CatalogJson, StringComparison.Ordinal);
        Assert.Contains("'PurchaseRequired'", PageScripts.CatalogJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://www.qidian.com", "/book/1045928363/", "1045928363")]
    [InlineData("https://www.qidian.com", "/book/1045928363/catalog/", "1045928363")]
    [InlineData("https://qidian.com", "/book/1045928363/catalog/", "1045928363")]
    [InlineData("https://www.qidian.com", "/search", null)]
    [InlineData("https://www.qidian.com", "/chapter/1045928363/1/", null)]
    [InlineData("https://www.qidian.com", "/foo/book/1045928363/catalog/", null)]
    [InlineData("https://www.qidian.com", "/book/1045928363", null)]
    [InlineData("https://www.qidian.com", "/book/1045928363/chapter/", null)]
    [InlineData("https://www.qidian.com", "/book/1045928363/reviews/", null)]
    [InlineData("https://www.qidian.com", "/book/not-a-number/catalog/", null)]
    [InlineData("https://www.qidian.com", "/book/%31%30%34%35%39%32%38%33%36%33/catalog/", null)]
    [InlineData("http://www.qidian.com", "/book/1045928363/catalog/", null)]
    [InlineData("https://evil.example", "/book/1045928363/catalog/", null)]
    public void CatalogJsonExtractsBookIdOnlyFromTrustedBookPath(
        string origin,
        string pathname,
        string? expectedBookId)
    {
        string json = EvaluateCatalogJson(
            origin,
            pathname,
            $"{origin}{pathname}?next=/book/999/catalog/#/book/888/catalog/");

        using JsonDocument document = JsonDocument.Parse(json);

        if (expectedBookId is null)
        {
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("bookId").ValueKind);
        }
        else
        {
            Assert.Equal(expectedBookId, document.RootElement.GetProperty("bookId").GetString());
        }
    }

    [Theory]
    [InlineData("https://www%2eqidian.com/book/1045928363/catalog/")]
    [InlineData("https://%77%77%77.qidian.com/book/1045928363/catalog/")]
    [InlineData("//www%2eqidian.com/book/1045928363/catalog/")]
    public void CatalogJsonRejectsBookPageWhenLocationHrefHasPercentEncodedAuthorityHost(
        string href)
    {
        string json = EvaluateCatalogJsonWithLinks(
            [new("/chapter/1045928363/1/", "Chapter One")],
            href);

        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("bookId").ValueKind);
        Assert.Empty(document.RootElement.GetProperty("volumes").EnumerateArray());
    }

    [Theory]
    [InlineData("https://ｗｗｗ.qidian.com/book/1045928363/catalog/")]
    [InlineData("https://www．qidian．com/book/1045928363/catalog/")]
    [InlineData("https://ⓦⓦⓦ.qidian.com/book/1045928363/catalog/")]
    [InlineData("https://ｗｗｗ．ｑｉｄｉａｎ．ｃｏｍ/book/1045928363/catalog/")]
    [InlineData("https://www。qidian。com/book/1045928363/catalog/")]
    public void CatalogJsonRejectsBookPageWhenLocationHrefHasNonCanonicalRawAuthorityHost(
        string href)
    {
        string json = EvaluateCatalogJsonWithLinks(
            [new("/chapter/1045928363/1/", "Chapter One")],
            href);

        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("bookId").ValueKind);
        Assert.Empty(document.RootElement.GetProperty("volumes").EnumerateArray());
    }

    [Fact]
    public void CatalogJsonExtractsCatalogFromCanonicalLocationHref()
    {
        string json = EvaluateCatalogJsonWithLinks(
            [new("/chapter/1045928363/1/", "Chapter One")],
            "https://www.qidian.com/book/1045928363/catalog/");

        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("1045928363", document.RootElement.GetProperty("bookId").GetString());
        JsonElement chapters = document.RootElement
            .GetProperty("volumes")[0]
            .GetProperty("chapters");

        Assert.Single(chapters.EnumerateArray());
        Assert.Equal("1", chapters[0].GetProperty("chapterId").GetString());
        Assert.Equal(
            "https://www.qidian.com/chapter/1045928363/1/",
            chapters[0].GetProperty("url").GetString());
    }

    [Fact]
    public void CatalogJsonExtractsOnlyTrustedCanonicalChapterLinks()
    {
        string json = EvaluateCatalogJsonWithLinks(
            [
                new("/search?next=/chapter/1045928363/1/", "Query Chapter"),
                new("#/chapter/1045928363/2/", "Fragment Chapter"),
                new("https://evil.example/chapter/1045928363/3/", "Foreign Chapter"),
                new("https://www.qidian.com/Chapter/1045928363/4/", "Uppercase Chapter"),
                new("https://www.qidian.com/chapter/1045928363/5", "Missing Slash Chapter"),
                new("https://www.qidian.com/chapter/1045928363/6/?from=app", "Search Chapter"),
                new("https://www.qidian.com/chapter/1045928363/6/?", "Bare Search Chapter"),
                new("https://www.qidian.com/chapter/1045928363/6/#", "Bare Fragment Chapter"),
                new("/chapter/1045928363/6/?", "Relative Bare Search Chapter"),
                new("/chapter/1045928363/6/#", "Relative Bare Fragment Chapter"),
                new("https://www.qidian.com/chapter/9999999999/7/", "Cross Book Chapter"),
                new("https://www.qidian.com/chapter/1045928363/%38/", "Encoded Chapter"),
                new("https://www.qidian.com/chapter/%31%30%34%35%39%32%38%33%36%33/9/", "Encoded Book"),
                new("/chapter/1045928363/../1045928363/8/", "Dot Segment Chapter"),
                new("https://www.qidian.com/chapter/1045928363/./9/", "Current Dot Segment Chapter"),
                new("/chapter/1045928363/%2e%2e/1045928363/10/", "Encoded Dot Segment Chapter"),
                new("/chapter/1045928363/%2E./11/", "Mixed Encoded Dot Segment Chapter"),
                new("/chapter/1045928363/.%2e/1045928363/12/", "Partly Encoded Dot Segment Chapter"),
                new("/chapter\\1045928363\\13\\", "Relative Backslash Chapter"),
                new("https://www.qidian.com/chapter\\1045928363\\14\\", "Absolute Backslash Chapter"),
                new("https://attacker@www.qidian.com/chapter/1045928363/15/", "Userinfo Chapter"),
                new(" https://@www.qidian.com/chapter/1045928363/15/", "Empty Userinfo Chapter"),
                new("\u0001https://@www.qidian.com/chapter/1045928363/15/", "Leading C0 Userinfo Chapter"),
                new(
                    "https://attacker:password@www.qidian.com/chapter/1045928363/16/",
                    "Password Userinfo Chapter"),
                new("https:////www.qidian.com/chapter/1045928363/17/", "Extra Scheme Slash Chapter"),
                new("https:/\t//www.qidian.com/chapter/1045928363/17/", "Embedded Tab Extra Scheme Slash Chapter"),
                new("https:/\n//www.qidian.com/chapter/1045928363/17/", "Embedded LF Extra Scheme Slash Chapter"),
                new("https:/\r//www.qidian.com/chapter/1045928363/17/", "Embedded CR Extra Scheme Slash Chapter"),
                new("\u0001https:////www.qidian.com/chapter/1045928363/17/", "Leading C0 Extra Scheme Slash Chapter"),
                new("///www.qidian.com/chapter/1045928363/18/", "Triple Protocol Slash Chapter"),
                new("//\t/www.qidian.com/chapter/1045928363/18/", "Embedded Tab Triple Protocol Slash Chapter"),
                new("//\n/www.qidian.com/chapter/1045928363/18/", "Embedded LF Triple Protocol Slash Chapter"),
                new("//\r/www.qidian.com/chapter/1045928363/18/", "Embedded CR Triple Protocol Slash Chapter"),
                new("\u0001///www.qidian.com/chapter/1045928363/18/", "Leading C0 Triple Protocol Slash Chapter"),
                new("////www.qidian.com/chapter/1045928363/19/", "Quad Protocol Slash Chapter"),
                new("https://www%2eqidian.com/chapter/1045928363/20/", "Encoded Dot Host Chapter"),
                new("//www%2eqidian.com/chapter/1045928363/21/", "Protocol Encoded Dot Host Chapter"),
                new("https://%77%77%77.qidian.com/chapter/1045928363/22/", "Encoded Host Chapter"),
                new("https://www.qidian.com/chapter/1045928363/\t19/", "Embedded Tab Chapter Id Chapter"),
                new("https://www.qidian.com/chapter/1045928363/\n19/", "Embedded LF Chapter Id Chapter"),
                new("https://www.qidian.com/chapter/1045928363/\r19/", "Embedded CR Chapter Id Chapter"),
                new("https://www.qidian.com/chapter/1045928363/19/\u0001", "Trailing C0 Chapter"),
                new("\t /chapter/1045928363/1/ \r\n", "Actual Chapter 1"),
                new("//www.qidian.com/chapter/1045928363/2/", "Actual Chapter 2"),
            ]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement chapters = document.RootElement
            .GetProperty("volumes")[0]
            .GetProperty("chapters");

        Assert.Equal(2, chapters.GetArrayLength());
        Assert.Equal("1", chapters[0].GetProperty("chapterId").GetString());
        Assert.Equal("Actual Chapter 1", chapters[0].GetProperty("title").GetString());
        Assert.Equal(
            "https://www.qidian.com/chapter/1045928363/1/",
            chapters[0].GetProperty("url").GetString());
        Assert.Equal("2", chapters[1].GetProperty("chapterId").GetString());
        Assert.Equal("Actual Chapter 2", chapters[1].GetProperty("title").GetString());
        Assert.Equal(
            "https://www.qidian.com/chapter/1045928363/2/",
            chapters[1].GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("https://ｗｗｗ.qidian.com/chapter/1045928363/30/")]
    [InlineData("https://www．qidian．com/chapter/1045928363/31/")]
    [InlineData("https://ⓦⓦⓦ.qidian.com/chapter/1045928363/32/")]
    [InlineData("//ｗｗｗ.qidian.com/chapter/1045928363/33/")]
    [InlineData("https://ｗｗｗ．ｑｉｄｉａｎ．ｃｏｍ/chapter/1045928363/34/")]
    [InlineData("https://www。qidian。com/chapter/1045928363/35/")]
    public void CatalogJsonRejectsChapterLinksWithNonCanonicalRawAuthorityHosts(
        string spoofedHref)
    {
        string json = EvaluateCatalogJsonWithLinks(
            [
                new(spoofedHref, "Spoofed Chapter"),
                new("https://www.qidian.com/chapter/1045928363/1/", "Canonical Chapter"),
            ]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement chapters = document.RootElement
            .GetProperty("volumes")[0]
            .GetProperty("chapters");

        Assert.Single(chapters.EnumerateArray());
        Assert.Equal("1", chapters[0].GetProperty("chapterId").GetString());
        Assert.Equal("Canonical Chapter", chapters[0].GetProperty("title").GetString());
        Assert.Equal(
            "https://www.qidian.com/chapter/1045928363/1/",
            chapters[0].GetProperty("url").GetString());
    }

    [Fact]
    public void CatalogJsonResolvesDuplicateChapterMetadataConservatively()
    {
        string json = EvaluateCatalogJsonWithLinks(
            [
                new(
                    "/chapter/1045928363/1/",
                    "Richer Accessible Duplicate",
                    AttributeTitle: "章节字数：1,234"),
                new("/chapter/1045928363/1/", "Purchase Required Duplicate", PurchaseRequired: true),
                new("/chapter/1045928363/2/", "Other Chapter"),
            ]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement chapters = document.RootElement
            .GetProperty("volumes")[0]
            .GetProperty("chapters");

        Assert.Equal(2, chapters.GetArrayLength());
        Assert.Equal("1", chapters[0].GetProperty("chapterId").GetString());
        Assert.Equal("Richer Accessible Duplicate", chapters[0].GetProperty("title").GetString());
        Assert.Equal(
            "https://www.qidian.com/chapter/1045928363/1/",
            chapters[0].GetProperty("url").GetString());
        Assert.Equal(1234, chapters[0].GetProperty("catalogWordCount").GetInt32());
        Assert.Equal(
            "PurchaseRequired",
            chapters[0].GetProperty("catalogAccessState").GetString());
        Assert.Equal("2", chapters[1].GetProperty("chapterId").GetString());
    }

    private static string EvaluateCatalogJson(string origin, string pathname, string href)
    {
        Engine engine = new();
        engine.SetValue("origin", origin);
        engine.SetValue("pathname", pathname);
        engine.SetValue("href", href);
        engine.Execute(
            """
            globalThis.window = {
                location: {
                    origin,
                    pathname,
                    href,
                },
            };
            globalThis.document = {
                body: { innerText: '' },
                title: 'Fallback Title',
                querySelector: () => null,
                querySelectorAll: () => [],
            };
            """);

        return engine.Evaluate($"({PageScripts.CatalogJson})()").AsString();
    }

    private static string EvaluateCatalogJsonWithLinks(
        IReadOnlyList<TestLink> testLinks,
        string? locationHref = null)
    {
        Engine engine = new();
        engine.SetValue("linksJson", JsonSerializer.Serialize(testLinks));
        engine.SetValue("locationHref", locationHref ?? string.Empty);
        engine.Execute(
            """
            const origin = 'https://www.qidian.com';
            const pathname = '/book/1045928363/catalog/';
            const href = locationHref || origin + pathname;
            globalThis.URL = function(input, base) {
                let absolute = input
                    .replace(/^[\u0000-\u0020]+|[\u0000-\u0020]+$/g, '')
                    .replace(/[\u0009\u000A\u000D]/g, '');
                if (absolute.match(/^https:\/{3,}[^/]/)) {
                    absolute = 'https://' + absolute.replace(/^https:\/+/, '');
                }
                else if (absolute.match(/^\/{3,}[^/]/)) {
                    absolute = 'https://' + absolute.replace(/^\/+/, '');
                }
                else if (absolute.startsWith('//')) {
                    absolute = 'https:' + absolute;
                }
                else if (absolute.startsWith('/')) {
                    absolute = origin + absolute;
                }
                else if (absolute.startsWith('#') || absolute.startsWith('?')) {
                    absolute = base + absolute;
                }
                absolute = absolute.replaceAll('\\', '/');

                const match = absolute.match(/^(https?:\/\/[^/?#]+)([^?#]*)(\?[^#]*)?(#.*)?$/);
                if (!match) {
                    throw new Error('Invalid URL');
                }

                const authority = match[1].slice(match[1].indexOf('//') + 2);
                const userInfoSeparator = authority.lastIndexOf('@');
                const userInfo = userInfoSeparator >= 0 ? authority.slice(0, userInfoSeparator) : '';
                const host = userInfoSeparator >= 0 ? authority.slice(userInfoSeparator + 1) : authority;
                const passwordSeparator = userInfo.indexOf(':');
                const decodedHost = host.replace(
                    /%([0-9a-fA-F]{2})/g,
                    (_, hex) => String.fromCharCode(parseInt(hex, 16)));
                const normalizedHost = decodedHost
                    .replace(
                        /[\uFF01-\uFF5E]/g,
                        (character) => String.fromCharCode(character.charCodeAt(0) - 0xFEE0))
                    .replace(
                        /[\u24B6-\u24CF]/g,
                        (character) => String.fromCharCode(
                            'A'.charCodeAt(0) + character.charCodeAt(0) - 0x24B6))
                    .replace(
                        /[\u24D0-\u24E9]/g,
                        (character) => String.fromCharCode(
                            'a'.charCodeAt(0) + character.charCodeAt(0) - 0x24D0))
                    .replace(/\u3002|\uFF61/g, '.')
                    .toLowerCase();

                const normalizePath = (path) => {
                    const output = [];
                    for (const segment of path.split('/')) {
                        const normalizedSegment = segment.replace(/%2e/gi, '.');
                        if (!normalizedSegment || normalizedSegment === '.') {
                            continue;
                        }

                        if (normalizedSegment === '..') {
                            output.pop();
                            continue;
                        }

                        output.push(segment);
                    }

                    return '/' + output.join('/') + (path.endsWith('/') && output.length > 0 ? '/' : '');
                };

                this.origin = match[1].slice(0, match[1].indexOf('//') + 2) + normalizedHost;
                this.username = passwordSeparator >= 0 ? userInfo.slice(0, passwordSeparator) : userInfo;
                this.password = passwordSeparator >= 0 ? userInfo.slice(passwordSeparator + 1) : '';
                this.pathname = normalizePath(match[2] || '/');
                const rawSearch = match[3] || '';
                const rawHash = match[4] || '';
                this.search = rawSearch === '?' ? '' : rawSearch;
                this.hash = rawHash === '#' ? '' : rawHash;
                this.href = this.origin + this.pathname + rawSearch + rawHash;
            };

            globalThis.window = {
                location: {
                    origin,
                    pathname,
                    href,
                },
            };

            const sourceLinks = JSON.parse(linksJson);
            const rows = [];
            const links = sourceLinks.map((link) => ({
                textContent: link.Title,
                parentElement: null,
                getAttribute: (name) => name === 'href'
                    ? link.Href
                    : name === 'title'
                        ? link.AttributeTitle
                        : null,
            }));
            for (let index = 0; index < links.length; index++) {
                const link = links[index];
                const sourceLink = sourceLinks[index];
                const icons = sourceLink.PurchaseRequired
                    ? [{ textContent: '' }]
                    : [];
                const row = {
                    tagName: 'DIV',
                    nextElementSibling: null,
                    parentElement: null,
                    querySelector: (selector) => selector === 'em.iconfont' && icons.length > 0
                        ? icons[0]
                        : null,
                    querySelectorAll: (selector) => selector === 'a[href]'
                        ? [link]
                        : selector === 'em.iconfont'
                            ? icons
                            : [],
                };
                link.parentElement = row;
                rows.push(row);
            }
            const container = {
                tagName: 'DIV',
                nextElementSibling: null,
                parentElement: null,
                querySelector: () => null,
                querySelectorAll: (selector) => selector === 'a[href]' ? links : [],
            };
            for (const row of rows) {
                row.parentElement = container;
            }

            const heading = {
                tagName: 'H3',
                textContent: 'Volume·共2章',
                nextElementSibling: container,
            };
            globalThis.document = {
                body: { innerText: '' },
                title: 'Fallback Title',
                querySelector: () => null,
                querySelectorAll: (selector) => selector === 'h3' ? [heading] : [],
            };
            """);

        return engine.Evaluate($"({PageScripts.CatalogJson})()").AsString();
    }

    private sealed record TestLink(
        string Href,
        string Title,
        bool PurchaseRequired = false,
        string AttributeTitle = "");
}
