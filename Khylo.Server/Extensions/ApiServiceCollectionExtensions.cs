using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Khylo.Api.Auth;
using Khylo.Api.Auth.AnonymousLanAccessPolicy;
using Khylo.Api.Auth.DefaultAuthorizationPolicy;
using Khylo.Api.Auth.FirstTimeSetupPolicy;
using Khylo.Api.Auth.LocalAccessOrRequiresElevationPolicy;
using Khylo.Api.Auth.SyncPlayAccessPolicy;
using Khylo.Api.Auth.UserPermissionPolicy;
using Khylo.Api.Constants;
using Khylo.Api.Controllers;
using Khylo.Api.Formatters;
using Khylo.Api.ModelBinders;
using Khylo.Common.Api;
using Khylo.Common.Net;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Enums;
using Khylo.Extensions.Json;
using Khylo.Model.Entities;
using Khylo.Server.Configuration;
using Khylo.Server.Core;
using Khylo.Server.Filters;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using AuthenticationSchemes = Khylo.Api.Constants.AuthenticationSchemes;

namespace Khylo.Server.Extensions
{
    /// <summary>
    /// API specific extensions for the service collection.
    /// </summary>
    public static class ApiServiceCollectionExtensions
    {
        /// <summary>
        /// Adds khylo API authorization policies to the DI container.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddKhyloApiAuthorization(this IServiceCollection serviceCollection)
        {
            // The default handler must be first so that it is evaluated first
            serviceCollection.AddSingleton<IAuthorizationHandler, DefaultAuthorizationHandler>();
            serviceCollection.AddSingleton<IAuthorizationHandler, UserPermissionHandler>();
            serviceCollection.AddSingleton<IAuthorizationHandler, FirstTimeSetupHandler>();
            serviceCollection.AddSingleton<IAuthorizationHandler, AnonymousLanAccessHandler>();
            serviceCollection.AddSingleton<IAuthorizationHandler, SyncPlayAccessHandler>();
            serviceCollection.AddSingleton<IAuthorizationHandler, LocalAccessOrRequiresElevationHandler>();

            return serviceCollection.AddAuthorizationCore(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(AuthenticationSchemes.CustomAuthentication)
                    .AddRequirements(new DefaultAuthorizationRequirement())
                    .Build();

                options.AddPolicy(Policies.AnonymousLanAccessPolicy, new AnonymousLanAccessRequirement());
                options.AddPolicy(Policies.CollectionManagement, new UserPermissionRequirement(PermissionKind.EnableCollectionManagement));
                options.AddPolicy(Policies.Download, new UserPermissionRequirement(PermissionKind.EnableContentDownloading));
                options.AddPolicy(Policies.FirstTimeSetupOrDefault, new FirstTimeSetupRequirement(requireAdmin: false));
                options.AddPolicy(Policies.FirstTimeSetupOrElevated, new FirstTimeSetupRequirement());
                options.AddPolicy(Policies.FirstTimeSetupOrIgnoreParentalControl, new FirstTimeSetupRequirement(false, false));
                options.AddPolicy(Policies.IgnoreParentalControl, new DefaultAuthorizationRequirement(validateParentalSchedule: false));
                options.AddPolicy(Policies.LiveTvAccess, new UserPermissionRequirement(PermissionKind.EnableLiveTvAccess));
                options.AddPolicy(Policies.LiveTvManagement, new UserPermissionRequirement(PermissionKind.EnableLiveTvManagement));
                options.AddPolicy(Policies.LocalAccessOrRequiresElevation, new LocalAccessOrRequiresElevationRequirement());
                options.AddPolicy(Policies.SyncPlayHasAccess, new SyncPlayAccessRequirement(SyncPlayAccessRequirementType.HasAccess));
                options.AddPolicy(Policies.SyncPlayCreateGroup, new SyncPlayAccessRequirement(SyncPlayAccessRequirementType.CreateGroup));
                options.AddPolicy(Policies.SyncPlayJoinGroup, new SyncPlayAccessRequirement(SyncPlayAccessRequirementType.JoinGroup));
                options.AddPolicy(Policies.SyncPlayIsInGroup, new SyncPlayAccessRequirement(SyncPlayAccessRequirementType.IsInGroup));
                options.AddPolicy(Policies.SubtitleManagement, new UserPermissionRequirement(PermissionKind.EnableSubtitleManagement));
                options.AddPolicy(Policies.LyricManagement, new UserPermissionRequirement(PermissionKind.EnableLyricManagement));
                options.AddPolicy(
                    Policies.RequiresElevation,
                    policy => policy.AddAuthenticationSchemes(AuthenticationSchemes.CustomAuthentication)
                        .RequireClaim(ClaimTypes.Role, UserRoles.Administrator));
            });
        }

        /// <summary>
        /// Adds custom legacy authentication to the service collection.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static AuthenticationBuilder AddCustomAuthentication(this IServiceCollection serviceCollection)
        {
            return serviceCollection.AddAuthentication(AuthenticationSchemes.CustomAuthentication)
                .AddScheme<AuthenticationSchemeOptions, CustomAuthenticationHandler>(AuthenticationSchemes.CustomAuthentication, null);
        }

        /// <summary>
        /// Extension method for adding the Khylo API to the service collection.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        /// <param name="pluginAssemblies">An IEnumerable containing all plugin assemblies with API controllers.</param>
        /// <param name="config">The <see cref="NetworkConfiguration"/>.</param>
        /// <returns>The MVC builder.</returns>
        public static IMvcBuilder AddKhyloApi(this IServiceCollection serviceCollection, IEnumerable<Assembly> pluginAssemblies, NetworkConfiguration config)
        {
            IMvcBuilder mvcBuilder = serviceCollection
                .AddCors()
                .AddTransient<ICorsPolicyProvider, CorsPolicyProvider>()
                .Configure<ForwardedHeadersOptions>(options =>
                {
                    ConfigureForwardHeaders(config, options);
                })
                .AddMvc(opts =>
                {
                    // Allow requester to change between camelCase and PascalCase
                    opts.RespectBrowserAcceptHeader = true;

                    opts.OutputFormatters.Insert(0, new CamelCaseJsonProfileFormatter());
                    opts.OutputFormatters.Insert(0, new PascalCaseJsonProfileFormatter());

                    opts.OutputFormatters.Add(new CssOutputFormatter());
                    opts.OutputFormatters.Add(new XmlOutputFormatter());

                    opts.ModelBinderProviders.Insert(0, new NullableEnumModelBinderProvider());
                })

                // Clear app parts to avoid other assemblies being picked up
                .ConfigureApplicationPartManager(a => a.ApplicationParts.Clear())
                .AddApplicationPart(typeof(StartupController).Assembly)
                .AddJsonOptions(options =>
                {
                    // Update all properties that are set in JsonDefaults
                    var jsonOptions = JsonDefaults.PascalCaseOptions;

                    // From JsonDefaults
                    options.JsonSerializerOptions.ReadCommentHandling = jsonOptions.ReadCommentHandling;
                    options.JsonSerializerOptions.WriteIndented = jsonOptions.WriteIndented;
                    options.JsonSerializerOptions.DefaultIgnoreCondition = jsonOptions.DefaultIgnoreCondition;
                    options.JsonSerializerOptions.NumberHandling = jsonOptions.NumberHandling;

                    options.JsonSerializerOptions.Converters.Clear();
                    foreach (var converter in jsonOptions.Converters)
                    {
                        options.JsonSerializerOptions.Converters.Add(converter);
                    }

                    // From JsonDefaults.PascalCase
                    options.JsonSerializerOptions.PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy;
                });

            foreach (Assembly pluginAssembly in pluginAssemblies)
            {
                mvcBuilder.AddApplicationPart(pluginAssembly);
            }

            return mvcBuilder.AddControllersAsServices();
        }

        internal static void ConfigureForwardHeaders(NetworkConfiguration config, ForwardedHeadersOptions options)
        {
            // https://github.com/dotnet/aspnetcore/blob/master/src/Middleware/HttpOverrides/src/ForwardedHeadersMiddleware.cs
            // Enable debug logging on Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware to help investigate issues.

            if (config.KnownProxies.Length == 0)
            {
                // Khylo: proxies on this machine or the home network (the web app's server, a reverse proxy
                // on another box, a tunnel) are trusted to say who the real viewer is, hop by hop, so people
                // watching away from home are seen as away. Only hops added by those proxies are believed: the
                // first address from outside the home network ends the chain, so a viewer can't fake one.
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                options.KnownProxies.Add(System.Net.IPAddress.Loopback);
                options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
                foreach (var network in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7", "fe80::/10" })
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            }
            else
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                AddProxyAddresses(config, config.KnownProxies, options);
            }

            // Only set forward limit if we have some known proxies or some known networks.
            if (options.KnownProxies.Count != 0 || options.KnownIPNetworks.Count != 0)
            {
                options.ForwardLimit = null;
            }
        }

        /// <summary>
        /// Adds Swagger to the service collection.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddKhyloApiSwagger(this IServiceCollection serviceCollection)
        {
            return serviceCollection.AddSwaggerGen(c =>
            {
                var version = Khylo.Common.KhyloBuildInfo.Number;
                c.SwaggerDoc("api-docs", new OpenApiInfo
                {
                    Title = "Khylo API",
                    Version = version,
                    Extensions = new Dictionary<string, IOpenApiExtension>
                    {
                        {
                            "x-khylo-version",
                            new JsonNodeExtension(JsonValue.Create(version))
                        }
                    }
                });

                c.AddSecurityDefinition(AuthenticationSchemes.CustomAuthentication, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "Authorization",
                    Description = "API key header parameter"
                });

                // Add all xml doc files to swagger generator.
                var xmlFiles = Directory.EnumerateFiles(
                    AppContext.BaseDirectory,
                    "*.xml",
                    SearchOption.TopDirectoryOnly);

                foreach (var xmlFile in xmlFiles)
                {
                    c.IncludeXmlComments(xmlFile);
                }

                // Order actions by route path, then by http method.
                c.OrderActionsBy(description =>
                    $"{description.ActionDescriptor.RouteValues["controller"]}_{description.RelativePath}");

                // Use method name as operationId
                c.CustomOperationIds(
                    description =>
                    {
                        description.TryGetMethodInfo(out MethodInfo methodInfo);
                        // Attribute name, method name, none.
                        return description?.ActionDescriptor.AttributeRouteInfo?.Name
                               ?? methodInfo?.Name
                               ?? null;
                    });

                // Allow parameters to properly be nullable.
                c.UseAllOfToExtendReferenceSchemas();
                c.SupportNonNullableReferenceTypes();

                // TODO - remove when all types are supported in System.Text.Json
                c.AddSwaggerTypeMappings();

                c.SchemaFilter<IgnoreEnumSchemaFilter>();
                c.SchemaFilter<FlagsEnumSchemaFilter>();
                c.OperationFilter<RetryOnTemporarilyUnavailableFilter>();
                c.OperationFilter<SecurityRequirementsOperationFilter>();
                c.OperationFilter<FileResponseFilter>();
                c.OperationFilter<FileRequestFilter>();
                c.OperationFilter<ParameterObsoleteFilter>();
                c.DocumentFilter<AdditionalModelFilter>();
                c.DocumentFilter<SecuritySchemeReferenceFixupFilter>();
            })
            .Replace(ServiceDescriptor.Transient<ISwaggerProvider, CachingOpenApiProvider>());
        }

        private static void AddPolicy(this AuthorizationOptions authorizationOptions, string policyName, IAuthorizationRequirement authorizationRequirement)
        {
            authorizationOptions.AddPolicy(policyName, policy =>
            {
                policy.AddAuthenticationSchemes(AuthenticationSchemes.CustomAuthentication).AddRequirements(authorizationRequirement);
            });
        }

        /// <summary>
        /// Sets up the proxy configuration based on the addresses/subnets in <paramref name="allowedProxies"/>.
        /// </summary>
        /// <param name="config">The <see cref="NetworkConfiguration"/> containing the config settings.</param>
        /// <param name="allowedProxies">The string array to parse.</param>
        /// <param name="options">The <see cref="ForwardedHeadersOptions"/> instance.</param>
        internal static void AddProxyAddresses(NetworkConfiguration config, string[] allowedProxies, ForwardedHeadersOptions options)
        {
            for (var i = 0; i < allowedProxies.Length; i++)
            {
                if (IPAddress.TryParse(allowedProxies[i], out var addr))
                {
                    AddIPAddress(config, options, addr, addr.AddressFamily == AddressFamily.InterNetwork ? NetworkConstants.MinimumIPv4PrefixSize : NetworkConstants.MinimumIPv6PrefixSize);
                }
                else if (NetworkUtils.TryParseToSubnet(allowedProxies[i], out var subnet))
                {
                    AddIPAddress(config, options, subnet.Address, subnet.Subnet.PrefixLength);
                }
                else if (NetworkUtils.TryParseHost(allowedProxies[i], out var addresses, config.EnableIPv4, config.EnableIPv6))
                {
                    foreach (var address in addresses)
                    {
                        AddIPAddress(config, options, address, address.AddressFamily == AddressFamily.InterNetwork ? NetworkConstants.MinimumIPv4PrefixSize : NetworkConstants.MinimumIPv6PrefixSize);
                    }
                }
            }
        }

        private static void AddIPAddress(NetworkConfiguration config, ForwardedHeadersOptions options, IPAddress addr, int prefixLength)
        {
            if (addr.IsIPv4MappedToIPv6)
            {
                addr = addr.MapToIPv4();
            }

            if ((!config.EnableIPv4 && addr.AddressFamily == AddressFamily.InterNetwork) || (!config.EnableIPv6 && addr.AddressFamily == AddressFamily.InterNetworkV6))
            {
                return;
            }

            if ((addr.AddressFamily == AddressFamily.InterNetwork && prefixLength == NetworkConstants.MinimumIPv4PrefixSize) || (addr.AddressFamily == AddressFamily.InterNetworkV6 && prefixLength == NetworkConstants.MinimumIPv6PrefixSize))
            {
                options.KnownProxies.Add(addr);
            }
            else
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(addr, prefixLength));
            }
        }

        private static void AddSwaggerTypeMappings(this SwaggerGenOptions options)
        {
            /*
             * TODO remove when System.Text.Json properly supports non-string keys.
             * Used in BaseItemDto.ImageBlurHashes
             */
            options.MapType<Dictionary<ImageType, string>>(() =>
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String
                    }
                });

            // Support dictionary with nullable string value.
            options.MapType<Dictionary<string, string?>>(() =>
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String | JsonSchemaType.Null
                    }
                });

            // Swashbuckle doesn't use JsonOptions to describe responses, so we need to manually describe it.
            options.MapType<Version>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String
            });
        }
    }
}
