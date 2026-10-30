#pragma warning disable CS1591

using Khylo.Controller.Entities;

namespace Khylo.Controller.Providers
{
    /// <summary>
    /// Marker interface.
    /// </summary>
    public interface IMetadataProvider
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        string Name { get; }
    }

    public interface IMetadataProvider<TItemType> : IMetadataProvider
           where TItemType : BaseItem
    {
    }
}
