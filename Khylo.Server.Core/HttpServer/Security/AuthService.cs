#pragma warning disable CS1591

using System.Threading.Tasks;
using Khylo.Controller.Net;
using Khylo.Data;
using Khylo.Database.Implementations.Enums;
using Microsoft.AspNetCore.Http;

namespace Khylo.Server.Core.HttpServer.Security
{
    public class AuthService : IAuthService
    {
        private readonly IAuthorizationContext _authorizationContext;

        public AuthService(
            IAuthorizationContext authorizationContext)
        {
            _authorizationContext = authorizationContext;
        }

        public async Task<AuthorizationInfo> Authenticate(HttpRequest request)
        {
            var auth = await _authorizationContext.GetAuthorizationInfo(request).ConfigureAwait(false);

            if (!auth.HasToken)
            {
                return auth;
            }

            if (!auth.IsAuthenticated)
            {
                throw new SecurityException("Invalid token.");
            }

            if (auth.User?.HasPermission(PermissionKind.IsDisabled) ?? false)
            {
                throw new SecurityException("User account has been disabled.");
            }

            return auth;
        }
    }
}
