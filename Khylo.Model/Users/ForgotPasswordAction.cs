#pragma warning disable CS1591

using System;

namespace Khylo.Model.Users
{
    public enum ForgotPasswordAction
    {
        [Obsolete("Returning different actions represents a security concern.")]
        ContactAdmin = 0,
        PinCode = 1,
        [Obsolete("Returning different actions represents a security concern.")]
        InNetworkRequired = 2
    }
}
