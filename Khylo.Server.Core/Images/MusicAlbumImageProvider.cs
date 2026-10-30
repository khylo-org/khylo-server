#pragma warning disable CS1591

using System.Collections.Generic;
using System.Linq;
using Khylo.Common.Configuration;
using Khylo.Controller.Drawing;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.IO;

namespace Khylo.Server.Core.Images
{
    public class MusicAlbumImageProvider : BaseFolderImageProvider<MusicAlbum>
    {
        public MusicAlbumImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor, ILibraryManager libraryManager)
            : base(fileSystem, providerManager, applicationPaths, imageProcessor, libraryManager)
        {
        }

        protected override IReadOnlyList<BaseItem> GetItemsWithImages(BaseItem item)
        {
            var items = base.GetItemsWithImages(item);

            // Ignore any folders because they can have generated collages
            return items.Where(i => i is not Folder).ToList();
        }
    }
}
