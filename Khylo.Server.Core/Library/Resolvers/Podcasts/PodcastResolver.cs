#nullable disable

#pragma warning disable CS1591

using System;
using System.IO;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Resolvers;
using Khylo.Data.Enums;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library.Resolvers.Podcasts
{
    /// <summary>
    /// Resolves podcast folders representing channels/feeds into Podcast entities.
    /// </summary>
    public class PodcastResolver : GenericFolderResolver<Podcast>
    {
        private readonly ILogger<PodcastResolver> _logger;

        public PodcastResolver(ILogger<PodcastResolver> logger)
        {
            _logger = logger;
        }

        public override ResolverPriority Priority => ResolverPriority.Second;

        protected override Podcast Resolve(ItemResolveArgs args)
        {
            if (!args.IsDirectory)
            {
                return null;
            }

            if (args.HasParent<Podcast>())
            {
                return null;
            }

            if (args.Parent is not null && args.Parent.IsRoot)
            {
                return null;
            }

            var collectionType = args.GetCollectionType();

            // Only resolve podcasts in podcasts collection
            if (collectionType == CollectionType.podcasts)
            {
                var folderName = Path.GetFileName(args.Path);
                var podcast = new Podcast
                {
                    Path = args.Path,
                    Name = folderName
                };

                // Check for optional feed descriptor files
                var feedUrlFile = Path.Combine(args.Path, "feed.url");
                if (File.Exists(feedUrlFile))
                {
                    try
                    {
                        var feedContent = File.ReadAllText(feedUrlFile).Trim();
                        if (Uri.TryCreate(feedContent, UriKind.Absolute, out _))
                        {
                            podcast.FeedUrl = feedContent;
                        }
                    }
                    catch
                    {
                        // Ignore file read exceptions
                    }
                }

                return podcast;
            }

            return null;
        }
    }
}
