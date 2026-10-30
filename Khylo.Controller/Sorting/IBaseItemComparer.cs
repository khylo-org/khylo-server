using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Data.Enums;

namespace Khylo.Controller.Sorting
{
    /// <summary>
    /// Interface IBaseItemComparer.
    /// </summary>
    public interface IBaseItemComparer : IComparer<BaseItem?>
    {
        /// <summary>
        /// Gets the comparer type.
        /// </summary>
        ItemSortBy Type { get; }
    }
}
