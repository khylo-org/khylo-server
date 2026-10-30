using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Khylo.Api.Helpers;
using Khylo.Api.Models.HomeProfileDtos;
using Khylo.Common.Api;
using Khylo.Common.Configuration;
using Khylo.Controller.Library;
using Khylo.Data;
using Khylo.Database.Implementations.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Home profiles in the dashboard: whether users the server is shared with may add profiles for
/// their own household, how many, and every household on the server.
/// </summary>
[Route("Khylo/HomeProfiles/Admin")]
[Authorize(Policy = Policies.RequiresElevation)]
public class HomeProfilesAdminController : BaseKhyloApiController
{
    private readonly IUserManager _userManager;
    private readonly HomeProfileStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeProfilesAdminController"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="applicationPaths">The application paths.</param>
    public HomeProfilesAdminController(IUserManager userManager, IApplicationPaths applicationPaths)
    {
        _userManager = userManager;
        _store = new HomeProfileStore(applicationPaths.ConfigurationDirectoryPath);
    }

    /// <summary>
    /// Gets the home profile settings and every household.
    /// </summary>
    /// <response code="200">The settings and households.</response>
    /// <returns>The settings, and each user who owns (or could own) a home.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<HomeProfilesAdminDto> Get()
    {
        var (allowByDefault, maxProfiles) = _store.GetSettings();
        var households = _store.GetHouseholds();
        var profileIds = households.Values.SelectMany(p => p).ToHashSet();

        var users = _userManager.GetUsers()
            .Where(u => !profileIds.Contains(u.Id))
            .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(u =>
            {
                var isAdmin = u.HasPermission(PermissionKind.IsAdministrator);
                var members = households.TryGetValue(u.Id, out var list) ? list : [];
                return new HomeProfilesAdminUserDto
                {
                    Id = u.Id,
                    Name = u.Username,
                    IsAdministrator = isAdmin,
                    Allowed = _store.GetUserAllowance(u.Id),
                    CanAddProfiles = isAdmin || _store.MayAddProfiles(u.Id),
                    Profiles = members
                        .Select(id => _userManager.GetUserById(id))
                        .Where(p => p is not null)
                        .Select(p => new HomeProfileDto { Id = p!.Id, Name = p.Username, IsKids = _store.IsKids(p.Id), HasPin = _store.HasPin(p.Id) })
                        .ToList(),
                };
            })
            .ToList();

        return new HomeProfilesAdminDto { AllowByDefault = allowByDefault, MaxProfilesPerHome = maxProfiles, Users = users };
    }

    /// <summary>
    /// Saves the server-wide home profile settings.
    /// </summary>
    /// <param name="request">The settings.</param>
    /// <response code="204">Saved.</response>
    /// <returns>No content.</returns>
    [HttpPost("Settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SaveSettings([FromBody, Required] HomeProfilesSettingsRequest request)
    {
        _store.SetSettings(request.AllowByDefault, request.MaxProfilesPerHome);
        return NoContent();
    }

    /// <summary>
    /// Sets whether one user may add home profiles.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="request">True, false, or null to follow the server default.</param>
    /// <response code="204">Saved.</response>
    /// <response code="404">No such user.</response>
    /// <returns>No content.</returns>
    [HttpPost("Users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SetUserAllowance([FromRoute, Required] Guid userId, [FromBody, Required] HomeProfilesUserAllowanceRequest request)
    {
        if (_userManager.GetUserById(userId) is null)
        {
            return NotFound();
        }

        _store.SetUserAllowance(userId, request.Allowed);
        return NoContent();
    }

    /// <summary>
    /// Removes a profile from any home, along with its watch history.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <response code="204">Removed.</response>
    /// <response code="404">Not a home profile.</response>
    /// <returns>No content.</returns>
    [HttpDelete("Profiles/{profileId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteProfile([FromRoute, Required] Guid profileId)
    {
        // Only profiles (members of someone's home) can be removed here, never a home's owner
        if (_store.GetOwner(profileId).Equals(profileId) || _userManager.GetUserById(profileId) is null)
        {
            return NotFound();
        }

        await _userManager.DeleteUserAsync(profileId).ConfigureAwait(false);
        _store.RemoveProfile(profileId);
        return NoContent();
    }
}
