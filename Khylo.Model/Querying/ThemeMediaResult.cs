using System;
using Khylo.Model.Dto;

namespace Khylo.Model.Querying
{
    /// <summary>
    /// Class ThemeMediaResult.
    /// </summary>
    public class ThemeMediaResult : QueryResult<BaseItemDto>
    {
        /// <summary>
        /// Gets or sets the owner id.
        /// </summary>
        /// <value>The owner id.</value>
        public Guid OwnerId { get; set; }
    }
}
