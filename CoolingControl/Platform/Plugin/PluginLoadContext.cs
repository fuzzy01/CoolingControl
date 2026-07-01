namespace CoolingControl.Platform;

using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Isolates a plugin assembly's dependencies while sharing types already loaded in the host.
/// This prevents double-loading of shared assemblies (e.g. CoolingControl itself) which would
/// cause type-identity mismatches when casting to <see cref="IPlatformAdapter"/>.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Prefer an already-loaded assembly from the host so shared types stay identical.
        var hostAssembly = Default.Assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        if (hostAssembly != null)
            return hostAssembly;

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }
}
