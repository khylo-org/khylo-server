using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Data;
using Khylo.Database.Implementations.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library;

/// <summary>
/// Turns new library items into notifications for each person who can see them. Things added together are
/// grouped ("3 new episodes of Bonanza"), and the list is kept on the server so it reads and clears the same
/// on every device.
/// </summary>
public sealed class NotificationInbox : BackgroundService, INotificationInbox
{
    private const int MaxPerUser = 100;
    private static readonly TimeSpan _flushEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _keepFor = TimeSpan.FromDays(60);
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<NotificationInbox> _logger;
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ConcurrentQueue<BaseItem> _added = new();
    private Dictionary<Guid, List<KhyloNotification>> _inbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationInbox"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="appPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public NotificationInbox(ILibraryManager libraryManager, IUserManager userManager, IApplicationPaths appPaths, ILogger<NotificationInbox> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _logger = logger;
        _path = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_notifications.json");
        _inbox = Load();
    }

    /// <inheritdoc />
    public IReadOnlyList<KhyloNotification> Get(Guid userId)
    {
        lock (_lock)
        {
            return _inbox.TryGetValue(userId, out var list) ? list.OrderByDescending(n => n.Date).ToList() : [];
        }
    }

    /// <inheritdoc />
    public void MarkRead(Guid userId, Guid id) => Change(userId, list => list.Where(n => n.Id.Equals(id)).ToList().ForEach(n => n.Read = true));

    /// <inheritdoc />
    public void MarkAllRead(Guid userId) => Change(userId, list => list.ForEach(n => n.Read = true));

    /// <inheritdoc />
    public void Remove(Guid userId, Guid id) => Change(userId, list => list.RemoveAll(n => n.Id.Equals(id)));

    /// <inheritdoc />
    public void Clear(Guid userId) => Change(userId, list => list.Clear());

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_flushEvery, stoppingToken).ConfigureAwait(false);
                try
                {
                    Flush();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Couldn't create notifications for new media");
                }
            }
        }
        finally
        {
            _libraryManager.ItemAdded -= OnItemAdded;
        }
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        var item = e.Item;
        if (!item.IsVirtualItem && item is Movie or Episode or MusicAlbum or Book or AudioBook or Comic or PodcastEpisode or Photo)
        {
            _added.Enqueue(item);
        }
    }

    // Everything added since the last minute becomes a few grouped notifications per person
    private void Flush()
    {
        var items = new List<BaseItem>();
        while (_added.TryDequeue(out var item))
        {
            items.Add(item);
        }

        if (items.Count == 0)
        {
            return;
        }

        var users = _userManager.GetUsers().Where(u => !u.HasPermission(PermissionKind.IsDisabled)).ToList();
        lock (_lock)
        {
            foreach (var user in users)
            {
                var visible = items.Where(i => SafeVisible(i, user)).ToList();
                if (visible.Count == 0)
                {
                    continue;
                }

                if (!_inbox.TryGetValue(user.Id, out var list))
                {
                    _inbox[user.Id] = list = [];
                }

                // One kind failing never costs the others their notification
                foreach (var group in visible.GroupBy(i => i.GetType()))
                {
                    try
                    {
                        list.AddRange(Describe(group.ToList()));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Couldn't describe new {Kind} items for a notification", group.Key.Name);
                    }
                }

                list.RemoveAll(n => n.Date < DateTime.UtcNow - _keepFor);
                if (list.Count > MaxPerUser)
                {
                    list.RemoveRange(0, list.Count - MaxPerUser);
                }
            }

            Save();
        }
    }

    private static IEnumerable<KhyloNotification> Describe(List<BaseItem> items)
    {
        // Episodes: one per show
        foreach (var show in items.OfType<Episode>().GroupBy(e => e.SeriesId))
        {
            var episodes = show.ToList();
            var first = episodes[0];
            yield return episodes.Count == 1
                ? new KhyloNotification { Title = $"New episode of {first.SeriesName}", Text = first.Name, ItemId = first.Id, ImageItemId = show.Key }
                : new KhyloNotification { Title = $"{episodes.Count} new episodes of {first.SeriesName}", ItemId = show.Key, ImageItemId = show.Key };
        }

        // Podcast episodes: one per podcast
        foreach (var podcast in items.OfType<PodcastEpisode>().GroupBy(e => e.ParentId))
        {
            var episodes = podcast.ToList();
            var parentName = episodes[0].GetParent()?.Name ?? "a podcast";
            yield return episodes.Count == 1
                ? new KhyloNotification { Title = $"New episode of {parentName}", Text = episodes[0].Name, ItemId = podcast.Key, ImageItemId = podcast.Key }
                : new KhyloNotification { Title = $"{episodes.Count} new episodes of {parentName}", ItemId = podcast.Key, ImageItemId = podcast.Key };
        }

        // Photos: one per folder
        foreach (var folder in items.OfType<Photo>().GroupBy(p => p.ParentId))
        {
            var photos = folder.ToList();
            yield return new KhyloNotification
            {
                Title = photos.Count == 1 ? "New photo" : $"{photos.Count} new photos",
                Text = photos[0].GetParent()?.Name,
                ItemId = photos[0].Id,
                ImageItemId = photos[0].Id
            };
        }

        foreach (var n in Several(items.OfType<Movie>().Cast<BaseItem>().ToList(), "New movie", "new movies"))
        {
            yield return n;
        }

        foreach (var n in Several(items.OfType<MusicAlbum>().Cast<BaseItem>().ToList(), "New album", "new albums"))
        {
            yield return n;
        }

        foreach (var n in Several(items.Where(i => i is Book).ToList(), "New book", "new books"))
        {
            yield return n;
        }

        foreach (var n in Several(items.OfType<Comic>().Cast<BaseItem>().ToList(), "New comic", "new comics"))
        {
            yield return n;
        }

        foreach (var n in Several(items.OfType<AudioBook>().Cast<BaseItem>().ToList(), "New audiobook", "new audiobooks"))
        {
            yield return n;
        }
    }

    // A few things get a notification each; more than that becomes one summary
    private static IEnumerable<KhyloNotification> Several(List<BaseItem> items, string one, string many)
    {
        if (items.Count == 0)
        {
            yield break;
        }

        if (items.Count <= 3)
        {
            foreach (var i in items)
            {
                var by = i is MusicAlbum album && !string.IsNullOrEmpty(album.AlbumArtist) ? album.AlbumArtist : null;
                yield return new KhyloNotification { Title = $"{one}: {i.Name}", Text = by, ItemId = i.Id, ImageItemId = i.Id };
            }

            yield break;
        }

        yield return new KhyloNotification
        {
            Title = $"{items.Count} {many}",
            Text = string.Join(", ", items.Take(3).Select(i => i.Name)) + ", and more",
            ImageItemId = items[0].Id
        };
    }

    private bool SafeVisible(BaseItem item, Khylo.Database.Implementations.Entities.User user)
    {
        try
        {
            return item.IsVisible(user);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Couldn't check whether {Item} is visible", item.Name);
            return false;
        }
    }

    private void Change(Guid userId, Action<List<KhyloNotification>> change)
    {
        lock (_lock)
        {
            if (_inbox.TryGetValue(userId, out var list))
            {
                change(list);
                Save();
            }
        }
    }

    private Dictionary<Guid, List<KhyloNotification>> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<Dictionary<Guid, List<KhyloNotification>>>(File.ReadAllText(_path)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Couldn't read notifications; starting fresh");
        }

        return [];
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_inbox, _json));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Couldn't save notifications");
        }
    }
}
