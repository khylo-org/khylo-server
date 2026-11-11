using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Khylo.Controller.Configuration;
using Khylo.Model.Configuration;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Khylo.Server.Configuration
{
    /// <summary>
    /// Cors policy provider.
    /// </summary>
    public class CorsPolicyProvider : ICorsPolicyProvider
    {
        private readonly IServerConfigurationManager _serverConfigurationManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="CorsPolicyProvider"/> class.
        /// </summary>
        /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        public CorsPolicyProvider(IServerConfigurationManager serverConfigurationManager)
        {
            _serverConfigurationManager = serverConfigurationManager;
        }

        /// <summary>
        /// The websites that may talk to this server from a browser: the server's own web and home addresses, and the
        /// hosts listed in the CORS settings. A single "*" (the factory setting) doesn't mean every website, so a page
        /// from anywhere else can't use a visitor's browser to reach the server.
        /// </summary>
        /// <param name="configuration">The server configuration.</param>
        /// <returns>The allowed origins, such as https://khylo.example.com.</returns>
        public static string[] AllowedOrigins(ServerConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var origins = new List<string>();
            foreach (var host in configuration.CorsHosts ?? [])
            {
                if (!string.Equals(host?.Trim(), CorsConstants.AnyOrigin, StringComparison.Ordinal) && OriginOf(host) is { } listed)
                {
                    origins.Add(listed);
                }
            }

            foreach (var address in new[] { configuration.KhyloWebAddress, configuration.KhyloHomeAddress })
            {
                if (OriginOf(address) is { } own)
                {
                    origins.Add(own);
                }
            }

            return origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <inheritdoc />
        public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
        {
            var builder = new CorsPolicyBuilder()
                .AllowAnyMethod()
                .AllowAnyHeader();

            // With nothing allowed the policy lets no other website in (the server's own pages never need it)
            var origins = AllowedOrigins(_serverConfigurationManager.Configuration);
            if (origins.Length > 0)
            {
                builder.WithOrigins(origins)
                    .AllowCredentials();
            }

            return Task.FromResult<CorsPolicy?>(builder.Build());
        }

        private static string? OriginOf(string? address)
        {
            var text = address?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (!text.Contains("://", StringComparison.Ordinal))
            {
                text = "https://" + text;
            }

            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? uri.GetLeftPart(UriPartial.Authority)
                : null;
        }
    }
}
