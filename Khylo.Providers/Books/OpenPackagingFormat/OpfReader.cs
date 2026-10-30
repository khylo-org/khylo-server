using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;
using Khylo.Model.Entities;
using Khylo.Model.Net;
using Microsoft.Extensions.Logging;

namespace Khylo.Providers.Books.OpenPackagingFormat
{
    /// <summary>
    /// Methods used to pull metadata and other information from Open Packaging Format in XML objects.
    /// </summary>
    /// <typeparam name="TCategoryName">The type of category.</typeparam>
    public partial class OpfReader<TCategoryName>
    {
        private const string DcNamespace = @"http://purl.org/dc/elements/1.1/";
        private const string OpfNamespace = @"http://www.idpf.org/2007/opf";

        private readonly XmlNamespaceManager _namespaceManager;
        private readonly XmlDocument _document;

        private readonly ILogger<TCategoryName> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpfReader{TCategoryName}"/> class.
        /// </summary>
        /// <param name="document">The XML document to parse.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
        public OpfReader(XmlDocument document, ILogger<TCategoryName> logger)
        {
            _document = document;
            _logger = logger;
            _namespaceManager = new XmlNamespaceManager(_document.NameTable);

            _namespaceManager.AddNamespace("dc", DcNamespace);
            _namespaceManager.AddNamespace("opf", OpfNamespace);
        }

        [GeneratedRegex(@"(?<=\p{L})\.(?!\s|$)")]
        private static partial Regex InitialsRegex();

        /// <summary>
        /// Checks for the existence of a cover image.
        /// </summary>
        /// <param name="opfRootDirectory">The root directory in which the OPF file is located.</param>
        /// <returns>Returns the found cover and its type or null.</returns>
        public (string MimeType, string Path)? ReadCoverPath(string opfRootDirectory)
        {
            var coverImage = ReadEpubCoverInto(opfRootDirectory, "//opf:item[@properties='cover-image']");
            if (coverImage is not null)
            {
                return coverImage;
            }

            var coverId = ReadEpubCoverInto(opfRootDirectory, "//opf:item[@id='cover' and @media-type='image/*']");
            if (coverId is not null)
            {
                return coverId;
            }

            var coverImageId = ReadEpubCoverInto(opfRootDirectory, "//opf:item[@id='*cover-image']");
            if (coverImageId is not null)
            {
                return coverImageId;
            }

            var metaCoverImage = _document.SelectSingleNode("//opf:meta[@name='cover']", _namespaceManager);
            var content = metaCoverImage?.Attributes?["content"]?.Value;
            if (string.IsNullOrEmpty(content) || metaCoverImage is null)
            {
                return null;
            }

            var coverPath = "Images/" + content;
            var coverFileManifest = _document.SelectSingleNode($"//opf:item[@href='{coverPath}']", _namespaceManager);
            var mediaType = coverFileManifest?.Attributes?["media-type"]?.Value;
            if (coverFileManifest?.Attributes is not null && !string.IsNullOrEmpty(mediaType) && IsValidImage(mediaType))
            {
                return (mediaType, EpubUtils.JoinZipPath(opfRootDirectory, coverPath));
            }

            var coverFileIdManifest = _document.SelectSingleNode($"//opf:item[@id='{content}']", _namespaceManager);
            if (coverFileIdManifest is not null)
            {
                return ReadManifestItem(coverFileIdManifest, opfRootDirectory);
            }

            return null;
        }

        /// <summary>
        /// Read all supported OPF data from the file.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The metadata result to update.</returns>
        public MetadataResult<Book> ReadOpfData(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var book = CreateBookFromOpf();
            var result = new MetadataResult<Book> { Item = book, HasMetadata = true };

            FindAuthors(result);
            ReadStringInto("//dc:language", language => result.ResultLanguage = language);

            return result;
        }

        private Book CreateBookFromOpf()
        {
            var book = new Book
            {
                Name = Khylo.Providers.Books.OpenLibrary.BookTitle.Tidy(FindMainTitle()),
                ForcedSortName = FindSortTitle(),
            };

            ReadStringInto("//dc:description", summary => book.Overview = summary);
            ReadStringInto("//dc:publisher", publisher => book.AddStudio(publisher));
            ReadStringInto("//dc:identifier[@opf:scheme='AMAZON']", amazon => book.SetProviderId("Amazon", amazon));
            ReadStringInto("//dc:identifier[@opf:scheme='GOOGLE']", google => book.SetProviderId("GoogleBooks", google));
            ReadStringInto("//dc:identifier[@opf:scheme='ISBN']", isbn => book.SetProviderId("ISBN", isbn));

            ReadStringInto("//dc:date", date =>
            {
                if (DateTime.TryParse(date, CultureInfo.InvariantCulture, out var dateValue))
                {
                    book.PremiereDate = dateValue.Date;
                    book.ProductionYear = dateValue.Date.Year;
                }
            });

            var genreNodes = _document.SelectNodes("//dc:subject", _namespaceManager);

            if (genreNodes?.Count > 0)
            {
                var subjects = genreNodes.Cast<XmlNode>().Select(node => node.InnerText.Trim()).Where(text => text.Length > 0).ToList();

                // Library-catalog subjects ("Private investigators -- England -- Fiction", as Project Gutenberg
                // and many publishers use) are turned into everyday genres
                if (subjects.Exists(s => s.Contains(" -- ", StringComparison.Ordinal)))
                {
                    foreach (var genre in Khylo.Providers.Books.OpenLibrary.OpenLibrarySource.PickGenres(subjects))
                    {
                        book.AddGenre(genre);
                    }
                }
                else
                {
                    foreach (var subject in subjects)
                    {
                        // specification has no rules about content and some books combine every genre into a single element
                        foreach (var item in subject.Split(["/", "&", ",", ";", " - "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            book.AddGenre(item);
                        }
                    }
                }
            }

            ReadInt32AttributeInto("//opf:meta[@name='calibre:series_index']", index => book.IndexNumber = index);
            ReadInt32AttributeInto("//opf:meta[@name='calibre:rating']", rating => book.CommunityRating = rating);
            ReadInt32AttributeInto("//opf:meta[@name='calibre:page_count']", pages => book.PageCount = pages);
            ReadInt32AttributeInto("//opf:meta[@name='page_count']", pages => book.PageCount = pages);
            ReadStringInto("//opf:meta[@property='schema:numberOfPages']", pages =>
            {
                if (int.TryParse(pages, CultureInfo.InvariantCulture, out var pageCount))
                {
                    book.PageCount = pageCount;
                }
            });

            var seriesNameNode = _document.SelectSingleNode("//opf:meta[@name='calibre:series']", _namespaceManager);

            if (!string.IsNullOrEmpty(seriesNameNode?.Attributes?["content"]?.Value))
            {
                try
                {
                    book.SeriesName = seriesNameNode.Attributes["content"]?.Value;
                }
                catch (Exception)
                {
                    _logger.LogError("error parsing Calibre series name");
                }
            }

            return book;
        }

        private string FindMainTitle()
        {
            var title = string.Empty;
            var titleTypes = _document.SelectNodes("//opf:meta[@property='title-type']", _namespaceManager);

            if (titleTypes is not null && titleTypes.Count > 0)
            {
                foreach (XmlElement titleNode in titleTypes)
                {
                    string refines = titleNode.GetAttribute("refines").TrimStart('#');
                    string titleType = titleNode.InnerText;

                    var titleElement = _document.SelectSingleNode($"//dc:title[@id='{refines}']", _namespaceManager);
                    if (titleElement is not null && string.Equals(titleType, "main", StringComparison.OrdinalIgnoreCase))
                    {
                        title = titleElement.InnerText;
                    }
                }
            }

            // fallback in case there is no main title definition
            if (string.IsNullOrEmpty(title))
            {
                ReadStringInto("//dc:title", titleString => title = titleString);
            }

            return title;
        }

        private string? FindSortTitle()
        {
            var titleTypes = _document.SelectNodes("//opf:meta[@property='file-as']", _namespaceManager);

            if (titleTypes is not null && titleTypes.Count > 0)
            {
                foreach (XmlElement titleNode in titleTypes)
                {
                    string refines = titleNode.GetAttribute("refines").TrimStart('#');
                    string sortTitle = titleNode.InnerText;

                    var titleElement = _document.SelectSingleNode($"//dc:title[@id='{refines}']", _namespaceManager);
                    if (titleElement is not null)
                    {
                        return sortTitle;
                    }
                }
            }

            // search for OPF 2.0 style title_sort node
            var resultElement = _document.SelectSingleNode("//opf:meta[@name='calibre:title_sort']", _namespaceManager);
            var titleSort = resultElement?.Attributes?["content"]?.Value;

            return titleSort;
        }

        private void FindAuthors(MetadataResult<Book> book)
        {
            var resultElement = _document.SelectNodes("//dc:creator", _namespaceManager);

            if (resultElement != null && resultElement.Count > 0)
            {
                foreach (XmlElement creator in resultElement)
                {
                    var role = creator.GetAttribute("opf:role");
                    var normalizedCreators = creator.InnerText
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(fullName =>
                        {
                            if (fullName.Split(',', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var lastName, var firstName])
                            {
                                fullName = $"{firstName} {lastName}";
                            }

                            return InitialsRegex().Replace(fullName, ". ");
                        });

                    foreach (var fullName in normalizedCreators)
                    {
                        book.AddPerson(new PersonInfo { Name = fullName, Type = GetRole(role) });
                    }
                }
            }
        }

        private PersonKind GetRole(string? role)
        {
            switch (role)
            {
                case "arr":
                    return PersonKind.Arranger;
                case "art":
                    return PersonKind.Artist;
                case "aut":
                case "aqt":
                case "aft":
                case "aui":
                default:
                    return PersonKind.Author;
                case "edt":
                    return PersonKind.Editor;
                case "ill":
                    return PersonKind.Illustrator;
                case "lyr":
                    return PersonKind.Lyricist;
                case "mus":
                    return PersonKind.AlbumArtist;
                case "nrt":
                    return PersonKind.Narrator;
                case "oth":
                    return PersonKind.Unknown;
                case "trl":
                    return PersonKind.Translator;
            }
        }

        private void ReadStringInto(string xmlPath, Action<string> commitResult)
        {
            var resultElement = _document.SelectSingleNode(xmlPath, _namespaceManager);
            if (resultElement is not null && !string.IsNullOrWhiteSpace(resultElement.InnerText))
            {
                commitResult(resultElement.InnerText);
            }
        }

        private void ReadInt32AttributeInto(string xmlPath, Action<int> commitResult)
        {
            var resultElement = _document.SelectSingleNode(xmlPath, _namespaceManager);
            var resultValue = resultElement?.Attributes?["content"]?.Value;

            if (!string.IsNullOrEmpty(resultValue))
            {
                try
                {
                    commitResult(Convert.ToInt32(Convert.ToDouble(resultValue, CultureInfo.InvariantCulture)));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "error converting to Int32");
                }
            }
        }

        private (string MimeType, string Path)? ReadEpubCoverInto(string opfRootDirectory, string xmlPath)
        {
            var resultElement = _document.SelectSingleNode(xmlPath, _namespaceManager);

            if (resultElement is not null)
            {
                return ReadManifestItem(resultElement, opfRootDirectory);
            }

            return null;
        }

        private (string MimeType, string Path)? ReadManifestItem(XmlNode manifestNode, string opfRootDirectory)
        {
            var href = manifestNode.Attributes?["href"]?.Value;
            var mediaType = manifestNode.Attributes?["media-type"]?.Value;

            if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(mediaType) || !IsValidImage(mediaType))
            {
                return null;
            }

            var coverPath = EpubUtils.JoinZipPath(opfRootDirectory, href);

            return (MimeType: mediaType, Path: coverPath);
        }

        private static bool IsValidImage(string? mimeType)
        {
            return !string.IsNullOrEmpty(mimeType) && !string.IsNullOrWhiteSpace(MimeTypes.ToExtension(mimeType));
        }
    }
}
