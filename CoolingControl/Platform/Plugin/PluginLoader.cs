namespace CoolingControl.Platform;

using Serilog;

/// <summary>
/// Scans a directory of per-plugin subdirectories and instantiates any <see cref="IPlatformAdapter"/>
/// implementations found inside them.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Loads all platform adapter plugins from the immediate subdirectories of <paramref name="pluginsDir"/>.
    /// Each plugin is expected to live in its own subdirectory (e.g. <c>plugins/MyPlugin/MyPlugin.dll</c>)
    /// so that its private dependency DLLs stay isolated from other plugins.
    /// Returns an empty dictionary if the directory does not exist.
    /// </summary>
    public static Dictionary<string, IPlatformAdapter> LoadAdapters(string pluginsDir, ConfigHelper config)
    {
        var result = new Dictionary<string, IPlatformAdapter>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(pluginsDir))
            return result;

        Log.Information("Loading platform adapter plugins from {Dir}", Path.GetFullPath(pluginsDir));

        foreach (var pluginDir in Directory.EnumerateDirectories(pluginsDir))
        {
            var pluginName = Path.GetFileName(pluginDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var dllPath = GetPluginDll(pluginDir, pluginName);
            if (dllPath == null)
            {
                Log.Warning("Plugin directory {Dir} has no matching {ExpectedDll} — skipping", pluginName, pluginName + ".dll");
                continue;
            }

            LoadFromDll(Path.GetFullPath(dllPath), config, result);
        }

        return result;
    }

    /// <summary>
    /// Resolves the single DLL to reflect over for a plugin subdirectory: the DLL named after the
    /// subdirectory itself (the convention our build targets follow), e.g. <c>plugins/MyPlugin/MyPlugin.dll</c>.
    /// Any other DLLs alongside it are treated as private dependencies and are left for the
    /// <see cref="PluginLoadContext"/> resolver to load on demand rather than being eagerly reflected over.
    /// Returns <c>null</c> if no such DLL exists.
    /// </summary>
    private static string? GetPluginDll(string pluginDir, string pluginName)
    {
        var conventionalDll = Path.Combine(pluginDir, pluginName + ".dll");
        return File.Exists(conventionalDll) ? conventionalDll : null;
    }

    private static void LoadFromDll(string dllPath, ConfigHelper config, Dictionary<string, IPlatformAdapter> result)
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
                return;
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
