using System;

namespace Khylo.Controller.Entities
{
    /// <summary>
    /// Interface for items that have a start date.
    /// </summary>
    public interface IHasStartDate
    {
        /// <summary>
        /// Gets or sets the start date.
        /// </summary>
        DateTime StartDate { get; set; }
    }
}
