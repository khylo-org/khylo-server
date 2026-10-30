using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Common.Api;
using Khylo.Controller.Configuration;
using Khylo.Controller.Invitations;
using Khylo.Model.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// The outcome of signing up with an invitation.
/// </summary>
public class RedeemInvitationResult
{
    /// <summary>Gets or sets the new user's id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets a value indicating whether the account was made.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets why it didn't work, if it didn't.</summary>
    public string? Message { get; set; }
}
