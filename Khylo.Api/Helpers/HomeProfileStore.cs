using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Khylo.Api.Helpers;

/// <summary>
/// Which users make up a household (the owner plus their home profiles) and the profiles' PINs.
/// Kept in a small file in the config folder.
/// </summary>
public sealed class HomeProfileStore
{
    private static readonly object _lock = new();
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeProfileStore"/> class.
    /// </summary>
    /// <param name="configurationDirectory">The server's configuration directory.</param>
    public HomeProfileStore(string configurationDirectory)
    {
        _path = Path.Combine(configurationDirectory, "khylo_home_profiles.json");
    }

    /// <summary>
    /// Gets the owner of the household a user belongs to: their own id if they own one or aren't in any.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The household owner's id.</returns>
    public Guid GetOwner(Guid userId)
    {
        var data = Load();
        foreach (var (owner, profiles) in data.Households)
        {
            if (profiles.Contains(userId))
            {
                return owner;
            }
        }

        return userId;
    }

    /// <summary>
    /// Gets the household's members: the owner first, then the profiles.
    /// </summary>
    /// <param name="ownerId">The household owner.</param>
    /// <returns>The member ids.</returns>
    public IReadOnlyList<Guid> GetMembers(Guid ownerId)
    {
        var data = Load();
        var members = new List<Guid> { ownerId };
        if (data.Households.TryGetValue(ownerId, out var profiles))
        {
            members.AddRange(profiles);
        }

        return members;
    }

    /// <summary>
    /// Adds a profile to a household.
    /// </summary>
    /// <param name="ownerId">The household owner.</param>
    /// <param name="profileId">The new profile.</param>
    public void AddProfile(Guid ownerId, Guid profileId)
    {
        Update(data =>
        {
            if (!data.Households.TryGetValue(ownerId, out var profiles))
            {
                profiles = new List<Guid>();
                data.Households[ownerId] = profiles;
            }

            profiles.Add(profileId);
        });
    }

    /// <summary>
    /// Removes a profile from its household and forgets its PIN.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    public void RemoveProfile(Guid profileId)
    {
        Update(data =>
        {
            foreach (var profiles in data.Households.Values)
            {
                profiles.Remove(profileId);
            }

            data.Pins.Remove(profileId);
            data.Kids.Remove(profileId);
            data.ProfileAccess.Remove(profileId);
        });
    }

    /// <summary>
    /// Gets a value indicating whether a profile needs a PIN to switch into.
    /// </summary>
    /// <param name="userId">The profile.</param>
    /// <returns>Whether a PIN is set.</returns>
    public bool HasPin(Guid userId) => Load().Pins.ContainsKey(userId);

    /// <summary>
    /// Checks a PIN. Profiles without one always pass.
    /// </summary>
    /// <param name="userId">The profile.</param>
    /// <param name="pin">The PIN entered.</param>
    /// <returns>Whether the PIN is right.</returns>
    public bool CheckPin(Guid userId, string? pin)
    {
        if (!Load().Pins.TryGetValue(userId, out var hash))
        {
            return true;
        }

        return pin is not null && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Hash(userId, pin));
    }

    /// <summary>
    /// Sets or clears (null) a profile's PIN.
    /// </summary>
    /// <param name="userId">The profile.</param>
    /// <param name="pin">The PIN, or null to remove it.</param>
    public void SetPin(Guid userId, string? pin)
    {
        Update(data =>
        {
            if (string.IsNullOrEmpty(pin))
            {
                data.Pins.Remove(userId);
            }
            else
            {
                data.Pins[userId] = Convert.ToHexString(Hash(userId, pin));
            }
        });
    }

    /// <summary>
    /// Gets the server-wide home profile settings.
    /// </summary>
    /// <returns>Whether users may add profiles by default, and how many profiles a home can have.</returns>
    public (bool AllowByDefault, int MaxProfilesPerHome) GetSettings()
    {
        var data = Load();
        return (data.AllowProfilesByDefault, data.MaxProfilesPerHome);
    }

    /// <summary>
    /// Saves the server-wide home profile settings.
    /// </summary>
    /// <param name="allowByDefault">Whether users may add profiles unless the admin says otherwise.</param>
    /// <param name="maxProfilesPerHome">How many profiles a home can have, besides its owner.</param>
    public void SetSettings(bool allowByDefault, int maxProfilesPerHome)
    {
        Update(data =>
        {
            data.AllowProfilesByDefault = allowByDefault;
            data.MaxProfilesPerHome = Math.Clamp(maxProfilesPerHome, 1, 20);
        });
    }

    /// <summary>
    /// Gets the admin's choice for one user: true or false, or null to follow the server default.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The user's setting, if one was made.</returns>
    public bool? GetUserAllowance(Guid userId) => Load().ProfileAccess.TryGetValue(userId, out var allowed) ? allowed : null;

    /// <summary>
    /// Sets whether a user may add home profiles; null follows the server default.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="allowed">The choice.</param>
    public void SetUserAllowance(Guid userId, bool? allowed)
    {
        Update(data =>
        {
            if (allowed is null)
            {
                data.ProfileAccess.Remove(userId);
            }
            else
            {
                data.ProfileAccess[userId] = allowed.Value;
            }
        });
    }

    /// <summary>
    /// Gets a value indicating whether a (non-admin) user may add home profiles.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>Whether they may.</returns>
    public bool MayAddProfiles(Guid userId)
    {
        var data = Load();
        return data.ProfileAccess.TryGetValue(userId, out var allowed) ? allowed : data.AllowProfilesByDefault;
    }

    /// <summary>
    /// Gets a value indicating whether a profile was made as a kids profile.
    /// </summary>
    /// <param name="userId">The profile.</param>
    /// <returns>Whether it's a kids profile.</returns>
    public bool IsKids(Guid userId) => Load().Kids.Contains(userId);

    /// <summary>
    /// Marks a profile as a kids profile.
    /// </summary>
    /// <param name="userId">The profile.</param>
    public void MarkKids(Guid userId) => Update(data => data.Kids.Add(userId));

    /// <summary>
    /// Gets every household: owner and profiles.
    /// </summary>
    /// <returns>The households.</returns>
    public IReadOnlyDictionary<Guid, List<Guid>> GetHouseholds() => Load().Households;

    // Salted with the user id, so the same PIN on two profiles stores differently
    private static byte[] Hash(Guid userId, string pin) => SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString("N") + ":" + pin));

    private HomeProfileData Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new HomeProfileData();
            }

            try
            {
                return JsonSerializer.Deserialize<HomeProfileData>(File.ReadAllText(_path)) ?? new HomeProfileData();
            }
            catch (JsonException)
            {
                return new HomeProfileData();
            }
        }
    }

    private void Update(Action<HomeProfileData> change)
    {
        lock (_lock)
        {
            var data = Load();
            change(data);
            File.WriteAllText(_path, JsonSerializer.Serialize(data));
        }
    }

    private sealed class HomeProfileData
    {
        public Dictionary<Guid, List<Guid>> Households { get; set; } = new();

        public Dictionary<Guid, string> Pins { get; set; } = new();

        public bool AllowProfilesByDefault { get; set; } = true;

        public int MaxProfilesPerHome { get; set; } = 6;

        public Dictionary<Guid, bool> ProfileAccess { get; set; } = new();

        public HashSet<Guid> Kids { get; set; } = new();
    }
}
