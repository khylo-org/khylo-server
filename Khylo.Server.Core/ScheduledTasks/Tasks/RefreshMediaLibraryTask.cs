using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.Globalization;
using Khylo.Model.Tasks;
using Khylo.Server.Core.Library;

namespace Khylo.Server.Core.ScheduledTasks.Tasks;

/// <summary>
/// Class RefreshMediaLibraryTask.
/// </summary>
public class RefreshMediaLibraryTask : IScheduledTask
{
    /// <summary>
    /// The _library manager.
    /// </summary>
    private readonly ILibraryManager _libraryManager;
    private readonly ILocalizationManager _localization;
    private readonly IProviderManager _providerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshMediaLibraryTask" /> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="providerManager">The provider manager, to stop queued refreshes when the scan is stopped.</param>
    public RefreshMediaLibraryTask(ILibraryManager libraryManager, ILocalizationManager localization, IProviderManager providerManager)
    {
        _libraryManager = libraryManager;
        _localization = localization;
        _providerManager = providerManager;
    }

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("TaskRefreshLibrary");

    /// <inheritdoc />
    public string Description => _localization.GetLocalizedString("TaskRefreshLibraryDescription");

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksLibraryCategory");

    /// <inheritdoc />
    public string Key => "RefreshLibrary";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Every 6 hours, plus a quiet nightly scan, so new media always turns up even where folder
        // watching can't see changes (network drives)
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(6).Ticks
        };

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(0);

        // Stopping the scan also stops the metadata refreshes (including intro and credit detection)
        // it queued, which would otherwise carry on in the background
        using var stopRefreshes = cancellationToken.Register(_providerManager.CancelQueuedRefreshes);

        await ((LibraryManager)_libraryManager).ValidateMediaLibraryInternal(progress, cancellationToken).ConfigureAwait(false);
    }
}
