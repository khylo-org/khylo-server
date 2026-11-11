using System.Threading.Tasks;
using Khylo.Api.Constants;
using Khylo.Api.Extensions;
using Khylo.Common.Configuration;
using Khylo.Common.Extensions;
using Khylo.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Khylo.Api.Auth.FirstTimeSetupPolicy
{
    /// <summary>
    /// Authorization handler for requiring first time setup or default privileges.
    /// </summary>
    public class FirstTimeSetupHandler : AuthorizationHandler<FirstTimeSetupRequirement>
    {
        private readonly IConfigurationManager _configurationManager;
        private readonly INetworkManager _networkManager;
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirstTimeSetupHandler" /> class.
        /// </summary>
        /// <param name="configurationManager">Instance of the <see cref="IConfigurationManager"/> interface.</param>
        /// <param name="networkManager">Instance of the <see cref="INetworkManager"/> interface.</param>
        /// <param name="httpContextAccessor">Instance of the <see cref="IHttpContextAccessor"/> interface.</param>
        public FirstTimeSetupHandler(IConfigurationManager configurationManager, INetworkManager networkManager, IHttpContextAccessor httpContextAccessor)
        {
            _configurationManager = configurationManager;
            _networkManager = networkManager;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <inheritdoc />
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FirstTimeSetupRequirement requirement)
        {
            // Until setup is finished there is no administrator yet, so these calls are open to whoever can reach the server.
            // Only this machine and the home network may do that: a server that is reachable from the internet before it
            // has been set up can't be taken over from outside.
            if (!_configurationManager.CommonConfiguration.IsStartupWizardCompleted)
            {
                if (IsFromHomeNetwork())
                {
                    context.Succeed(requirement);
                }
                else
                {
                    context.Fail();
                }
            }

            // Succeed if user is admin
            else if (context.User.IsInRole(UserRoles.Administrator))
            {
                context.Succeed(requirement);
            }

            // Fail if admin is required and user is not admin
            else if (requirement.RequireAdmin)
            {
                context.Fail();
            }

            // Succeed if admin is not required and user is not guest
            else if (context.User.IsInRole(UserRoles.User))
            {
                context.Succeed(requirement);
            }

            // Any user-specific checks are handled in the DefaultAuthorizationHandler.
            return Task.CompletedTask;
        }

        private bool IsFromHomeNetwork()
            => _httpContextAccessor.HttpContext is { } http && _networkManager.IsInLocalNetwork(http.GetNormalizedRemoteIP());
    }
}
