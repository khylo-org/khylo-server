#nullable disable
#pragma warning disable CS1591

using System;
using Khylo.Controller.Entities;
using Khylo.Controller.Sorting;
using Khylo.Data.Enums;
using Khylo.Model.Querying;

namespace Khylo.Server.Core.Sorting
{
    public class DateLastMediaAddedComparer : IBaseItemComparer
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public ItemSortBy Type => ItemSortBy.DateLastContentAdded;

        /// <summary>
        /// Compares the specified x.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <returns>System.Int32.</returns>
        public int Compare(BaseItem x, BaseItem y)
        {
            return GetDate(x).CompareTo(GetDate(y));
        }

        /// <summary>
        /// Gets the date.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <returns>DateTime.</returns>
        private static DateTime GetDate(BaseItem x)
        {
            if (x is Folder folder)
            {
                if (folder.DateLastMediaAdded.HasValue)
                {
                    return folder.DateLastMediaAdded.Value;
                }
            }

            return DateTime.MinValue;
        }
    }
}
