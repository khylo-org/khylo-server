using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Api.Helpers;
using Khylo.Common.Extensions;
using Khylo.Common.Net;
using Khylo.Controller.Authentication;
using Khylo.Controller.Configuration;
using Khylo.Controller.Library;
using Khylo.Controller.Net;
using Khylo.Controller.Session;
using Khylo.Data;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Extensions;
using Khylo.Model.Activity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Khylo.Api.Controllers;

/// <summary>
/// Sign a new device in by scanning a QR code shown on a device that's already signed in. The signed-in device
/// makes a short-lived, single-use code; the new device redeems it and is signed in as that person, with no
/// server address, user name or password typed. Codes live in memory only, so a restart throws them away.
/// </summary>
[Route("Khylo/SignInCodes")]
public class KhyloSignInCodesController : BaseKhyloApiController
{
    private const int MaxUnusedPerUser = 5;
    private const int MaxFailuresPerMinute = 10;
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _keepFinished = TimeSpan.FromMinutes(10);

    private static readonly object _lock = new();
    private static readonly List<SignInCode> _codes = [];
    private static readonly Dictionary<string, List<DateTime>> _failures = new(StringComparer.Ordinal);

    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly IAuthorizationContext _authContext;
    private readonly IServerConfigurationManager _config;
    private readonly IActivityManager _activityManager;
    private readonly ILogger<KhyloSignInCodesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloSignInCodesController"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="authContext">The authorization context.</param>
    /// <param name="config">The server configuration.</param>
    /// <param name="activityManager">The activity log.</param>
    /// <param name="logger">The logger.</param>
    public KhyloSignInCodesController(
        ISessionManager sessionManager,
        IUserManager userManager,
        IAuthorizationContext authContext,
        IServerConfigurationManager config,
        IActivityManager activityManager,
        ILogger<KhyloSignInCodesController> logger)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _authContext = authContext;
        _config = config;
        _activityManager = activityManager;
        _logger = logger;
    }

    /// <summary>
    /// Makes a sign-in code for the signed-in user.
    /// </summary>
    /// <response code="200">The code and the address to draw as a QR code.</response>
    /// <returns>The code.</returns>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignInCodeResult>> Create()
    {
        var auth = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        var userId = User.GetUserId();
        // Whole seconds, so every app's date reader understands the expiry time
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var code = new SignInCode
        {
            Code = Base64Url(RandomNumberGenerator.GetBytes(32)),
            UserId = userId,
            CreatedOn = auth.Device ?? auth.Client ?? "another device",
            ExpiresAt = now + _lifetime
        };

        lock (_lock)
        {
            Prune(now);

            // At most five waiting codes a person; a sixth replaces the oldest
            var waiting = _codes.Where(c => c.UserId.Equals(userId) && !c.Used && c.ExpiresAt > now).OrderBy(c => c.ExpiresAt).ToList();
            foreach (var old in waiting.Take(Math.Max(0, waiting.Count - (MaxUnusedPerUser - 1))))
            {
                _codes.Remove(old);
            }

            _codes.Add(code);
        }

        return new SignInCodeResult
        {
            Code = code.Code,
            ExpiresAt = code.ExpiresAt,
            ExpiresInSeconds = (int)_lifetime.TotalSeconds,
            QrUrl = $"{AppAddress()}/#/signin?code={code.Code}"
        };
    }

    /// <summary>
    /// Checks whether a code has been used yet.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <response code="200">Where the code stands.</response>
    /// <response code="404">Unknown, or someone else's.</response>
    /// <returns>The code's state.</returns>
    [HttpGet("{code}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SignInCodeStatus> Status([FromRoute, Required] string code)
    {
        var userId = User.GetUserId();
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            Prune(now);
            var found = Find(code);
            if (found is null || !found.UserId.Equals(userId))
            {
                return NotFound();
            }

            return new SignInCodeStatus
            {
                Used = found.Used,
                Expired = !found.Used && found.ExpiresAt <= now,
                DeviceName = found.DeviceName,
                ClientName = found.ClientName
            };
        }
    }

    /// <summary>
    /// Cancels a code nobody has used yet.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <response code="204">Cancelled.</response>
    /// <response code="404">Unknown, or someone else's.</response>
    /// <returns>No content.</returns>
    [HttpDelete("{code}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Cancel([FromRoute, Required] string code)
    {
        var userId = User.GetUserId();
        lock (_lock)
        {
            var found = Find(code);
            if (found is null || !found.UserId.Equals(userId))
            {
                return NotFound();
            }

            _codes.Remove(found);
        }

        return NoContent();
    }

    /// <summary>
    /// Signs the calling device in with a code made on another device.
    /// </summary>
    /// <param name="request">The code.</param>
    /// <response code="200">Signed in, exactly as a password sign-in.</response>
    /// <response code="403">The account is disabled or can't sign in right now.</response>
    /// <response code="404">Unknown, expired or already used.</response>
    /// <response code="429">Too many failed attempts from this address.</response>
    /// <returns>The authentication result.</returns>
    [HttpPost("Redeem")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthenticationResult>> Redeem([FromBody, Required] RedeemSignInCodeRequest request)
    {
        var remote = HttpContext.GetNormalizedRemoteIP().ToString();
        var now = DateTime.UtcNow;
        SignInCode? code;
        lock (_lock)
        {
            if (TooManyFailures(remote, now))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests);
            }

            Prune(now);
            code = Find(request.Code ?? string.Empty);
            if (code is null || code.Used || code.ExpiresAt <= now)
            {
                RecordFailure(remote, now);
                return NotFound();
            }

            // Taken before anything else happens, so two devices racing get one success and one 404
            code.Used = true;
        }

        var user = _userManager.GetUserById(code.UserId);
        if (user is null)
        {
            return NotFound();
        }

        // The same rules a password sign-in checks before letting someone in
        if (user.HasPermission(PermissionKind.IsDisabled) || !user.IsParentalScheduleAllowed())
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var auth = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (string.IsNullOrEmpty(auth.Client) || string.IsNullOrEmpty(auth.DeviceId) || string.IsNullOrEmpty(auth.Device) || string.IsNullOrEmpty(auth.Version))
        {
            return BadRequest("Send the app's Authorization header (Client, Device, DeviceId, Version).");
        }

        AuthenticationResult result;
        try
        {
            result = await _sessionManager.AuthenticateDirect(new AuthenticationRequest
            {
                UserId = user.Id,
                App = auth.Client,
                AppVersion = auth.Version,
                DeviceId = auth.DeviceId,
                DeviceName = auth.Device,
                RemoteEndPoint = remote
            }).ConfigureAwait(false);
        }
        catch (System.Security.SecurityException ex)
        {
            // Device not allowed, or the account (or its home) is at its limit of devices
            _logger.LogInformation("Scan-to-sign-in for {User} refused: {Reason}", user.Username, ex.Message);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        lock (_lock)
        {
            code.DeviceName = auth.Device;
            code.ClientName = auth.Client;
            code.FinishedAt = DateTime.UtcNow;
        }

        await _activityManager.CreateAsync(new ActivityLog(
            $"{user.Username} signed in on {auth.Device} by scanning a code from {code.CreatedOn}",
            "AuthenticationSucceeded",
            user.Id)
        {
            ShortOverview = $"IP address: {remote}"
        }).ConfigureAwait(false);

        return result;
    }

    // Where the web app lives: the server's web address when it has one, otherwise how this request reached it
    private string AppAddress()
    {
        var web = _config.Configuration.KhyloWebAddress?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(web))
        {
            return web.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? web : "https://" + web;
        }

        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
    }

    private static SignInCode? Find(string code)
    {
        var wanted = Encoding.UTF8.GetBytes(code);
        SignInCode? match = null;
        foreach (var c in _codes)
        {
            // Every code is compared in full, so timing says nothing about how close a guess was
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(c.Code), wanted))
            {
                match = c;
            }
        }

        return match;
    }

    private static void Prune(DateTime now)
    {
        _codes.RemoveAll(c => (c.FinishedAt ?? c.ExpiresAt) + _keepFinished < now);
        foreach (var key in _failures.Keys.ToList())
        {
            _failures[key].RemoveAll(t => t < now.AddMinutes(-1));
            if (_failures[key].Count == 0)
            {
                _failures.Remove(key);
            }
        }
    }

    private static bool TooManyFailures(string remote, DateTime now)
        => _failures.TryGetValue(remote, out var times) && times.Count(t => t > now.AddMinutes(-1)) >= MaxFailuresPerMinute;

    private static void RecordFailure(string remote, DateTime now)
    {
        if (!_failures.TryGetValue(remote, out var times))
        {
            _failures[remote] = times = [];
        }

        times.Add(now);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class SignInCode
    {
        public string Code { get; set; } = string.Empty;

        public Guid UserId { get; set; }

        public string CreatedOn { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public bool Used { get; set; }

        public DateTime? FinishedAt { get; set; }

        public string? DeviceName { get; set; }

        public string? ClientName { get; set; }
    }

    /// <summary>
    /// A new sign-in code.
    /// </summary>
    public class SignInCodeResult
    {
        /// <summary>Gets or sets the code (only ever scanned, never typed).</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Gets or sets when it stops working (UTC).</summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>Gets or sets how long it lasts.</summary>
        public int ExpiresInSeconds { get; set; }

        /// <summary>Gets or sets the address to draw as the QR code.</summary>
        public string QrUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Where a sign-in code stands.
    /// </summary>
    public class SignInCodeStatus
    {
        /// <summary>Gets or sets a value indicating whether a device signed in with it.</summary>
        public bool Used { get; set; }

        /// <summary>Gets or sets a value indicating whether it ran out unused.</summary>
        public bool Expired { get; set; }

        /// <summary>Gets or sets the name of the device that used it.</summary>
        public string? DeviceName { get; set; }

        /// <summary>Gets or sets the app that used it.</summary>
        public string? ClientName { get; set; }
    }

    /// <summary>
    /// A code to sign in with.
    /// </summary>
    public class RedeemSignInCodeRequest
    {
        /// <summary>Gets or sets the code.</summary>
        public string? Code { get; set; }
    }
}
