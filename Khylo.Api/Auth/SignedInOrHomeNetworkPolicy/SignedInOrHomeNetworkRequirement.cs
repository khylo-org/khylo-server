using Microsoft.AspNetCore.Authorization;

namespace Khylo.Api.Auth.SignedInOrHomeNetworkPolicy
{
    /// <summary>
    /// For things a player asks for without being able to sign in (a subtitle file, a font): anyone on the home network, or
    /// anyone from anywhere who carries a valid sign-in. Someone on the internet with no sign-in is turned away.
    /// </summary>
    public class SignedInOrHomeNetworkRequirement : IAuthorizationRequirement
    {
    }
}
