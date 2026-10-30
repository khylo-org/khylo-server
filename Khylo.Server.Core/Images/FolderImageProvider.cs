#pragma warning disable CS1591

using Khylo.Common.Configuration;
using Khylo.Controller.Drawing;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.IO;

namespace Khylo.Server.Core.Images
{
    public class FolderImageProvider : BaseFolderImageProvider<Folder>
    {
        public FolderImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor, ILibraryManager libraryManager)
            : base(fileSystem, providerManager, applicationPaths, imageProcessor, libraryManager)
        {
        }

        protected override bool Supports(BaseItem item)
        {
            if (item is PhotoAlbum || item is MusicAlbum)
            {
                return false;
            }

            if (item is Folder && item.IsTopParent)
            {
                return false;
            }

            return true;
        }
    }
}
