using Khylo.Controller.Entities;
using Khylo.Controller.Sorting;
using Khylo.Data.Enums;
using Khylo.Model.Querying;

namespace Khylo.Server.Core.Sorting
{
    /// <summary>
    /// Class CriticRatingComparer.
    /// </summary>
    public class CriticRatingComparer : IBaseItemComparer
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public ItemSortBy Type => ItemSortBy.CriticRating;

        /// <summary>
        /// Compares the specified x.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <returns>System.Int32.</returns>
        public int Compare(BaseItem? x, BaseItem? y)
        {
            return GetValue(x).CompareTo(GetValue(y));
        }

        private static float GetValue(BaseItem? x)
        {
            return x?.CriticRating ?? 0;
        }
    }
}
