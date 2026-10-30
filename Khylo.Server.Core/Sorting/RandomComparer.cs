using System;
using Khylo.Controller.Entities;
using Khylo.Controller.Sorting;
using Khylo.Data.Enums;
using Khylo.Model.Querying;

namespace Khylo.Server.Core.Sorting
{
    /// <summary>
    /// Class RandomComparer.
    /// </summary>
    public class RandomComparer : IBaseItemComparer
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public ItemSortBy Type => ItemSortBy.Random;

        /// <summary>
        /// Compares the specified x.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <returns>System.Int32.</returns>
        public int Compare(BaseItem? x, BaseItem? y)
        {
            return Guid.NewGuid().CompareTo(Guid.NewGuid());
        }
    }
}
