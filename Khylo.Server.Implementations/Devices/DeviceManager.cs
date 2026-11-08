using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Common.Extensions;
using Khylo.Controller.Devices;
using Khylo.Controller.Library;
using Khylo.Data;
using Khylo.Data.Dtos;
using Khylo.Data.Events;
using Khylo.Data.Queries;
using Khylo.Database.Implementations;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Entities.Security;
using Khylo.Database.Implementations.Enums;
using Khylo.Extensions;
using Khylo.Model.Devices;
using Khylo.Model.Dto;
using Khylo.Model.Querying;
using Khylo.Model.Session;
using Microsoft.EntityFrameworkCore;

namespace Khylo.Server.Implementations.Devices
{
    /// <summary>
    /// Manages the creation, updating, and retrieval of devices.
    /// </summary>
    public class DeviceManager : IDeviceManager
    {
        private readonly IDbContextFactory<KhyloDbContext> _dbProvider;
        private readonly IUserManager _userManager;
        private readonly string _capabilitiesFile;
        private readonly object _capabilitiesFileLock = new();
        private readonly ConcurrentDictionary<string, ClientCapabilities> _capabilitiesMap = new();
        private readonly ConcurrentDictionary<int, Device> _devices;
        private readonly ConcurrentDictionary<string, DeviceOptions> _deviceOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceManager"/> class.
        /// </summary>
        /// <param name="dbProvider">The database provider.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="appPaths">The application paths.</param>
        public DeviceManager(IDbContextFactory<KhyloDbContext> dbProvider, IUserManager userManager, IApplicationPaths appPaths)
        {
            _dbProvider = dbProvider;
            _userManager = userManager;
            _capabilitiesFile = Path.Combine(appPaths.DataPath, "device-capabilities.json");
            LoadCapabilities();
            _devices = new ConcurrentDictionary<int, Device>();
            _deviceOptions = new ConcurrentDictionary<string, DeviceOptions>();

            using var dbContext = _dbProvider.CreateDbContext();
            foreach (var device in dbContext.Devices
                         .OrderBy(d => d.Id)
                         .AsEnumerable())
            {
                _devices.TryAdd(device.Id, device);
            }

            foreach (var deviceOption in dbContext.DeviceOptions
                         .OrderBy(d => d.Id)
                         .AsEnumerable())
            {
                _deviceOptions.TryAdd(deviceOption.DeviceId, deviceOption);
            }
        }

        /// <inheritdoc />
        public event EventHandler<GenericEventArgs<Tuple<string, DeviceOptions>>>? DeviceOptionsUpdated;

        /// <inheritdoc />
        public void SaveCapabilities(string deviceId, ClientCapabilities capabilities)
        {
            _capabilitiesMap[deviceId] = capabilities;
            PersistCapabilities();
        }

        // What each device can do (play, be controlled) is kept on disk. Held only in memory, every server restart made all devices
        // look uncontrollable until each app was reopened, so Handoff and Mirror found nobody to send to
        private void LoadCapabilities()
        {
            try
            {
                if (!File.Exists(_capabilitiesFile))
                {
                    return;
                }

                var saved = JsonSerializer.Deserialize<Dictionary<string, ClientCapabilities>>(File.ReadAllText(_capabilitiesFile));
                foreach (var (deviceId, capabilities) in saved ?? [])
                {
                    _capabilitiesMap[deviceId] = capabilities;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged file only means devices report again the next time they open
            }
        }

        private void PersistCapabilities()
        {
            try
            {
                lock (_capabilitiesFileLock)
                {
                    // Only what Handoff and Mirror need: the device profile is large and is sent again whenever a device plays something
                    var slim = _capabilitiesMap.ToDictionary(
                        p => p.Key,
                        p => new ClientCapabilities
                        {
                            PlayableMediaTypes = p.Value.PlayableMediaTypes,
                            SupportedCommands = p.Value.SupportedCommands,
                            SupportsMediaControl = p.Value.SupportsMediaControl,
                            SupportsPersistentIdentifier = p.Value.SupportsPersistentIdentifier,
                            AppStoreUrl = p.Value.AppStoreUrl,
                            IconUrl = p.Value.IconUrl
                        });
                    File.WriteAllText(_capabilitiesFile, JsonSerializer.Serialize(slim));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not being able to save is not worth failing the device's request for
            }
        }

        /// <inheritdoc />
        public async Task UpdateDeviceOptions(string deviceId, string? deviceName)
        {
            DeviceOptions? deviceOptions;
            var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                deviceOptions = await dbContext.DeviceOptions.FirstOrDefaultAsync(dev => dev.DeviceId == deviceId).ConfigureAwait(false);
                if (deviceOptions is null)
                {
                    deviceOptions = new DeviceOptions(deviceId);
                    dbContext.DeviceOptions.Add(deviceOptions);
                }

                deviceOptions.CustomName = deviceName;
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            _deviceOptions[deviceId] = deviceOptions;

            DeviceOptionsUpdated?.Invoke(this, new GenericEventArgs<Tuple<string, DeviceOptions>>(new Tuple<string, DeviceOptions>(deviceId, deviceOptions)));
        }

        /// <inheritdoc />
        public async Task<Device> CreateDevice(Device device)
        {
            var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                dbContext.Devices.Add(device);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
                _devices.TryAdd(device.Id, device);
            }

            return device;
        }

        /// <inheritdoc />
        public DeviceOptionsDto? GetDeviceOptions(string deviceId)
        {
            if (_deviceOptions.TryGetValue(deviceId, out var deviceOptions))
            {
                return ToDeviceOptionsDto(deviceOptions);
            }

            return null;
        }

        /// <inheritdoc />
        public ClientCapabilities GetCapabilities(string? deviceId)
        {
            if (deviceId is null)
            {
                return new();
            }

            return _capabilitiesMap.TryGetValue(deviceId, out ClientCapabilities? result)
                ? result
                : new();
        }

        /// <inheritdoc />
        public DeviceInfoDto? GetDevice(string id)
        {
            var device = _devices.Values.Where(d => d.DeviceId == id).OrderByDescending(d => d.DateLastActivity).FirstOrDefault();
            _deviceOptions.TryGetValue(id, out var deviceOption);

            var deviceInfo = device is null ? null : ToDeviceInfo(device, deviceOption);
            return deviceInfo is null ? null : ToDeviceInfoDto(deviceInfo);
        }

        /// <inheritdoc />
        public QueryResult<Device> GetDevices(DeviceQuery query)
        {
            IEnumerable<Device> devices = _devices.Values
                .Where(device => !query.UserId.HasValue || device.UserId.Equals(query.UserId.Value))
                .Where(device => query.DeviceId is null || device.DeviceId == query.DeviceId)
                .Where(device => query.AccessToken is null || device.AccessToken == query.AccessToken)
                .OrderBy(d => d.Id)
                .ToList();
            var count = devices.Count();

            if (query.Skip.HasValue)
            {
                devices = devices.Skip(query.Skip.Value);
            }

            if (query.Limit.HasValue && query.Limit.Value > 0)
            {
                devices = devices.Take(query.Limit.Value);
            }

            return new QueryResult<Device>(query.Skip, count, devices.ToList());
        }

        /// <inheritdoc />
        public QueryResult<DeviceInfo> GetDeviceInfos(DeviceQuery query)
        {
            var devices = GetDevices(query);

            return new QueryResult<DeviceInfo>(
                devices.StartIndex,
                devices.TotalRecordCount,
                devices.Items.Select(device => ToDeviceInfo(device)).ToList());
        }

        /// <inheritdoc />
        public QueryResult<DeviceInfoDto> GetDevicesForUser(Guid? userId)
        {
            IEnumerable<Device> devices = _devices.Values
                .OrderByDescending(d => d.DateLastActivity)
                .ThenBy(d => d.DeviceId);

            if (!userId.IsNullOrEmpty())
            {
                var user = _userManager.GetUserById(userId.Value);
                if (user is null)
                {
                    throw new ResourceNotFoundException();
                }

                devices = devices.Where(i => CanAccessDevice(user, i.DeviceId));
            }

            var array = devices.Select(device =>
                {
                    _deviceOptions.TryGetValue(device.DeviceId, out var option);
                    return ToDeviceInfo(device, option);
                })
                .Select(ToDeviceInfoDto)
                .ToArray();

            return new QueryResult<DeviceInfoDto>(array);
        }

        /// <inheritdoc />
        public async Task DeleteDevice(Device device)
        {
            _devices.TryRemove(device.Id, out _);
            var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                await dbContext.Devices
                    .Where(d => d.Id == device.Id)
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task UpdateDevice(Device device)
        {
            var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                dbContext.Devices.Update(device);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            _devices[device.Id] = device;
        }

        /// <inheritdoc />
        public bool CanAccessDevice(User user, string deviceId)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentException.ThrowIfNullOrEmpty(deviceId);

            if (user.HasPermission(PermissionKind.EnableAllDevices) || user.HasPermission(PermissionKind.IsAdministrator))
            {
                return true;
            }

            return user.GetPreference(PreferenceKind.EnabledDevices).Contains(deviceId, StringComparison.OrdinalIgnoreCase)
                   || !GetCapabilities(deviceId).SupportsPersistentIdentifier;
        }

        private DeviceInfo ToDeviceInfo(Device authInfo, DeviceOptions? options = null)
        {
            var caps = GetCapabilities(authInfo.DeviceId);
            var user = _userManager.GetUserById(authInfo.UserId) ?? throw new ResourceNotFoundException("User with UserId " + authInfo.UserId + " not found");

            return new()
            {
                AppName = authInfo.AppName,
                AppVersion = authInfo.AppVersion,
                Id = authInfo.DeviceId,
                LastUserId = authInfo.UserId,
                LastUserName = user.Username,
                Name = authInfo.DeviceName,
                DateLastActivity = authInfo.DateLastActivity,
                IconUrl = caps.IconUrl,
                CustomName = options?.CustomName,
            };
        }

        private DeviceOptionsDto ToDeviceOptionsDto(DeviceOptions options)
        {
            return new()
            {
                Id = options.Id,
                DeviceId = options.DeviceId,
                CustomName = options.CustomName,
            };
        }

        private DeviceInfoDto ToDeviceInfoDto(DeviceInfo info)
        {
            return new()
            {
                Name = info.Name,
                CustomName = info.CustomName,
                AccessToken = info.AccessToken,
                Id = info.Id,
                LastUserName = info.LastUserName,
                AppName = info.AppName,
                AppVersion = info.AppVersion,
                LastUserId = info.LastUserId,
                DateLastActivity = info.DateLastActivity,
                Capabilities = ToClientCapabilitiesDto(info.Capabilities),
                IconUrl = info.IconUrl
            };
        }

        /// <inheritdoc />
        public ClientCapabilitiesDto ToClientCapabilitiesDto(ClientCapabilities capabilities)
        {
            return new()
            {
                PlayableMediaTypes = capabilities.PlayableMediaTypes,
                SupportedCommands = capabilities.SupportedCommands,
                SupportsMediaControl = capabilities.SupportsMediaControl,
                SupportsPersistentIdentifier = capabilities.SupportsPersistentIdentifier,
                DeviceProfile = capabilities.DeviceProfile,
                AppStoreUrl = capabilities.AppStoreUrl,
                IconUrl = capabilities.IconUrl
            };
        }
    }
}
