using Khylo.Controller.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// The address people reach this Khylo server at from anywhere, so a TV's sign-in QR code works on a
/// phone that isn't on the home network. Public: the TV asks before anyone has signed in.
/// </summary>
[Route("Khylo/WebAddress")]
public class KhyloWebAddressController : BaseKhyloApiController
{
    private readonly IServerConfigurationManager _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloWebAddressController"/> class.
    /// </summary>
    /// <param name="config">The server configuration.</param>
    public KhyloWebAddressController(IServerConfigurationManager config)
    {
        _config = config;
    }

    /// <summary>
    /// Gets the server's public web address, if it has one.
    /// </summary>
    /// <response code="200">The address (empty when not known).</response>
    /// <returns>The address.</returns>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<WebAddressDto> Get() => new WebAddressDto { Address = _config.Configuration.KhyloWebAddress?.Trim().TrimEnd('/') };

    /// <summary>
    /// The server's public web address.
    /// </summary>
    public class WebAddressDto
    {
        /// <summary>Gets or sets the address, for example https://khylo.example.com.</summary>
        public string? Address { get; set; }
    }
}
