using System.Threading.Tasks;
using Khylo.Common.Extensions;
using Khylo.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Khylo.Api.Auth.SignedInOrHomeNetworkPolicy
{
    /// <summary>
    /// Authorization handler for <see cref="SignedInOrHomeNetworkRequirement"/>.
    /// </summary>
    public class SignedInOrHomeNetworkHandler : AuthorizationHandler<SignedInOrHomeNetworkRequirement>
    {
        private readonly INetworkManager _networkManager;
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="SignedInOrHomeNetworkHandler"/> class.
        /// </summary>
        /// <param name="networkManager">Instance of the <see cref="INetworkManager"/> interface.</param>
        /// <param name="httpContextAccessor">Instance of the <see cref="IHttpContextAccessor"/> interface.</param>
        public SignedInOrHomeNetworkHandler(INetworkManager networkManager, IHttpContextAccessor httpContextAccessor)
        {
            _networkManager = networkManager;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <inheritdoc />
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SignedInOrHomeNetworkRequirement requirement)
        {
            var http = _httpContextAccessor.HttpContext;

            // A valid sign-in is enough; so is being on the home network (loopback is part of it)
            if (context.User.Identity?.IsAuthenticated == true || http is null || _networkManager.IsInLocalNetwork(http.GetNormalizedRemoteIP()))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }
    }
}
