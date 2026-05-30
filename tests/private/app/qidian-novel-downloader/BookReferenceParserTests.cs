using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class BookReferenceParserTests
{
    [Fact]
    public void ParseAcceptsNumericBookId()
    {
        BookReference reference = BookReferenceParser.Parse("1045928363");

        Assert.Equal("1045928363", reference.BookId);
        Assert.Equal("1045928363", reference.RawValue);
    }

    [Fact]
    public void ParseAcceptsCanonicalBookUrl()
    {
        BookReference reference = BookReferenceParser.Parse(
            "https://www.qidian.com/book/1045928363/");

        Assert.Equal("1045928363", reference.BookId);
    }

    [Fact]
    public void ParseRejectsUnsupportedReference()
    {
        Assert.Throws<CliInputException>(
            () => BookReferenceParser.Parse("https://book.qidian.com/info/1045928363"));
    }

    [Theory]
    [InlineData("１０４５９２８３６３")]
    [InlineData("https://www.qidian.com/book/１０４５９２８３６３/")]
    public void ParseRejectsNonAsciiBookIds(string rawValue)
    {
        Assert.Throws<CliInputException>(() => BookReferenceParser.Parse(rawValue));
    }
}
