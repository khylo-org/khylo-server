#pragma warning disable CS1591

using Khylo.Common.Configuration;
using Khylo.Controller.Drawing;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.IO;

namespace Khylo.Server.Core.Images
{
    public class PhotoAlbumImageProvider : BaseFolderImageProvider<PhotoAlbum>
    {
        public PhotoAlbumImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor, ILibraryManager libraryManager)
            : base(fileSystem, providerManager, applicationPaths, imageProcessor, libraryManager)
        {
        }
    }
}
