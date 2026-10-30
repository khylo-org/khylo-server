using System;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Library;
using Khylo.Database.Implementations;

namespace Khylo.Server.Core.Data;

/// <summary>
/// Refreshes the database statistics after every library scan.
/// </summary>
/// <remarks>
/// The scheduled optimization skips itself while a scan runs, so without this the first scan of a new server
/// leaves every query planned for an empty library until the next scheduled run.
/// </remarks>
public class RefreshDatabaseStatisticsPostScanTask : ILibraryPostScanTask
{
    private readonly IKhyloDatabaseProvider _databaseProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshDatabaseStatisticsPostScanTask"/> class.
    /// </summary>
    /// <param name="databaseProvider">The database provider.</param>
    public RefreshDatabaseStatisticsPostScanTask(IKhyloDatabaseProvider databaseProvider)
    {
        _databaseProvider = databaseProvider;
    }

    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _databaseProvider.RefreshStatistics(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
