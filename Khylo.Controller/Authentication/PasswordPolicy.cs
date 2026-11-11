using System;
using System.Collections.Frozen;
using System.Linq;

namespace Khylo.Controller.Authentication
{
    /// <summary>
    /// What a new password has to be. Checked wherever a person (or an administrator for them) chooses a password; the
    /// passwords Khylo makes for itself, and the blank password an administrator's reset leaves, don't go through it.
    /// </summary>
    public static class PasswordPolicy
    {
        /// <summary>
        /// The fewest characters a password may have.
        /// </summary>
        public const int MinimumLength = 8;

        private static readonly FrozenSet<string> _tooCommon = new[]
        {
            "password", "password1", "password12", "password123", "passw0rd", "p@ssw0rd", "p@ssword", "12345678", "123456789",
            "1234567890", "123123123", "11111111", "00000000", "87654321", "qwertyui", "qwerty123", "qwertyuiop", "1q2w3e4r",
            "1qaz2wsx", "abc12345", "abcd1234", "iloveyou", "letmein1", "welcome1", "admin123", "administrator", "changeme",
            "khylo123", "khylopassword", "mediaserver"
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>
        /// Says what is wrong with a password, in words the person can act on.
        /// </summary>
        /// <param name="password">The password they chose.</param>
        /// <param name="username">The account's name, which the password may not be.</param>
        /// <returns>The problem, or null when the password is fine.</returns>
        public static string? Problem(string? password, string? username = null)
        {
            if (string.IsNullOrEmpty(password) || password.All(char.IsWhiteSpace))
            {
                return "Choose a password.";
            }

            if (password.Length < MinimumLength)
            {
                return "Passwords need at least 8 characters.";
            }

            var lower = password.ToLowerInvariant();
            if (_tooCommon.Contains(lower)
                || lower.Distinct().Count() == 1
                || (!string.IsNullOrEmpty(username) && string.Equals(lower, username.ToLowerInvariant(), StringComparison.Ordinal)))
            {
                return "That password is too easy to guess. Choose another.";
            }

            return null;
        }
    }
}
