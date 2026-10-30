using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Khylo.Providers.Books.OpenPackagingFormat
{
    /// <summary>
    /// Utilities for EPUB files.
    /// </summary>
    public static class EpubUtils
    {
        /// <summary>
        /// Attempt to read content from ZIP archive.
        /// </summary>
        /// <param name="epub">The ZIP archive.</param>
        /// <returns>The content file path.</returns>
        public static string? ReadContentFilePath(ZipArchive epub)
        {
            // Paths inside an EPUB (a zip) always use "/", on every operating system
            var container = epub.GetEntry("META-INF/container.xml");
            if (container == null)
            {
                return null;
            }

            using var containerStream = container.Open();

            XNamespace containerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";
            var containerDocument = XDocument.Load(containerStream);
            var element = containerDocument.Descendants(containerNamespace + "rootfile").FirstOrDefault();

            return element?.Attribute("full-path")?.Value;
        }

        /// <summary>
        /// Joins paths inside an EPUB. Entries in a zip always use "/", so <see cref="Path.Combine(string, string)"/>
        /// (which uses "\" on Windows) can't be used for them.
        /// </summary>
        /// <param name="directory">The folder inside the EPUB, or empty for its root.</param>
        /// <param name="path">The path relative to that folder.</param>
        /// <returns>The joined path, with "/" throughout.</returns>
        public static string JoinZipPath(string? directory, string path)
        {
            var joined = string.IsNullOrEmpty(directory) ? path : directory.TrimEnd('/', '\\') + "/" + path;
            return joined.Replace('\\', '/');
        }
    }
}
