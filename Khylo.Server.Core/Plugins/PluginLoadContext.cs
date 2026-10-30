using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Khylo.Server.Core.Plugins;

/// <summary>
/// A custom <see cref="AssemblyLoadContext"/> for loading plugins.
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoadContext"/> class.
    /// </summary>
    /// <param name="path">The path of the plugin assembly.</param>
    public PluginLoadContext(string path) : base(true)
    {
        _resolver = new AssemblyDependencyResolver(path);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Plugins built for the original server ask for its libraries by their old names; hand them
        // the server's own copies, which carry Khylo names but the same types
        if (assemblyName.Name is { } name && name.StartsWith("Khylo.", StringComparison.Ordinal))
        {
            try
            {
                return Default.LoadFromAssemblyName(new AssemblyName("Khylo." + name["Khylo.".Length..]));
            }
            catch (FileNotFoundException)
            {
                // Not one of the server's libraries; let the plugin bring its own
            }
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is not null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }
}
