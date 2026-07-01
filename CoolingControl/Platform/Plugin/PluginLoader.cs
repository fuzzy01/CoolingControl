namespace CoolingControl.Platform;

using Serilog;

/// <summary>
/// Scans a directory for plugin DLLs and instantiates any <see cref="IPlatformAdapter"/>
/// implementations found inside them.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Loads all platform adapter plugins from <paramref name="pluginsDir"/>.
    /// Returns an empty dictionary if the directory does not exist.
    /// </summary>
    public static Dictionary<string, IPlatformAdapter> LoadAdapters(string pluginsDir, ConfigHelper config)
    {
        var result = new Dictionary<string, IPlatformAdapter>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(pluginsDir))
            return result;

        foreach (var dllPath in Directory.EnumerateFiles(pluginsDir, "*.dll"))
        {
            try
            {
                var loadContext = new PluginLoadContext(dllPath);
                var assembly = loadContext.LoadFromAssemblyPath(dllPath);

                var adapterTypes = assembly.GetTypes()
                    .Where(t => t is { IsAbstract: false, IsClass: true }
                             && typeof(IPlatformAdapter).IsAssignableFrom(t)
                             && t.GetCustomAttributes(typeof(PlatformAdapterAttribute), inherit: false)
                                 .Length > 0)
                    .ToList();

                if (adapterTypes.Count == 0)
                {
                    Log.Warning("Plugin {Dll} contains no types decorated with [PlatformAdapter]", Path.GetFileName(dllPath));
                    continue;
                }

                foreach (var type in adapterTypes)
                {
                    var attr = (PlatformAdapterAttribute)type
                        .GetCustomAttributes(typeof(PlatformAdapterAttribute), inherit: false)[0];
                    string platformName = attr.PlatformName;

                    if (result.ContainsKey(platformName))
                    {
                        Log.Warning("Duplicate platform name '{Name}' in {Dll} — skipping", platformName, Path.GetFileName(dllPath));
                        continue;
                    }

                    var adapter = Instantiate(type, config, dllPath);
                    if (adapter != null)
                    {
                        result[platformName] = adapter;
                        Log.Information("Loaded plugin adapter '{Name}' from {Dll}", platformName, Path.GetFileName(dllPath));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load plugin from {Dll}", Path.GetFileName(dllPath));
            }
        }

        return result;
    }

    private static IPlatformAdapter? Instantiate(Type type, ConfigHelper config, string dllPath)
    {
        // Prefer (ConfigHelper config) constructor
        var configCtor = type.GetConstructor([typeof(ConfigHelper)]);
        if (configCtor != null)
            return (IPlatformAdapter)configCtor.Invoke([config]);

        // Fall back to parameterless
        var defaultCtor = type.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
            return (IPlatformAdapter)defaultCtor.Invoke(null);

        Log.Error("Plugin type {Type} in {Dll} has no suitable constructor (expected parameterless or (ConfigHelper))",
            type.Name, Path.GetFileName(dllPath));
        return null;
    }
}
