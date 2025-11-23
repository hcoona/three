from playwright.sync_api import sync_playwright


def main() -> None:
    with sync_playwright() as p:
        browser = p.chromium.connect_over_cdp("http://localhost:9222")
        context = browser.contexts[0]

        for page in context.pages:
            print(f"Title: {page.title()}")
            print(f"URL: {page.url}")
            print(f"Content: {page.content()[:200]}...")
            print()

        # TODO(shuaizhang): Implement summarization logic here
        pass


if __name__ == "__main__":
    main()
