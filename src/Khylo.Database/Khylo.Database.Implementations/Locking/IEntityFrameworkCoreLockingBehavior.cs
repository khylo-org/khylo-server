using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Khylo.Database.Implementations.Locking;

/// <summary>
/// Defines a khylo locking behavior that can be configured.
/// </summary>
public interface IEntityFrameworkCoreLockingBehavior
{
    /// <summary>
    /// Provides access to the builder to setup any connection related locking behavior.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    void Initialise(DbContextOptionsBuilder optionsBuilder);

    /// <summary>
    /// Will be invoked when changes should be saved in the current locking behavior.
    /// </summary>
    /// <param name="context">The database context invoking the action.</param>
    /// <param name="saveChanges">Callback for performing the actual save changes.</param>
    void OnSaveChanges(KhyloDbContext context, Action saveChanges);

    /// <summary>
    /// Will be invoked when changes should be saved in the current locking behavior.
    /// </summary>
    /// <param name="context">The database context invoking the action.</param>
    /// <param name="saveChanges">Callback for performing the actual save changes.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task OnSaveChangesAsync(KhyloDbContext context, Func<Task> saveChanges);
}
