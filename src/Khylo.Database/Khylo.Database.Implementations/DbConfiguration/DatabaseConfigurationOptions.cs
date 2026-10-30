using System.Collections.Generic;

namespace Khylo.Database.Implementations.DbConfiguration;

/// <summary>
/// Options to configure khylos managed database.
/// </summary>
public class DatabaseConfigurationOptions
{
    /// <summary>
    /// Gets or Sets the type of database khylo should use.
    /// </summary>
    public required string DatabaseType { get; set; }

    /// <summary>
    /// Gets or sets the options required to use a custom database provider.
    /// </summary>
    public CustomDatabaseOptions? CustomProviderOptions { get; set; }

    /// <summary>
    /// Gets or Sets the kind of locking behavior khylo should perform. Possible options are "NoLock", "Pessimistic", "Optimistic".
    /// Defaults to "NoLock".
    /// </summary>
    public DatabaseLockingBehaviorTypes LockingBehavior { get; set; }
}
