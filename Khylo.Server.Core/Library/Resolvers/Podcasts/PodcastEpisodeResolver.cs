#nullable disable

#pragma warning disable CS1591

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Resolvers;
using Khylo.Data.Enums;
using Khylo.Extensions;

namespace Khylo.Server.Core.Library.Resolvers.Podcasts
{
    /// <summary>
    /// Resolves audio episodes of syndicated podcasts into PodcastEpisode items.
    /// </summary>
    public partial class PodcastEpisodeResolver : ItemResolver<PodcastEpisode>
    {
        private static readonly string[] _audioExtensions = { ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".wav" };

        public override ResolverPriority Priority => ResolverPriority.First;

        [GeneratedRegex(@"(?:ep(?:isode)?|[#])\s*(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex EpisodeRegex();

        [GeneratedRegex(@"^(\d+)\b")]
        private static partial Regex LeadingNumberRegex();

        protected override PodcastEpisode Resolve(ItemResolveArgs args)
        {
            if (args.IsDirectory)
            {
                return null;
            }

            var collectionType = args.GetCollectionType();

            // Process items in podcasts collection or with a Podcast parent
            if (collectionType != CollectionType.podcasts && !args.HasParent<Podcast>())
            {
                return null;
            }

            var extension = Path.GetExtension(args.Path.AsSpan());
            if (!_audioExtensions.Contains(extension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(args.Path);
            int? episodeNumber = null;

            var match = EpisodeRegex().Match(fileName);
            if (match.Success && int.TryParse(match.Groups[1].ValueSpan, out var ep))
            {
                episodeNumber = ep;
            }
            else
            {
                var leadingMatch = LeadingNumberRegex().Match(fileName);
                if (leadingMatch.Success && int.TryParse(leadingMatch.Groups[1].ValueSpan, out var num))
                {
                    episodeNumber = num;
                }
            }

            return new PodcastEpisode
            {
                Path = args.Path,
                Name = fileName,
                EpisodeNumber = episodeNumber,
                IsInMixedFolder = true
            };
        }
    }
}
