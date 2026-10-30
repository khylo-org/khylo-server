#pragma warning disable CS1591

using System;
using Khylo.Controller.Entities;
using Khylo.Controller.LiveTv;
using Khylo.Controller.Sorting;
using Khylo.Data.Enums;

namespace Khylo.Server.Core.Sorting
{
    public class StartDateComparer : IBaseItemComparer
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public ItemSortBy Type => ItemSortBy.StartDate;

        /// <summary>
        /// Compares the specified x.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <returns>System.Int32.</returns>
        public int Compare(BaseItem? x, BaseItem? y)
        {
            return GetDate(x).CompareTo(GetDate(y));
        }

        /// <summary>
        /// Gets the date.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <returns>DateTime.</returns>
        private static DateTime GetDate(BaseItem? x)
        {
            if (x is LiveTvProgram hasStartDate)
            {
                return hasStartDate.StartDate;
            }

            return DateTime.MinValue;
        }
    }
}
