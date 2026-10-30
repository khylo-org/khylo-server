#nullable disable

#pragma warning disable CS1591

using Khylo.Model.Entities;
using Khylo.Model.IO;

namespace Khylo.Controller.Providers
{
    public class LocalImageInfo
    {
        public FileSystemMetadata FileInfo { get; set; }

        public ImageType Type { get; set; }
    }
}
