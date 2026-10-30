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
/// What someone opening an invitation link sees before signing up.
/// </summary>
public class InvitationDetails
{
    /// <summary>Gets or sets the invitation code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets when the invitation expires.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets a value indicating whether the invitation can still be used.</summary>
    public bool IsValid { get; set; }

    /// <summary>Gets or sets the server's name.</summary>
    public string ServerName { get; set; } = string.Empty;
}
