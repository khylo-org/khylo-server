using System.IO;
using System.IO.Compression;
using Khylo.Providers.Books.OpenLibrary;
using Khylo.Providers.Books.OpenPackagingFormat;
using Khylo.Providers.Podcasts.Online;
using Xunit;

namespace Khylo.Providers.Tests.Books;

public class BookMetadataTests
{
    [Theory]
    [InlineData("Moby Dick; Or, The Whale", "Moby Dick")]
    [InlineData("Jane Eyre: An Autobiography", "Jane Eyre")]
    [InlineData("The Odyssey / Rendered into English prose", "The Odyssey")]
    [InlineData("The war of the worlds", "The War of the Worlds")]
    [InlineData("Frankenstein; or, the modern prometheus", "Frankenstein")]
    [InlineData("Pride and Prejudice", "Pride and Prejudice")]
    public void Tidy_ShowsTheMainTitle(string title, string expected)
        => Assert.Equal(expected, BookTitle.Tidy(title));

    [Theory]
    [InlineData("Frankenstein - Mary Shelley", "Frankenstein", "Mary Shelley")]
    [InlineData("Dracula", "Dracula", null)]
    public void FileNameTitles_SplitIntoTitleAndAuthor(string name, string title, string? author)
    {
        Assert.Equal(title, BookTitle.Clean(name));
        Assert.Equal(author, BookTitle.AuthorFromName(name));
    }

    [Theory]
    [InlineData("Frankenstein; or, the modern prometheus", "Frankenstein")]
    [InlineData("The Call of the Wild", "Call of the wild")]
    public void SameBook_IgnoresSubtitlesCaseAndThe(string a, string b)
        => Assert.True(BookTitle.SameBook(a, b));

    [Fact]
    public void PickGenres_TurnsCatalogSubjectsIntoEverydayGenres()
    {
        var genres = OpenLibrarySource.PickGenres(["Holmes, Sherlock (Fictitious character) -- Fiction", "Private investigators -- England -- Fiction", "Detective and mystery stories"]);
        Assert.Equal(["Mystery"], genres);
    }

    [Fact]
    public void PickGenres_MatchesWholeWordsOnly()
    {
        // "Western Europe" isn't a western, and "warfare" isn't a war story
        Assert.Empty(OpenLibrarySource.PickGenres(["Western Europe", "Naval warfare", "Comic strips"]));
    }

    [Fact]
    public void EpubPaths_AlwaysUseForwardSlashes()
    {
        Assert.Equal("OEBPS/cover.jpg", EpubUtils.JoinZipPath("OEBPS", "cover.jpg"));
        Assert.Equal("OEBPS/images/cover.jpg", EpubUtils.JoinZipPath("OEBPS\\", "images\\cover.jpg"));
        Assert.Equal("cover.jpg", EpubUtils.JoinZipPath(string.Empty, "cover.jpg"));
    }

    [Fact]
    public void ReadContentFilePath_FindsThePackageInsideTheEpub()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("META-INF/container.xml").Open());
            writer.Write("""<?xml version="1.0"?><container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0"><rootfiles><rootfile full-path="OEBPS/content.opf"/></rootfiles></container>""");
        }

        stream.Position = 0;
        using var epub = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal("OEBPS/content.opf", EpubUtils.ReadContentFilePath(epub));
    }

    [Theory]
    [InlineData("Park Postcards", "Park Postcards Podcast", true)]
    [InlineData("NASA's Curious Universe", "NASA's Curious Universe", true)]
    [InlineData("Science", "Science Friday", false)]
    public void PodcastNames_MatchCloseVariants(string a, string b, bool expected)
        => Assert.Equal(expected, PodcastDirectory.SameName(a, b) || PodcastDirectory.CloseName(a, b));
}
