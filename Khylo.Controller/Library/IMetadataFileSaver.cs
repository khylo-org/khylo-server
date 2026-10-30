#pragma warning disable CS1591

using Khylo.Controller.Entities;

namespace Khylo.Controller.Library
{
    public interface IMetadataFileSaver : IMetadataSaver
    {
        /// <summary>
        /// Gets the save path.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>System.String.</returns>
        string GetSavePath(BaseItem item);
    }
}
