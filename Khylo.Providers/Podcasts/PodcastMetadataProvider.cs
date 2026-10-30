#nullable disable

#pragma warning disable CS1591

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.IO;
using Microsoft.Extensions.Logging;

namespace Khylo.Providers.Podcasts
{
    /// <summary>
    /// Metadata provider for syndicated podcasts.
    /// Ingests podcast channel metadata from local RSS XML feed snapshots and feed descriptor files.
    /// </summary>
    public class PodcastMetadataProvider : ILocalMetadataProvider<Podcast>, IHasItemChangeMonitor
    {
        private static readonly XNamespace _itunesNs = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        private readonly ILogger<PodcastMetadataProvider> _logger;

        public PodcastMetadataProvider(ILogger<PodcastMetadataProvider> logger)
        {
            _logger = logger;
        }

        public string Name => "Podcast Feed Metadata";

        public async Task<MetadataResult<Podcast>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(info.Path) || !Directory.Exists(info.Path))
            {
                return new MetadataResult<Podcast> { HasMetadata = false };
            }

            var podcast = new Podcast
            {
                Path = info.Path,
                Name = Path.GetFileName(info.Path)
            };

            var hasMetadata = false;

            // Check for feed descriptor (feed.url or feed.txt)
            var feedUrlPath = Path.Combine(info.Path, "feed.url");
            if (!File.Exists(feedUrlPath))
            {
                feedUrlPath = Path.Combine(info.Path, "feed.txt");
            }

            if (File.Exists(feedUrlPath))
            {
                try
                {
                    var url = (await File.ReadAllTextAsync(feedUrlPath, cancellationToken).ConfigureAwait(false)).Trim();
                    if (Uri.TryCreate(url, UriKind.Absolute, out _))
                    {
                        podcast.FeedUrl = url;
                        hasMetadata = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error reading feed URL file for {Path}", info.Path);
                }
            }

            // Check for local RSS/XML feed cache (feed.xml or podcast.xml)
            var feedXmlPath = Path.Combine(info.Path, "feed.xml");
            if (!File.Exists(feedXmlPath))
            {
                feedXmlPath = Path.Combine(info.Path, "podcast.xml");
            }

            if (File.Exists(feedXmlPath))
            {
                try
                {
                    var doc = XDocument.Load(feedXmlPath);
                    var channel = doc.Descendants("channel").FirstOrDefault();
                    if (channel is not null)
                    {
                        var title = (string)channel.Element("title");
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            podcast.Name = title.Trim();
                            hasMetadata = true;
                        }

                        var description = (string)channel.Element("description") ??
                                          (string)channel.Element(_itunesNs + "summary");
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            podcast.Overview = description.Trim();
                            hasMetadata = true;
                        }

                        var author = (string)channel.Element(_itunesNs + "author") ??
                                     (string)channel.Element("managingEditor");
                        if (!string.IsNullOrWhiteSpace(author))
                        {
                            podcast.Author = author.Trim();
                            hasMetadata = true;
                        }

                        var link = (string)channel.Element("link");
                        if (!string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out _))
                        {
                            podcast.WebsiteUrl = link.Trim();
                            hasMetadata = true;
                        }

                        // Genres / Categories
                        var categories = channel.Elements(_itunesNs + "category")
                            .Select(c => (string)c.Attribute("text"))
                            .Where(c => !string.IsNullOrWhiteSpace(c));

                        foreach (var cat in categories)
                        {
                            podcast.AddGenre(cat.Trim());
                        }

                        podcast.LastSyncDate = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing RSS feed XML for {Path}", info.Path);
                }
            }

            return new MetadataResult<Podcast>
            {
                Item = podcast,
                HasMetadata = hasMetadata
            };
        }

        public bool HasChanged(BaseItem item, IDirectoryService directoryService)
        {
            if (item.IsFileProtocol)
            {
                var feedXmlPath = Path.Combine(item.Path, "feed.xml");
                if (File.Exists(feedXmlPath))
                {
                    var file = directoryService.GetFile(feedXmlPath);
                    return file is not null && item.HasChanged(file.LastWriteTimeUtc);
                }
            }

            return false;
        }
    }
}
