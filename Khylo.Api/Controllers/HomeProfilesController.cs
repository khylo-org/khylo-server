using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Api.Helpers;
using Khylo.Api.Models.HomeProfileDtos;
using Khylo.Common.Configuration;
using Khylo.Common.Extensions;
using Khylo.Controller.Authentication;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Session;
using Khylo.Data;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Model.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Home profiles: a household's own profiles (like a family's on a streaming service). Anyone
/// signed in to a profile can switch to another profile in the same household without a password;
/// a PIN can protect a profile, and an administrator's profile always needs one. Users the server is shared with outside the home are never part
/// of a household, so they never see or reach these profiles.
/// </summary>
[Route("Khylo/HomeProfiles")]
[Authorize]
public class HomeProfilesController : BaseKhyloApiController
{
    // Admins who own a home aren't held to the per-home limit, only to this
    private const int MaxProfilesForAdmins = 20;

    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly HomeProfileStore _store;
    private readonly ILocalizationManager _localization;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeProfilesController"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="localization">The localization manager, for content ratings.</param>
    /// <param name="libraryManager">The library manager.</param>
    public HomeProfilesController(IUserManager userManager, ISessionManager sessionManager, IApplicationPaths applicationPaths, ILocalizationManager localization, ILibraryManager libraryManager)
    {
        _userManager = userManager;
        _sessionManager = sessionManager;
        _localization = localization;
        _libraryManager = libraryManager;
        _store = new HomeProfileStore(applicationPaths.ConfigurationDirectoryPath);
    }

    /// <summary>
    /// Gets the profiles in the caller's household.
    /// </summary>
    /// <response code="200">The household.</response>
    /// <returns>The household's profiles and whether the caller can add profiles.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<HomeProfilesDto> GetProfiles()
    {
        var callerId = User.GetUserId();
        var ownerId = _store.GetOwner(callerId);
        var owner = _userManager.GetUserById(ownerId);
        var caller = _userManager.GetUserById(callerId);
        var levels = GetRatingLevels();

        var profiles = _store.GetMembers(ownerId)
            .Select(id => _userManager.GetUserById(id))
            .Where(user => user is not null)
            .Select(user => new HomeProfileDto
            {
                Id = user!.Id,
                Name = user.Username,
                IsOwner = user.Id.Equals(ownerId),
                IsCurrent = user.Id.Equals(callerId),
                HasPin = _store.HasPin(user.Id),
                PrimaryImageTag = _userManager.GetUserDto(user).PrimaryImageTag,
                IsKids = IsKids(user),
                RatingLimit = IsKids(user) ? DescribeLimit(levels, user.MaxParentalRatingScore!.Value) : null,
                NeedsGrownUp = !user.Id.Equals(callerId) && !_store.HasPin(user.Id) && NeedsPinToEnter(caller, user),
            })
            .ToList();

        return new HomeProfilesDto
        {
            Profiles = profiles,
            // The home's owner adds profiles: admins always, others when the server allows it
            CanManage = caller is not null && CanAddProfiles(caller) && profiles.Count - 1 < ProfileLimit(caller),
        };
    }

    /// <summary>
    /// Adds a profile to the caller's household.
    /// </summary>
    /// <param name="request">The new profile.</param>
    /// <response code="200">The profile was added.</response>
    /// <response code="400">The name is taken or the household is full.</response>
    /// <response code="403">Only the household's owner (an admin) can add profiles.</response>
    /// <returns>The profile that was added.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HomeProfileDto>> CreateProfile([FromBody, Required] CreateHomeProfileRequest request)
    {
        var callerId = User.GetUserId();
        var caller = _userManager.GetUserById(callerId);
        if (caller is null || !CanAddProfiles(caller))
        {
            return Forbid();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Enter a name for the profile.");
        }

        var limit = ProfileLimit(caller);
        if (_store.GetMembers(callerId).Count - 1 >= limit)
        {
            return BadRequest($"Your home can have up to {limit} profiles.");
        }

        if (_userManager.GetUserByName(name) is not null)
        {
            return BadRequest("Someone on this server already has that name.");
        }

        if (request.IsKids)
        {
            if (request.MaxParentalRating is null)
            {
                return BadRequest("Choose which ratings this profile can watch.");
            }

            // Without a PIN on the owner's profile, a kid could switch straight into it
            if (!_store.HasPin(callerId))
            {
                if (request.OwnerPin is not { Length: 4 } ownerPin || !ownerPin.All(char.IsAsciiDigit))
                {
                    return BadRequest("Set a 4-digit PIN for your own profile, so kids can't switch into it.");
                }

                _store.SetPin(callerId, ownerPin);
            }
        }

        var profile = await _userManager.CreateUserAsync(name).ConfigureAwait(false);

        // Profiles are reached by switching, never by typing a password
        await _userManager.ChangePassword(profile.Id, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).ConfigureAwait(false);
        _store.AddProfile(callerId, profile.Id);
        if (request.IsKids)
        {
            _store.MarkKids(profile.Id);
        }

        await ApplyProfileAccess(profile.Id, caller, request).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(request.Pin))
        {
            _store.SetPin(profile.Id, request.Pin);
        }

        return new HomeProfileDto { Id = profile.Id, Name = profile.Username, HasPin = !string.IsNullOrEmpty(request.Pin), IsKids = request.IsKids };
    }

    /// <summary>
    /// Gets what a kids profile can be limited to: the content-rating levels and the libraries.
    /// </summary>
    /// <response code="200">The options.</response>
    /// <response code="403">Only the household's owner (an admin) can add profiles.</response>
    /// <returns>The rating levels, libraries, and whether the owner has a PIN.</returns>
    [HttpGet("KidsOptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<KidsOptionsDto> GetKidsOptions()
    {
        var callerId = User.GetUserId();
        var caller = _userManager.GetUserById(callerId);
        if (caller is null || !CanAddProfiles(caller))
        {
            return Forbid();
        }

        // A profile can never see more than the person adding it
        var callerPolicy = _userManager.GetUserDto(caller).Policy;
        var libraries = _libraryManager.GetUserRootFolder().Children
            .Where(i => !i.IsHidden && _libraryManager.GetLibraryOptions(i).Enabled)
            .Where(i => callerPolicy.IsAdministrator || callerPolicy.EnableAllFolders || callerPolicy.EnabledFolders.Contains(i.Id))
            .OrderBy(i => i.SortName)
            .Select(i => new KidsLibraryDto
            {
                Id = i.Id,
                Name = i.Name,
                CollectionType = (i as CollectionFolder)?.CollectionType?.ToString().ToLowerInvariant(),
            })
            .ToList();

        var maxScore = callerPolicy.IsAdministrator ? null : callerPolicy.MaxParentalRating;
        return new KidsOptionsDto
        {
            Levels = GetRatingLevels().Where(l => maxScore is null || l.Score <= maxScore).ToList(),
            Libraries = libraries,
            OwnerHasPin = _store.HasPin(callerId),
            LiveTvAvailable = callerPolicy.IsAdministrator || callerPolicy.EnableLiveTvAccess,
        };
    }

    /// <summary>
    /// Switches this device to another profile in the caller's household.
    /// </summary>
    /// <param name="profileId">The profile to switch to.</param>
    /// <param name="request">The profile's PIN, when it has one.</param>
    /// <response code="200">Signed in as the profile.</response>
    /// <response code="403">The profile isn't in the caller's household, or the PIN is wrong.</response>
    /// <returns>The new sign-in.</returns>
    [HttpPost("{profileId}/Switch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthenticationResult>> SwitchProfile([FromRoute, Required] Guid profileId, [FromBody] SwitchHomeProfileRequest? request)
    {
        var callerId = User.GetUserId();
        var ownerId = _store.GetOwner(callerId);
        if (!_store.GetMembers(ownerId).Contains(profileId) || _userManager.GetUserById(profileId) is null)
        {
            return Forbid();
        }

        // A profile that can run the server, or a grown-up's profile reached from a kids profile, can only be entered
        // with its PIN: without one there is nothing to stop a switch, so it is refused
        if (!profileId.Equals(callerId) && NeedsPinToEnter(_userManager.GetUserById(callerId), _userManager.GetUserById(profileId)) && !_store.HasPin(profileId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Set a PIN on that profile first.");
        }

        if (!profileId.Equals(callerId))
        {
            // PINs are short, so wrong guesses pause further tries (under a key no user name can match)
            var pinKey = "pin:" + profileId.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
            var remote = HttpContext.GetNormalizedRemoteIP().ToString();
            if (LoginThrottle.RetryAfter(pinKey, remote, pin: true) is { } wait)
            {
                Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(StatusCodes.Status429TooManyRequests, "Too many wrong PINs. Try again in a few minutes.");
            }

            if (!_store.CheckPin(profileId, request?.Pin))
            {
                LoginThrottle.RecordFailure(pinKey, remote);
                return Forbid();
            }

            LoginThrottle.RecordSuccess(pinKey, remote);
        }

        return await _sessionManager.AuthenticateDirect(new AuthenticationRequest
        {
            UserId = profileId,
            App = User.GetClient() ?? "Khylo",
            AppVersion = User.GetVersion() ?? "1",
            DeviceId = User.GetDeviceId() ?? Guid.NewGuid().ToString("N"),
            DeviceName = User.GetDevice() ?? "Khylo",
            RemoteEndPoint = HttpContext.GetNormalizedRemoteIP().ToString(),
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets or removes a profile's PIN. Profiles can change their own; the owner can change any.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <param name="request">The new PIN, or empty to remove it.</param>
    /// <response code="204">PIN saved.</response>
    /// <response code="400">The PIN isn't four to eight digits.</response>
    /// <response code="403">Not allowed.</response>
    /// <returns>No content.</returns>
    [HttpPost("{profileId}/Pin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult SetPin([FromRoute, Required] Guid profileId, [FromBody] SwitchHomeProfileRequest? request)
    {
        var callerId = User.GetUserId();
        var ownerId = _store.GetOwner(callerId);
        if (!profileId.Equals(callerId) && !(callerId.Equals(ownerId) && _store.GetMembers(ownerId).Contains(profileId)))
        {
            return Forbid();
        }

        if (!string.IsNullOrEmpty(request?.Pin) && !HomeProfileStore.IsValidPin(request.Pin))
        {
            return BadRequest("A PIN is four to eight digits.");
        }

        _store.SetPin(profileId, request?.Pin);
        return NoContent();
    }

    /// <summary>
    /// Removes a profile (and its watch history). Only the household's owner can.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <response code="204">Removed.</response>
    /// <response code="403">Not allowed.</response>
    /// <returns>No content.</returns>
    [HttpDelete("{profileId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteProfile([FromRoute, Required] Guid profileId)
    {
        var callerId = User.GetUserId();
        if (profileId.Equals(callerId) || !_store.GetOwner(callerId).Equals(callerId) || !_store.GetMembers(callerId).Contains(profileId))
        {
            return Forbid();
        }

        await _userManager.DeleteUserAsync(profileId).ConfigureAwait(false);
        _store.RemoveProfile(profileId);
        return NoContent();
    }

    // Kids profiles are marked when they're made; their limits only an administrator can change
    private bool IsKids(User? user) => user is not null && _store.IsKids(user.Id);

    // Whether getting into a profile must be protected by its PIN: it is an administrator's (read from the account, since an
    // administrator can make any profile one later), or it is a grown-up's and the one switching is a kids profile
    private bool NeedsPinToEnter(User? caller, User? target)
        => target is not null && (target.HasPermission(PermissionKind.IsAdministrator) || (IsKids(caller) && !IsKids(target)));

    // The owner of their own home (not a profile in someone else's) who is an admin, or whom the
    // server lets add profiles
    private bool CanAddProfiles(User user)
        => _store.GetOwner(user.Id).Equals(user.Id)
            && (user.HasPermission(PermissionKind.IsAdministrator) || _store.MayAddProfiles(user.Id));

    private int ProfileLimit(User owner)
        => owner.HasPermission(PermissionKind.IsAdministrator) ? MaxProfilesForAdmins : _store.GetSettings().MaxProfilesPerHome;

    private static string DescribeLimit(List<KidsRatingLevelDto> levels, int score)
    {
        var level = levels.LastOrDefault(l => l.Score <= score);
        return level is null ? "Kids" : string.Join(", ", level.Ratings);
    }

    /// <summary>
    /// The server's content ratings grouped into levels, youngest first, leaving out adult-only
    /// levels. Variants such as TV-PG-V are folded into their base rating.
    /// </summary>
    private List<KidsRatingLevelDto> GetRatingLevels()
    {
        return _localization.GetParentalRatings()
            .Where(r => r.RatingScore is not null && r.RatingScore.Score < 18)
            .GroupBy(r => r.RatingScore!.Score)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var names = g.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return new KidsRatingLevelDto
                {
                    Score = g.Key,
                    Ratings = names.Where(n => !names.Any(other => other != n && n.StartsWith(other + "-", StringComparison.OrdinalIgnoreCase))).ToList(),
                };
            })
            .ToList();
    }

    /// <summary>
    /// Sets what a new profile can reach. Profiles made by someone who isn't an admin get exactly
    /// that person's access (libraries, ratings, Live TV, streaming limits) and never more. A kids
    /// profile is then narrowed further to the ratings, libraries and Live TV the parent chose.
    /// </summary>
    private async Task ApplyProfileAccess(Guid profileId, User owner, CreateHomeProfileRequest request)
    {
        var user = _userManager.GetUserById(profileId)!;
        var policy = _userManager.GetUserDto(user).Policy;
        var ownerPolicy = _userManager.GetUserDto(owner).Policy;

        policy.IsAdministrator = false;
        policy.IsHidden = true;
        policy.EnableLiveTvManagement = false;
        policy.EnableContentDeletion = false;

        if (!ownerPolicy.IsAdministrator)
        {
            policy.EnableAllFolders = ownerPolicy.EnableAllFolders;
            policy.EnabledFolders = ownerPolicy.EnabledFolders;
            policy.EnableAllChannels = ownerPolicy.EnableAllChannels;
            policy.EnabledChannels = ownerPolicy.EnabledChannels;
            policy.MaxParentalRating = ownerPolicy.MaxParentalRating;
            policy.MaxParentalSubRating = ownerPolicy.MaxParentalSubRating;
            policy.BlockUnratedItems = ownerPolicy.BlockUnratedItems;
            policy.BlockedTags = ownerPolicy.BlockedTags;
            policy.AllowedTags = ownerPolicy.AllowedTags;
            policy.AccessSchedules = ownerPolicy.AccessSchedules;
            policy.EnableLiveTvAccess = ownerPolicy.EnableLiveTvAccess;
            policy.EnableRemoteAccess = ownerPolicy.EnableRemoteAccess;
            policy.EnableMediaPlayback = ownerPolicy.EnableMediaPlayback;
            policy.EnableAudioPlaybackTranscoding = ownerPolicy.EnableAudioPlaybackTranscoding;
            policy.EnableVideoPlaybackTranscoding = ownerPolicy.EnableVideoPlaybackTranscoding;
            policy.EnablePlaybackRemuxing = ownerPolicy.EnablePlaybackRemuxing;
            policy.ForceRemoteSourceTranscoding = ownerPolicy.ForceRemoteSourceTranscoding;
            policy.EnableContentDownloading = ownerPolicy.EnableContentDownloading;
            policy.EnableSyncTranscoding = ownerPolicy.EnableSyncTranscoding;
            policy.EnableMediaConversion = ownerPolicy.EnableMediaConversion;
            policy.RemoteClientBitrateLimit = ownerPolicy.RemoteClientBitrateLimit;
            policy.MaxActiveSessions = ownerPolicy.MaxActiveSessions;
            policy.EnableCollectionManagement = false;
            policy.EnableSubtitleManagement = false;
        }

        if (request.IsKids)
        {
            // The chosen rating, but never above what the owner may watch
            policy.MaxParentalRating = policy.MaxParentalRating is int ownerMax && request.MaxParentalRating is int chosen
                ? Math.Min(ownerMax, chosen)
                : request.MaxParentalRating ?? policy.MaxParentalRating;
            policy.MaxParentalSubRating = null;

            // NR: unrated movies, shows and videos stay hidden unless the parent allows them
            if (!request.AllowUnrated)
            {
                policy.BlockUnratedItems = policy.BlockUnratedItems
                    .Union([UnratedItem.Movie, UnratedItem.Trailer, UnratedItem.Series, UnratedItem.ChannelContent, UnratedItem.LiveTvProgram, UnratedItem.LiveTvChannel, UnratedItem.Other])
                    .ToArray();
            }

            policy.EnableLiveTvAccess = policy.EnableLiveTvAccess && request.AllowLiveTv;
            policy.EnableCollectionManagement = false;
            policy.EnableSubtitleManagement = false;

            if (request.Libraries is { Count: > 0 } libraries)
            {
                var allowed = policy.EnableAllFolders ? libraries : libraries.Where(policy.EnabledFolders.Contains).ToList();
                policy.EnableAllFolders = false;
                policy.EnabledFolders = allowed.ToArray();
            }
        }

        await _userManager.UpdatePolicyAsync(profileId, policy).ConfigureAwait(false);
    }
}
