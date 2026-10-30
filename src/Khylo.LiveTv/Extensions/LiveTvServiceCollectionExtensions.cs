using Khylo.Controller.Channels;
using Khylo.Controller.LiveTv;
using Khylo.LiveTv.Channels;
using Khylo.LiveTv.Guide;
using Khylo.LiveTv.IO;
using Khylo.LiveTv.Listings;
using Khylo.LiveTv.Recordings;
using Khylo.LiveTv.Timers;
using Khylo.LiveTv.TunerHosts;
using Khylo.LiveTv.TunerHosts.HdHomerun;
using Khylo.Model.IO;
using Microsoft.Extensions.DependencyInjection;

namespace Khylo.LiveTv.Extensions;

/// <summary>
/// Live TV extensions for <see cref="IServiceCollection"/>.
/// </summary>
public static class LiveTvServiceCollectionExtensions
{
    /// <summary>
    /// Adds Live TV services to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    public static void AddLiveTvServices(this IServiceCollection services)
    {
        services.AddSingleton<LiveTvDtoService>();
        services.AddSingleton<TimerManager>();
        services.AddSingleton<SeriesTimerManager>();
        services.AddSingleton<RecordingsMetadataManager>();

        services.AddSingleton<ILiveTvManager, LiveTvManager>();
        services.AddSingleton<IChannelManager, ChannelManager>();
        services.AddSingleton<IStreamHelper, StreamHelper>();
        services.AddSingleton<ITunerHostManager, TunerHostManager>();
        services.AddSingleton<IListingsManager, ListingsManager>();
        services.AddSingleton<IGuideManager, GuideManager>();
        services.AddSingleton<IRecordingsManager, RecordingsManager>();

        services.AddSingleton<ILiveTvService, DefaultLiveTvService>();
        services.AddSingleton<ITunerHost, HdHomerunHost>();
        services.AddSingleton<ITunerHost, M3UTunerHost>();
        services.AddSingleton<SchedulesDirect>();
        services.AddSingleton<IListingsProvider>(s => s.GetRequiredService<SchedulesDirect>());
        services.AddSingleton<ISchedulesDirectService>(s => s.GetRequiredService<SchedulesDirect>());
        services.AddSingleton<IListingsProvider, XmlTvListingsProvider>();
    }
}
