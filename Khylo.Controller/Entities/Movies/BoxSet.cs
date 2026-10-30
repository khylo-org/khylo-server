#nullable disable

#pragma warning disable CA1721, CA1819, CS1591

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using Khylo.Controller.Dto;
using Khylo.Controller.Providers;
using Khylo.Data;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Model.Querying;

namespace Khylo.Controller.Entities.Movies
{
    /// <summary>
    /// Class BoxSet.
    /// </summary>
    public class BoxSet : Folder, IHasTrailers, IHasDisplayOrder, IHasLookupInfo<BoxSetInfo>
    {
        public BoxSet()
        {
            DisplayOrder = "PremiereDate";
        }

        [JsonIgnore]
        protected override bool FilterLinkedChildrenPerUser => true;

        [JsonIgnore]
        public override bool SupportsInheritedParentImages => false;

        [JsonIgnore]
        public override bool SupportsPeople => true;

        /// <inheritdoc />
        [JsonIgnore]
        public IReadOnlyList<BaseItem> LocalTrailers => GetExtras([Model.Entities.ExtraType.Trailer]).ToArray();

        /// <summary>
        /// Gets or sets the display order.
        /// </summary>
        /// <value>The display order.</value>
        public string DisplayOrder { get; set; }

        /// <summary>
        /// Gets or sets the user a personal collection belongs to. Only that user sees and edits it.
        /// Empty for shared collections (made automatically or by an administrator for everyone).
        /// </summary>
        public Guid OwnerUserId { get; set; }

        /// <summary>
        /// Gets a value indicating whether this is someone's personal collection.
        /// </summary>
        [JsonIgnore]
        public bool IsPersonal => !OwnerUserId.Equals(Guid.Empty);

        [JsonIgnore]
        private bool IsLegacyBoxSet
        {
            get
            {
                if (string.IsNullOrEmpty(Path))
                {
                    return false;
                }

                if (LinkedChildren.Length > 0)
                {
                    return false;
                }

                return !FileSystem.ContainsSubPath(ConfigurationManager.ApplicationPaths.DataPath, Path);
            }
        }

        [JsonIgnore]
        public override bool IsPreSorted => true;

        public Guid[] LibraryFolderIds { get; set; }

        /// <summary>
        /// Whether a user may rename, add to or remove from this collection: the owner of a personal
        /// collection, or for shared collections an administrator or a user allowed to manage them.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>Whether the user may change the collection.</returns>
        public bool CanEdit(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            return IsPersonal
                ? user.Id.Equals(OwnerUserId)
                : user.HasPermission(PermissionKind.IsAdministrator) || user.HasPermission(PermissionKind.EnableCollectionManagement);
        }

        protected override bool GetBlockUnratedValue(User user)
        {
            return user.GetPreferenceValues<UnratedItem>(PreferenceKind.BlockUnratedItems).Contains(UnratedItem.Movie);
        }

        public override double GetDefaultPrimaryImageAspectRatio()
            => 2.0 / 3;

        public override UnratedItem GetBlockUnratedType()
        {
            return UnratedItem.Movie;
        }

        protected override IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
        {
            if (IsLegacyBoxSet)
            {
                return base.GetNonCachedChildren(directoryService);
            }

            return [];
        }

        protected override IReadOnlyList<BaseItem> LoadChildren()
        {
            if (IsLegacyBoxSet)
            {
                return base.LoadChildren();
            }

            // Save a trip to the database
            return [];
        }

        public override bool IsAuthorizedToDelete(User user, List<Folder> allCollectionFolders)
        {
            // Administrators can also remove someone's personal collection
            return CanEdit(user) || user.HasPermission(PermissionKind.IsAdministrator);
        }

        public override bool IsSaveLocalMetadataEnabled()
        {
            return true;
        }

        private IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User user)
        {
            if (!Enum.TryParse<ItemSortBy>(DisplayOrder, out var sortBy))
            {
                sortBy = ItemSortBy.PremiereDate;
            }

            if (sortBy == ItemSortBy.Default)
            {
                return items;
            }

            return LibraryManager.Sort(items, user, new[] { sortBy }, SortOrder.Ascending);
        }

        public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query)
        {
            var children = base.GetChildren(user, includeLinkedChildren, query);
            return Sort(children, user).ToArray();
        }

        public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, out int totalItemCount, InternalItemsQuery query = null)
        {
            var children = base.GetChildren(user, includeLinkedChildren, out totalItemCount, query);
            return Sort(children, user).ToArray();
        }

        public override IReadOnlyList<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query, out int totalCount)
        {
            var children = base.GetRecursiveChildren(user, query, out totalCount);
            return Sort(children, user).ToArray();
        }

        public BoxSetInfo GetLookupInfo()
        {
            return GetItemLookupInfo<BoxSetInfo>();
        }

        public override bool IsVisible(User user, bool skipAllowedTagsCheck = false)
        {
            if (IsLegacyBoxSet)
            {
                return base.IsVisible(user, skipAllowedTagsCheck);
            }

            // Personal collections are private to the person who made them
            if (IsPersonal && !user.Id.Equals(OwnerUserId))
            {
                return false;
            }

            if (!IsParentalAllowed(user, skipAllowedTagsCheck))
            {
                return false;
            }

            if (LinkedChildren.Length == 0)
            {
                return true;
            }

            List<BaseItem> linkedItems = null;
            var libraryFolderIds = LibraryFolderIds;
            if (libraryFolderIds is null)
            {
                linkedItems = GetLinkedChildren(DtoOptions.StoredColumnsOnly);
                libraryFolderIds = GetLibraryFolderIds(linkedItems);
            }

            if (libraryFolderIds.Length == 0)
            {
                return true;
            }

            var userLibraryFolderIds = GetLibraryFolderIds(user);

            if (!userLibraryFolderIds.Any(i => libraryFolderIds.Contains(i)))
            {
                return false;
            }

            // If user has parental controls, hide the BoxSet when all children are restricted
            if (user.MaxParentalRatingScore.HasValue)
            {
                linkedItems ??= GetLinkedChildren(DtoOptions.StoredColumnsOnly);
                if (linkedItems.Count > 0 && linkedItems.All(child => !child.IsParentalAllowed(user, true)))
                {
                    return false;
                }
            }

            return true;
        }

        public override void MarkPlayed(User user, DateTime? datePlayed, bool resetPosition)
        {
            if (IsLegacyBoxSet)
            {
                base.MarkPlayed(user, datePlayed, resetPosition);
                return;
            }

            foreach (var item in GetLinkedChildren(user))
            {
                item.MarkPlayed(user, datePlayed, resetPosition);
            }
        }

        public override void MarkUnplayed(User user)
        {
            if (IsLegacyBoxSet)
            {
                base.MarkUnplayed(user);
                return;
            }

            foreach (var item in GetLinkedChildren(user))
            {
                item.MarkUnplayed(user);
            }
        }

        public override bool IsVisibleStandalone(User user)
        {
            if (IsLegacyBoxSet)
            {
                return base.IsVisibleStandalone(user);
            }

            return IsVisible(user);
        }

        private Guid[] GetLibraryFolderIds(User user)
        {
            return LibraryManager.GetUserRootFolder().GetChildren(user, true)
                .Select(i => i.Id)
                .ToArray();
        }

        public Guid[] GetLibraryFolderIds()
        {
            return GetLibraryFolderIds(GetLinkedChildren(DtoOptions.StoredColumnsOnly));
        }

        private Guid[] GetLibraryFolderIds(IEnumerable<BaseItem> linkedChildren)
        {
            // Seeded with this box set so a cycle through a nested collection terminates.
            var expandedFolders = new List<Guid> { Id };

            // The user root children are the same for every item.
            var rootChildren = LibraryManager.GetUserRootFolder().Children.OfType<Folder>().ToList();

            return FlattenItems(linkedChildren, expandedFolders)
                .SelectMany(i => LibraryManager.GetCollectionFolders(i, rootChildren))
                .Select(i => i.Id)
                .Distinct()
                .ToArray();
        }

        private IEnumerable<BaseItem> FlattenItems(IEnumerable<BaseItem> items, List<Guid> expandedFolders)
        {
            return items
                .SelectMany(i => FlattenItems(i, expandedFolders));
        }

        private IEnumerable<BaseItem> FlattenItems(BaseItem item, List<Guid> expandedFolders)
        {
            if (item is BoxSet boxset)
            {
                if (!expandedFolders.Contains(item.Id))
                {
                    expandedFolders.Add(item.Id);

                    return FlattenItems(boxset.GetLinkedChildren(DtoOptions.StoredColumnsOnly), expandedFolders);
                }

                return [];
            }

            return [item];
        }
    }
}
