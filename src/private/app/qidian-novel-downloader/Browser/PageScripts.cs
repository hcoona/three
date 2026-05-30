namespace Hcoona.QidianNovelDownloader.Browser;

internal static class PageScripts
{
    public const string LoginStateJson = """
        () => {
            const signInElement = document.getElementById('sign-in');
            const userNameElement = document.getElementById('user-name');
            const isSignInHidden = signInElement
                ? signInElement.classList.contains('hidden')
                : true;
            const userName = userNameElement ? userNameElement.textContent.trim() : null;
            const isLoggedIn = (!!signInElement && !isSignInHidden)
                || (!!userName && userName !== '用户名');
            const hasLoggedOutEvidence = (!!signInElement && isSignInHidden)
                || (!!userNameElement && userName === '用户名');
            const isProbeComplete = isLoggedIn || hasLoggedOutEvidence;
            return JSON.stringify({
                isLoggedIn,
                userName,
                isProbeComplete,
            });
        }
        """;

    public const string CatalogJson = """
        () => {
            const normalizeText = (value) => value ? value.replace(/\s+/g, ' ').trim() : null;
            const readFirstText = (selectors) => {
                for (const selector of selectors) {
                    const element = document.querySelector(selector);
                    const text = normalizeText(element ? element.textContent : null);
                    if (text) {
                        return text;
                    }
                }
                return null;
            };
            const trustedOrigins = new Set([
                'https://www.qidian.com',
                'https://qidian.com',
            ]);
            const hasRawUserInfoInAuthority = (href) => {
                const authorityMatch = href.match(/^[a-zA-Z][a-zA-Z0-9+.-]*:\/\/([^/\\?#]*)/)
                    || href.match(/^\/\/([^/\\?#]*)/);
                return !!authorityMatch && authorityMatch[1].includes('@');
            };
            const hasRawPercentEncodedAuthorityHost = (href) => {
                const authorityMatch = href.match(/^[a-zA-Z][a-zA-Z0-9+.-]*:\/\/([^/\\?#]*)/)
                    || href.match(/^\/\/([^/\\?#]*)/);
                if (!authorityMatch) {
                    return false;
                }

                const authority = authorityMatch[1];
                const userInfoSeparator = authority.lastIndexOf('@');
                const host = userInfoSeparator >= 0
                    ? authority.slice(userInfoSeparator + 1)
                    : authority;
                return /%[0-9a-fA-F]{2}/.test(host);
            };
            const hasRawNonCanonicalAuthorityHost = (href) => {
                const authorityMatch = href.match(/^[a-zA-Z][a-zA-Z0-9+.-]*:\/\/([^/\\?#]*)/)
                    || href.match(/^\/\/([^/\\?#]*)/);
                if (!authorityMatch) {
                    return false;
                }

                const authority = authorityMatch[1];
                const userInfoSeparator = authority.lastIndexOf('@');
                const host = userInfoSeparator >= 0
                    ? authority.slice(userInfoSeparator + 1)
                    : authority;
                return host !== 'www.qidian.com' && host !== 'qidian.com';
            };
            const bookIdMatch = trustedOrigins.has(window.location.origin)
                && !hasRawUserInfoInAuthority(window.location.href)
                && !hasRawPercentEncodedAuthorityHost(window.location.href)
                && !hasRawNonCanonicalAuthorityHost(window.location.href)
                ? window.location.pathname.match(/^\/book\/(\d+)\/(?:catalog\/)?$/)
                : null;
            const bookId = bookIdMatch ? bookIdMatch[1] : null;
            const hasRawDotSegment = (href) => {
                const pathBeforeQueryOrFragment = href.split(/[?#]/, 1)[0];
                const authorityMatch = pathBeforeQueryOrFragment.match(
                    /^[a-zA-Z][a-zA-Z0-9+.-]*:\/\/[^/\\]*(.*)$/)
                    || pathBeforeQueryOrFragment.match(/^\/\/[^/\\]*(.*)$/);
                const rawPath = authorityMatch ? authorityMatch[1] : pathBeforeQueryOrFragment;
                return rawPath
                    .split(/[\/\\]+/)
                    .map((segment) => segment.replace(/%2e/gi, '.'))
                    .some((segment) => segment === '.' || segment === '..');
            };
            const hasRawBackslashPathSeparator = (href) => {
                const pathBeforeQueryOrFragment = href.split(/[?#]/, 1)[0];
                const authorityMatch = pathBeforeQueryOrFragment.match(
                    /^[a-zA-Z][a-zA-Z0-9+.-]*:\/\/[^/\\]*(.*)$/)
                    || pathBeforeQueryOrFragment.match(/^\/\/[^/\\]*(.*)$/);
                const rawPath = authorityMatch ? authorityMatch[1] : pathBeforeQueryOrFragment;
                return rawPath.includes('\\');
            };
            const hasRawNonCanonicalAuthoritySlashes = (href) =>
                /^[a-zA-Z][a-zA-Z0-9+.-]*:\/{3,}/.test(href)
                || /^\/{3,}/.test(href);
            const parseCanonicalChapterLink = (link) => {
                const rawHref = link.getAttribute('href');
                if (!rawHref) {
                    return null;
                }

                const href = rawHref.trim();
                if (!href) {
                    return null;
                }
                if (/[\u0000-\u001F]/.test(href)
                    || /[?#]/.test(href)
                    || hasRawUserInfoInAuthority(href)
                    || hasRawPercentEncodedAuthorityHost(href)
                    || hasRawNonCanonicalAuthorityHost(href)
                    || hasRawNonCanonicalAuthoritySlashes(href)
                    || hasRawBackslashPathSeparator(href)
                    || hasRawDotSegment(href)) {
                    return null;
                }

                let url;
                try {
                    url = new URL(href, window.location.href);
                }
                catch {
                    return null;
                }

                if (!trustedOrigins.has(url.origin)
                    || url.username
                    || url.password
                    || url.search
                    || url.hash) {
                    return null;
                }

                const match = url.pathname.match(/^\/chapter\/(\d+)\/(\d+)\/$/);
                return match && match[1] === bookId
                    ? {
                        chapterId: match[2],
                        url: url.href,
                    }
                    : null;
            };
            const isMoreConservativeChapter = (chapter, existingChapter) => {
                const chapterRank = chapter.catalogAccessState === 'Accessible' ? 0 : 1;
                const existingChapterRank =
                    existingChapter.catalogAccessState === 'Accessible' ? 0 : 1;
                return chapterRank > existingChapterRank;
            };
            const mergeDuplicateChapter = (existingChapter, chapter) => ({
                chapterId: existingChapter.chapterId,
                title: existingChapter.title || chapter.title,
                url: existingChapter.url || chapter.url,
                isVip: existingChapter.isVip || chapter.isVip,
                catalogWordCount: existingChapter.catalogWordCount ?? chapter.catalogWordCount,
                catalogAccessState: isMoreConservativeChapter(chapter, existingChapter)
                    ? chapter.catalogAccessState
                    : existingChapter.catalogAccessState,
            });
            const getCatalogChapterAccessState = (link, section) => {
                const findChapterRow = () => {
                    let fallback = link.parentElement ?? section;
                    for (
                        let element = link.parentElement;
                        element;
                        element = element.parentElement
                    ) {
                        const chapterLinkCount = [...element
                            .querySelectorAll('a[href]')]
                            .filter((candidate) => parseCanonicalChapterLink(candidate))
                            .length;
                        if (chapterLinkCount === 1) {
                            fallback = element;
                            if (element.querySelector('em.iconfont')) {
                                return element;
                            }
                        }

                        if (element === section) {
                            break;
                        }
                    }

                    return fallback;
                };

                const chapterRow = findChapterRow();
                const hasPurchaseRequiredSignal = !!chapterRow
                    && [...chapterRow.querySelectorAll('em.iconfont')]
                        .some((icon) => normalizeText(icon.textContent) === '');
                return hasPurchaseRequiredSignal ? 'PurchaseRequired' : 'Accessible';
            };

            const bodyText = document.body ? document.body.innerText : '';
            const estimatedWordCountMatch = bodyText.match(/(?:总字数|字数)\s*([0-9,]+)/);
            const estimatedWordCount = estimatedWordCountMatch
                ? Number.parseInt(estimatedWordCountMatch[1].replaceAll(',', ''), 10)
                : null;

            const title = readFirstText([
                '.book-info h1 em',
                '.book-info h1',
                'h1 em',
                'h1',
            ]) ?? (document.title ? document.title.split('_')[0].trim() : null);
            const author = readFirstText([
                '.book-info h1 a.writer',
                '.book-info h1 a[href*="/author/"]',
                '.book-info .author a',
                '#authorId',
            ]);

            const volumes = [];
            for (const heading of document.querySelectorAll('h3')) {
                const headingText = normalizeText(heading.textContent);
                if (!headingText || !headingText.includes('·共') || !headingText.includes('章')) {
                    continue;
                }

                const titleMatch = headingText.match(/^(?:订阅本卷\s*)?(.+?)·/);
                const volumeTitle = titleMatch ? titleMatch[1].trim() : headingText;
                const isVip = headingText.includes('VIP') && !headingText.includes('免费');
                const chapters = [];
                const chapterIndexesById = new Map();
                let sibling = heading.nextElementSibling;
                while (sibling && sibling.tagName !== 'H3') {
                    for (const link of sibling.querySelectorAll('a[href]')) {
                        const chapterLink = parseCanonicalChapterLink(link);
                        const chapterTitle = normalizeText(link.textContent);
                        if (!chapterLink || !chapterTitle) {
                            continue;
                        }

                        const chapterId = chapterLink.chapterId;
                        const titleText = normalizeText(link.getAttribute('title')) ?? '';
                        const wordCountMatch = titleText.match(/章节字数[:：]\s*([0-9,]+)/);
                        const chapter = {
                            chapterId,
                            title: chapterTitle,
                            url: chapterLink.url,
                            isVip,
                            catalogWordCount: wordCountMatch
                                ? Number.parseInt(wordCountMatch[1].replaceAll(',', ''), 10)
                                : null,
                            catalogAccessState: getCatalogChapterAccessState(link, sibling),
                        };
                        if (chapterIndexesById.has(chapterId)) {
                            const existingChapterIndex = chapterIndexesById.get(chapterId);
                            chapters[existingChapterIndex] = mergeDuplicateChapter(
                                chapters[existingChapterIndex],
                                chapter);
                            continue;
                        }

                        chapterIndexesById.set(chapterId, chapters.length);
                        chapters.push(chapter);
                    }

                    sibling = sibling.nextElementSibling;
                }

                if (chapters.length > 0) {
                    volumes.push({
                        title: volumeTitle,
                        isVip,
                        chapters,
                    });
                }
            }

            return JSON.stringify({
                bookId,
                title,
                author,
                estimatedWordCount,
                volumes,
            });
        }
        """;

    public const string ChapterContentJson = """
        () => {
            const pageUrl = typeof window !== 'undefined' && window.location
                ? window.location.href
                : null;
            const bodyText = document.body ? document.body.innerText : '';
            const isPreview =
                (bodyText.includes('需要订阅后才能阅读') || bodyText.includes('本章为付费章节'))
                && bodyText.includes('VIP');
            const paragraphs = [];
            let contentSelector = null;
            const isRejectedInterstitialText = (text) => {
                const normalized = text ? text.replace(/\s+/g, ' ').trim() : '';
                if (!normalized) {
                    return false;
                }

                return [
                    /(?:请|需要|您还未|未)登录(?:后|后再|才|才能|以)?(?:继续)?(?:阅读|查看|访问)?/,
                    /(?=[\s\S]*登录后)(?=[\s\S]*(?:阅读|查看|访问))/,
                    /请先登录/,
                    /验证码|captcha/i,
                    /安全验证|人机验证|滑块验证|拖动滑块/,
                    /访问过于频繁|操作过于频繁|检测到异常|系统繁忙|请稍后再试/,
                    /页面不存在|章节不存在|内容不存在|访问受限/,
                    /access denied|verify you are human|unusual traffic|interstitial|error page/i,
                ].some((pattern) => pattern.test(normalized));
            };
            const matchesSelector = (element, selector) => {
                try {
                    return !!element.matches && element.matches(selector);
                }
                catch {
                    return false;
                }
            };
            const markerDomSelectors = [
                '.captcha',
                '#captcha',
                '[class*="captcha"]',
                '[id*="captcha"]',
                '.verify',
                '#verify',
                '[class*="verify"]',
                '[id*="verify"]',
                '.login',
                '#login',
                '[class*="login"]',
                '[id*="login"]',
                '.error',
                '#error',
                '[class*="error"]',
                '[id*="error"]',
                '.interstitial',
                '#interstitial',
                '[class*="interstitial"]',
                '[id*="interstitial"]',
                '.forbidden',
                '#forbidden',
                '[class*="forbidden"]',
                '[id*="forbidden"]',
                '.blocked',
                '#blocked',
                '[class*="blocked"]',
                '[id*="blocked"]',
                '[class*="access-denied"]',
                '[id*="access-denied"]',
                '[class*="access_denied"]',
                '[id*="access_denied"]',
                '[class*="accessdenied"]',
                '[id*="accessdenied"]',
            ];
            const markerDomNameFragments = [
                'captcha',
                'verify',
                'login',
                'error',
                'interstitial',
                'forbidden',
                'blocked',
                'access-denied',
                'access_denied',
                'accessdenied',
            ];
            const readDomMarkerAttribute = (element, attributeName) => {
                try {
                    if (attributeName === 'id' && typeof element.id === 'string') {
                        return element.id;
                    }

                    if (attributeName === 'class' && typeof element.className === 'string') {
                        return element.className;
                    }

                    const value = element.getAttribute
                        ? element.getAttribute(attributeName)
                        : null;
                    return typeof value === 'string' ? value : '';
                }
                catch {
                    return '';
                }
            };
            const hasRejectedMarkerDomName = (element) => {
                const markerName = `${
                    readDomMarkerAttribute(element, 'id')
                } ${
                    readDomMarkerAttribute(element, 'class')
                }`.toLowerCase();
                return markerDomNameFragments.some(
                    (fragment) => markerName.includes(fragment));
            };
            const hasRejectedMarkerDom = (element) => {
                if (!element) {
                    return false;
                }

                if (hasRejectedMarkerDomName(element)) {
                    return true;
                }

                try {
                    if (element.querySelectorAll) {
                        for (const descendant of element.querySelectorAll('[id], [class]')) {
                            if (hasRejectedMarkerDomName(descendant)) {
                                return true;
                            }
                        }
                    }
                }
                catch {
                }

                return markerDomSelectors.some((selector) => {
                    if (matchesSelector(element, selector)) {
                        return true;
                    }

                    try {
                        return !!element.querySelectorAll
                            && element.querySelectorAll(selector).length > 0;
                    }
                    catch {
                        return false;
                    }
                });
            };
            const visiblePageMarkerDomNameFragments = [
                'captcha',
                'verify',
                'interstitial',
                'forbidden',
                'blocked',
                'access-denied',
                'access_denied',
                'accessdenied',
                'error',
                'error-page',
                'errorpage',
            ];
            const readElementText = (element) => {
                try {
                    if (typeof element.innerText === 'string') {
                        return element.innerText;
                    }

                    return typeof element.textContent === 'string'
                        ? element.textContent
                        : '';
                }
                catch {
                    return '';
                }
            };
            const hasRejectedPageMarkerDomName = (element) => {
                const markerId = readDomMarkerAttribute(element, 'id').toLowerCase().trim();
                const markerClass = readDomMarkerAttribute(element, 'class').toLowerCase();
                const markerName = `${
                    markerId
                } ${
                    markerClass
                }`;
                const hasGenericErrorName = markerId === 'error'
                    || markerClass.split(/\s+/).some((name) => name === 'error');
                return visiblePageMarkerDomNameFragments.some(
                    (fragment) => markerName.includes(fragment))
                    || hasGenericErrorName
                    || ((markerName.includes('login') || markerName.includes('error'))
                        && isRejectedInterstitialText(readElementText(element)));
            };
            const isElementHiddenByAttribute = (element) => {
                try {
                    return !!element.hidden
                        || (element.getAttribute
                            && element.getAttribute('aria-hidden') === 'true');
                }
                catch {
                    return false;
                }
            };
            const isElementVisible = (element) => {
                for (let current = element; current; current = current.parentElement) {
                    if (isElementHiddenByAttribute(current)) {
                        return false;
                    }

                    try {
                        if (typeof window !== 'undefined' && window.getComputedStyle) {
                            const style = window.getComputedStyle(current);
                            if (style
                                && (style.display === 'none'
                                    || style.visibility === 'hidden'
                                    || style.visibility === 'collapse'
                                    || style.opacity === '0')) {
                                return false;
                            }
                        }
                    }
                    catch {
                    }
                }

                let hasVisibilitySignal = false;
                try {
                    if (element.getClientRects) {
                        hasVisibilitySignal = true;
                        if (element.getClientRects().length > 0) {
                            return true;
                        }
                    }
                }
                catch {
                }

                try {
                    if ('offsetParent' in element) {
                        hasVisibilitySignal = true;
                        if (element.offsetParent !== null) {
                            return true;
                        }
                    }
                }
                catch {
                }

                return !hasVisibilitySignal;
            };
            const hasRejectedPageMarker = () => {
                if (isRejectedInterstitialText(bodyText)) {
                    return true;
                }

                try {
                    if (document.querySelectorAll) {
                        for (const element of document.querySelectorAll('[id], [class]')) {
                            if (isElementVisible(element)
                                && hasRejectedPageMarkerDomName(element)) {
                                return true;
                            }
                        }
                    }
                }
                catch {
                }

                return false;
            };
            const hasRejectedContentMarker = (elements) => {
                for (const element of elements) {
                    if (isRejectedInterstitialText(element.textContent)
                        || hasRejectedMarkerDom(element)) {
                        return true;
                    }
                }

                return false;
            };
            const recognizedContentContainerSelectors = [
                '.read-content',
                '.chapter-content',
                '#j_chapterContent',
            ];
            const findRecognizedContentContainer = (element) => {
                for (let current = element; current; current = current.parentElement) {
                    if (recognizedContentContainerSelectors.some(
                        (selector) => matchesSelector(current, selector))) {
                        return current;
                    }
                }

                return null;
            };
            const getSpanContentMarkerContexts = (spans) => {
                const contexts = [];
                const seen = new Set();
                const addContext = (element) => {
                    if (element && !seen.has(element)) {
                        seen.add(element);
                        contexts.push(element);
                    }
                };

                for (const span of spans) {
                    addContext(findRecognizedContentContainer(span) ?? span.parentElement);
                }

                return contexts.length > 0 ? contexts : spans;
            };

            const pushParagraphs = (elements, useClone) => {
                for (const element of elements) {
                    const source = useClone ? element.cloneNode(true) : element;
                    if (useClone) {
                        source.querySelectorAll('.review, .review-count, .review-icon').forEach(
                            (node) => node.remove());
                    }

                    const text = source.textContent ? source.textContent.trim() : '';
                    if (text) {
                        paragraphs.push(text);
                    }
                }
            };

            const rejectedByPageMarker = hasRejectedPageMarker();
            let rejectedByContentMarker = false;
            if (!rejectedByPageMarker) {
                const contentSpans = document.querySelectorAll('span.content-text');
                if (contentSpans.length > 0) {
                    if (hasRejectedContentMarker(getSpanContentMarkerContexts(contentSpans))) {
                        rejectedByContentMarker = true;
                    }
                    else {
                        contentSelector = 'span.content-text';
                        pushParagraphs(contentSpans, false);
                    }
                }
                else {
                    const fallbackSelectors = [
                        { container: '.read-content', paragraphs: '.read-content p' },
                        { container: '.chapter-content', paragraphs: '.chapter-content p' },
                        { container: '#j_chapterContent', paragraphs: '#j_chapterContent p' },
                    ];

                    for (const selector of fallbackSelectors) {
                        const nodes = document.querySelectorAll(selector.paragraphs);
                        if (nodes.length > 0) {
                            const containers = document.querySelectorAll(selector.container);
                            if (hasRejectedContentMarker(
                                containers.length > 0 ? containers : nodes)) {
                                rejectedByContentMarker = true;
                            }
                            else {
                                contentSelector = selector.paragraphs;
                                pushParagraphs(nodes, true);
                            }

                            break;
                        }
                    }
                }
            }

            if (paragraphs.some(isRejectedInterstitialText)) {
                rejectedByContentMarker = true;
                contentSelector = null;
                paragraphs.length = 0;
            }

            return JSON.stringify({
                pageUrl,
                contentSelector,
                isPreview,
                rejected: rejectedByPageMarker || rejectedByContentMarker,
                paragraphs,
            });
        }
        """;
}
