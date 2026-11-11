using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Authentication;
using Khylo.Controller.Invitations;
using Khylo.Controller.Library;
using Khylo.Model.Invitations;
using Khylo.Model.Users;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Implementations.Invitations;

/// <summary>
/// Service managing self-hosted Khylo server user invitations.
/// </summary>
public class InvitationManager : IInvitationManager, IDisposable
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<InvitationManager> _logger;
    private readonly IUserManager _userManager;
    private readonly string _filePath;
    private readonly ConcurrentDictionary<Guid, KhyloInvitationDto> _invitations = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly SemaphoreSlim _redeemLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvitationManager"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="appPaths">Application paths.</param>
    public InvitationManager(
        ILogger<InvitationManager> logger,
        IUserManager userManager,
        IApplicationPaths appPaths)
    {
        _logger = logger;
        _userManager = userManager;
        _filePath = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_invitations.json");
        LoadInvitations();
    }

    /// <inheritdoc/>
    public KhyloInvitationDto CreateInvitation(Guid createdByUserId, CreateInvitationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lifetime = request.LifetimeHours > 0 ? request.LifetimeHours : 48;
        var maxUses = request.MaxUses > 0 ? request.MaxUses : 1;

        // Generate cryptographically random secure token
        var tokenBytes = RandomNumberGenerator.GetBytes(9);
        var code = "khylo-" + Convert.ToHexString(tokenBytes).ToLowerInvariant();

        var invitation = new KhyloInvitationDto
        {
            Id = Guid.NewGuid(),
            Code = code,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(lifetime),
            MaxUses = maxUses,
            UseCount = 0,
            EnabledFolders = request.EnabledFolders ?? Array.Empty<Guid>(),
            IsActive = true
        };

        _invitations[invitation.Id] = invitation;
        SaveInvitations();
        _logger.LogInformation("Created Khylo invitation {InvitationId} valid until {Expiry}", invitation.Id, invitation.ExpiresAtUtc);
        return invitation;
    }

    /// <inheritdoc/>
    public IReadOnlyList<KhyloInvitationDto> GetInvitations()
    {
        RemoveOldFinished();
        return _invitations.Values.OrderByDescending(i => i.CreatedAtUtc).ToList();
    }

    /// <inheritdoc/>
    public KhyloInvitationDto? GetInvitationByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return _invitations.Values.FirstOrDefault(i => string.Equals(i.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public bool RevokeInvitation(Guid invitationId)
    {
        if (_invitations.TryGetValue(invitationId, out var inv))
        {
            inv.IsActive = false;
            inv.RevokedAtUtc ??= DateTime.UtcNow;
            SaveInvitations();
            _logger.LogInformation("Revoked Khylo invitation {InvitationId}", inv.Id);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool DeleteInvitation(Guid invitationId)
    {
        if (!_invitations.TryGetValue(invitationId, out var inv) || inv.FinishedAtUtc is null)
        {
            return false;
        }

        _invitations.TryRemove(invitationId, out _);
        SaveInvitations();
        _logger.LogInformation("Deleted Khylo invitation {InvitationId}", inv.Id);
        return true;
    }

    /// <inheritdoc/>
    public int ClearFinishedInvitations()
    {
        var removed = RemoveFinished(_ => true);
        _logger.LogInformation("Cleared {Count} finished Khylo invitations", removed);
        return removed;
    }

    /// <summary>
    /// Finished invitations tidy themselves away after a month.
    /// </summary>
    private void RemoveOldFinished()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        RemoveFinished(finished => finished < cutoff);
    }

    private int RemoveFinished(Func<DateTime, bool> finishedMatches)
    {
        var removed = 0;
        foreach (var inv in _invitations.Values)
        {
            if (inv.FinishedAtUtc is { } finished && finishedMatches(finished) && _invitations.TryRemove(inv.Id, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            SaveInvitations();
        }

        return removed;
    }

    /// <inheritdoc/>
    public async Task<Guid> RedeemInvitationAsync(string code, string username, string password)
    {
        // One redemption at a time: checking that an invitation still has uses left and counting this use have to happen
        // together, or a single-use invitation could be redeemed by several people at once
        await _redeemLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RedeemCoreAsync(code, username, password).ConfigureAwait(false);
        }
        finally
        {
            _redeemLock.Release();
        }
    }

    private async Task<Guid> RedeemCoreAsync(string code, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (PasswordPolicy.Problem(password, username) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var invitation = GetInvitationByCode(code)
            ?? throw new InvalidOperationException("Invitation code not found.");

        if (!invitation.IsValid)
        {
            throw new InvalidOperationException("Invitation code is expired, fully redeemed, or inactive.");
        }

        // 1. Create the user
        var user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);

        try
        {
            // 2. Set the password
            await _userManager.ChangePassword(user.Id, password).ConfigureAwait(false);

            // 3. Apply the invitation's library access on top of the new user's own policy, which carries the
            //    sign-in and password-reset providers every user needs
            var policy = _userManager.GetUserDto(user).Policy;
            policy.EnableAllFolders = invitation.EnabledFolders.Length == 0;
            policy.EnabledFolders = invitation.EnabledFolders;
            policy.EnableMediaPlayback = true;
            policy.EnableAudioPlaybackTranscoding = true;
            policy.EnableVideoPlaybackTranscoding = true;
            policy.EnablePlaybackRemuxing = true;
            policy.EnableSharedDeviceControl = true;
            policy.EnableUserPreferenceAccess = true;

            await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
        }
        catch
        {
            // Don't leave a half-made account behind; the person can try again with the same name
            await _userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
            throw;
        }

        // 4. Update Invitation redemption count
        invitation.UseCount++;
        invitation.RedeemedBy.Add(new KhyloInvitationRedemption { UserId = user.Id, Username = user.Username, RedeemedAtUtc = DateTime.UtcNow });
        if (invitation.UseCount >= invitation.MaxUses)
        {
            invitation.IsActive = false;
        }

        SaveInvitations();
        _logger.LogInformation("Successfully redeemed invitation {InvitationId} for user {Username} ({UserId})", invitation.Id, username, user.Id);
        return user.Id;
    }

    private void LoadInvitations()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<KhyloInvitationDto>>(json);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        _invitations[item.Id] = item;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Khylo invitations from {Path}", _filePath);
        }
    }

    private void SaveInvitations()
    {
        Task.Run(async () =>
        {
            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var list = _invitations.Values.ToList();
                var json = JsonSerializer.Serialize(list, _jsonSerializerOptions);
                await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Khylo invitations to {Path}", _filePath);
            }
            finally
            {
                _fileLock.Release();
            }
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _fileLock.Dispose();
                _redeemLock.Dispose();
            }

            _disposed = true;
        }
    }
}
