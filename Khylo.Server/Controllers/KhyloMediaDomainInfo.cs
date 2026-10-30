using Khylo.Model.Entities;

namespace Khylo.Server.Controllers;

/// <summary>Describes a Khylo media domain for UI presentation.</summary>
public class KhyloMediaDomainInfo
{
    /// <summary>Gets or sets the id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the icon.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Gets or sets the collection type.</summary>
    public CollectionTypeOptions CollectionType { get; set; }
}
