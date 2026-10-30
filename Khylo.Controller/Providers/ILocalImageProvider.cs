#pragma warning disable CS1591

using System.Collections.Generic;
using Khylo.Controller.Entities;

namespace Khylo.Controller.Providers
{
    /// <summary>
    /// This is just a marker interface.
    /// </summary>
    public interface ILocalImageProvider : IImageProvider
    {
        IEnumerable<LocalImageInfo> GetImages(BaseItem item, IDirectoryService directoryService);
    }
}
