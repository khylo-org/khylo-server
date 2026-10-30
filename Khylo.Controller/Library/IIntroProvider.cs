#nullable disable

using System.Collections.Generic;
using System.Threading.Tasks;
using Khylo.Controller.Entities;
using Khylo.Database.Implementations.Entities;

namespace Khylo.Controller.Library
{
    /// <summary>
    /// Class BaseIntroProvider.
    /// </summary>
    public interface IIntroProvider
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        string Name { get; }

        /// <summary>
        /// Gets the intros.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="user">The user.</param>
        /// <returns>IEnumerable{System.String}.</returns>
        Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user);
    }
}
