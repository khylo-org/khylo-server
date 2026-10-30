#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using Khylo.Model.Entities;

namespace Khylo.Controller.Collections
{
    public class CollectionCreationOptions : IHasProviderIds
    {
        public CollectionCreationOptions()
        {
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ItemIdList = Array.Empty<string>();
            UserIds = Array.Empty<Guid>();
        }

        public string Name { get; set; }

        public Guid? ParentId { get; set; }

        public bool IsLocked { get; set; }

        public Dictionary<string, string> ProviderIds { get; set; }

        public IReadOnlyList<string> ItemIdList { get; set; }

        public IReadOnlyList<Guid> UserIds { get; set; }

        /// <summary>
        /// Gets or sets the user a personal collection belongs to; empty for a shared collection.
        /// </summary>
        public Guid OwnerUserId { get; set; }
    }
}
