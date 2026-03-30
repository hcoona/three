namespace Hcoona.QidianNovelDownloader.Browser;

internal static class PageScripts
{
    public const string LoginStateJson = """
        () => {
            const signInElement = document.getElementById('sign-in');
            const userNameElement = document.getElementById('user-name');
            const isSignInHidden = signInElement ? signInElement.classList.contains('hidden') : true;
            const userName = userNameElement ? userNameElement.textContent.trim() : null;
            const isLoggedIn = (!!signInElement && !isSignInHidden)
                || (!!userName && userName !== '用户名');
            return JSON.stringify({
                isLoggedIn,
                userName,
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
                const seenChapterIds = new Set();
                let sibling = heading.nextElementSibling;
                while (sibling && sibling.tagName !== 'H3') {
                    for (const link of sibling.querySelectorAll('a[href*="/chapter/"]')) {
                        const href = link.getAttribute('href');
                        const chapterTitle = normalizeText(link.textContent);
                        if (!href || !chapterTitle) {
                            continue;
                        }

                        const chapterMatch = href.match(/\/chapter\/\d+\/(\d+)\/?/);
                        const chapterId = chapterMatch ? chapterMatch[1] : null;
                        if (!chapterId || seenChapterIds.has(chapterId)) {
                            continue;
                        }

                        seenChapterIds.add(chapterId);
                        const titleText = normalizeText(link.getAttribute('title')) ?? '';
                        const wordCountMatch = titleText.match(/章节字数[:：]\s*([0-9,]+)/);
                        chapters.push({
                            chapterId,
                            title: chapterTitle,
                            url: href.startsWith('http')
                                ? href
                                : href.startsWith('//')
                                    ? `https:${href}`
                                    : `https://www.qidian.com${href}`,
                            isVip,
                            catalogWordCount: wordCountMatch
                                ? Number.parseInt(wordCountMatch[1].replaceAll(',', ''), 10)
                                : null,
                        });
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
                title,
                author,
                estimatedWordCount,
                volumes,
            });
        }
        """;

    public const string ChapterContentJson = """
        () => {
            const bodyText = document.body ? document.body.innerText : '';
            const isPreview =
                (bodyText.includes('需要订阅后才能阅读') || bodyText.includes('本章为付费章节'))
                && bodyText.includes('VIP');
            const paragraphs = [];

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

            const contentSpans = document.querySelectorAll('span.content-text');
            if (contentSpans.length > 0) {
                pushParagraphs(contentSpans, false);
            }
            else {
                const candidates = [
                    ...document.querySelectorAll('main p'),
                    ...document.querySelectorAll('.read-content p'),
                    ...document.querySelectorAll('.chapter-content p'),
                    ...document.querySelectorAll('#j_chapterContent p'),
                ];

                if (candidates.length > 0) {
                    pushParagraphs(candidates, true);
                }
            }

            return JSON.stringify({
                isPreview,
                paragraphs,
            });
        }
        """;
}
