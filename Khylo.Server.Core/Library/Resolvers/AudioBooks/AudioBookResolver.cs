#nullable disable

#pragma warning disable CS1591

using System;
using System.IO;
using System.Linq;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Resolvers;
using Khylo.Data.Enums;
using Khylo.Extensions;

namespace Khylo.Server.Core.Library.Resolvers.AudioBooks
{
    /// <summary>
    /// Resolves audiobooks (.m4b or audio files in audiobook libraries) into AudioBook items.
    /// </summary>
    public class AudioBookResolver : ItemResolver<AudioBook>
    {
        private readonly string[] _audioExtensions = { ".m4b", ".m4a", ".mp3", ".flac", ".aac", ".ogg", ".wma", ".opus" };

        public override ResolverPriority Priority => ResolverPriority.Fourth;

        protected override AudioBook Resolve(ItemResolveArgs args)
        {
            if (args.IsDirectory)
            {
                return null;
            }

            var extension = Path.GetExtension(args.Path.AsSpan());
            if (!_audioExtensions.Contains(extension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var isM4b = extension.Equals(".m4b", StringComparison.OrdinalIgnoreCase);
            var collectionType = args.GetCollectionType();
            var isAudioBookLibrary = collectionType == CollectionType.audiobooks;

            // Resolve as AudioBook if it's an .m4b file or located in an audiobook library
            if (isM4b || isAudioBookLibrary)
            {
                var book = new AudioBook
                {
                    Path = args.Path,
                    Name = Path.GetFileNameWithoutExtension(args.Path),
                    IsInMixedFolder = true
                };

                if (args.Parent is not null && !args.Parent.IsRoot)
                {
                    book.Album = args.Parent.Name;
                    book.SeriesName = args.Parent.Name;
                }

                return book;
            }

            return null;
        }
    }
}
