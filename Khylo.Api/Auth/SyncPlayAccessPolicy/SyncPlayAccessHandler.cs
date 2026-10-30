using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Common.Extensions;
using Khylo.Controller.Library;
using Khylo.Controller.SyncPlay;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Enums;
using Khylo.Extensions;
using Microsoft.AspNetCore.Authorization;

namespace Khylo.Api.Auth.SyncPlayAccessPolicy
{
    /// <summary>
    /// Default authorization handler.
    /// </summary>
    public class SyncPlayAccessHandler : AuthorizationHandler<SyncPlayAccessRequirement>
    {
        private readonly ISyncPlayManager _syncPlayManager;
        private readonly IUserManager _userManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncPlayAccessHandler"/> class.
        /// </summary>
        /// <param name="syncPlayManager">Instance of the <see cref="ISyncPlayManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        public SyncPlayAccessHandler(
            ISyncPlayManager syncPlayManager,
            IUserManager userManager)
        {
            _syncPlayManager = syncPlayManager;
            _userManager = userManager;
        }

        /// <inheritdoc />
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SyncPlayAccessRequirement requirement)
        {
            var userId = context.User.GetUserId();
            if (userId.IsEmpty())
            {
                // Unauthenticated requests and API keys carry no user, so there is nothing to
                // check: leave the requirement unsatisfied and let the request be challenged.
                return Task.CompletedTask;
            }

            var user = _userManager.GetUserById(userId);
            if (user is null)
            {
                throw new ResourceNotFoundException();
            }

            if (requirement.RequiredAccess == SyncPlayAccessRequirementType.HasAccess)
            {
                if (user.SyncPlayAccess is SyncPlayUserAccessType.CreateAndJoinGroups or SyncPlayUserAccessType.JoinGroups
                    || _syncPlayManager.IsUserActive(userId))
                {
                    context.Succeed(requirement);
                }
            }
            else if (requirement.RequiredAccess == SyncPlayAccessRequirementType.CreateGroup)
            {
                if (user.SyncPlayAccess == SyncPlayUserAccessType.CreateAndJoinGroups)
                {
                    context.Succeed(requirement);
                }
            }
            else if (requirement.RequiredAccess == SyncPlayAccessRequirementType.JoinGroup)
            {
                if (user.SyncPlayAccess == SyncPlayUserAccessType.CreateAndJoinGroups
                    || user.SyncPlayAccess == SyncPlayUserAccessType.JoinGroups)
                {
                    context.Succeed(requirement);
                }
            }
            else if (requirement.RequiredAccess == SyncPlayAccessRequirementType.IsInGroup)
            {
                if (_syncPlayManager.IsUserActive(userId))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}
