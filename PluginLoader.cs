using DSRemapper;
using DSRemapper.Core;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using DSRemapper.Core.CDN;
using System.Text.Json;
using System.Dynamic;
using Microsoft.VisualBasic;

namespace DSRemapper.Framework
{
    internal class PluginInfo(FileInfo file, Manifest manifest)
    {
        public FileInfo File { get; init; } = file;
        public Manifest Manifest { get; init; } = manifest;
        public List<Assembly> Assemblies { get; init; } = [];

        public bool IsCompatible(Version core, Version framework) =>
            Manifest.IsSupportedByCore(core) && Manifest.IsSupportedByFramework(framework);

        public bool LoadPlugin(Version core, Version framework, out SortedList<string, byte[]> images, DSRLogger? logger = null, string? pluginId = null)
        {
            images = [];
            pluginId ??= File.FullName;
            if (!IsCompatible(core, framework))
                return false;

            using ZipArchive za = ZipFile.Open(File.FullName, ZipArchiveMode.Read);
            foreach (var ent in za.Entries)
            {
                if (ent.Name.EndsWith(".dll"))
                {
                    try
                    {
                        using Stream s = ent.Open();
                        using MemoryStream ms = new();
                        s.CopyTo(ms);
                        Assemblies.Add(Assembly.Load(ms.ToArray()));
                        logger?.LogInformation($"Assembly found: {pluginId}/{ent.FullName}");
                    }
                    catch
                    {
                        logger?.LogWarning($"Unable to load {pluginId}/{ent.FullName}");
                    }
                }
                else if (ent.Name.EndsWith(".png") || ent.Name.EndsWith(".jpg")) // Passed to the calling method to proper storage
                {
                    try
                    {
                        using Stream s = ent.Open();
                        using MemoryStream ms = new();
                        s.CopyTo(ms);
                        images.Add($"{ent.FullName}", ms.ToArray());
                        logger?.LogInformation($"Controller image found: {pluginId}/{ent.FullName}");
                    }
                    catch
                    {
                        logger?.LogWarning($"Unable to load {pluginId}/{ent.FullName}");
                    }
                }
                else if (ent.Name.EndsWith(".ndll")) // fully handled here (not clean, but easier)
                {
                    string fileName = $"{ent.Name[..^5]}.dll";
                    logger?.LogInformation($"Native dll found: {pluginId}/{ent.FullName}");
                    if (!System.IO.File.Exists(fileName))
                    {
                        logger?.LogInformation($"Installing native dll: {Path.GetFullPath(fileName)}");
                        ent.ExtractToFile(fileName);
                    }
                    else
                        logger?.LogInformation($"Native dll currently installed: {Path.GetFullPath(fileName)}");

                }
            }

            return true;
        }
    }
    
    /// <summary>
    /// Class used for loading all DSRemapper assemblies and plugins
    /// </summary>
    public static class PluginLoader
    {
        private static Version GetAssemblyVersion(string name) =>
            AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) ?? false)
            .GetName().Version!;
        /// <summary>
        /// Returns the current version of the <see cref="DSRemapper.Core"/>
        /// </summary>
        public static Version CoreVersion { get; private set; } = GetAssemblyVersion("DSRemapper.Core");
        /// <summary>
        /// Returns the current version of the <see cref="DSRemapper.Framework"/>
        /// </summary>
        public static Version FrameworkVersion { get; private set; } = GetAssemblyVersion("DSRemapper.Framework");

        private static readonly DSRLogger logger = DSRLogger.GetLogger("DSRemapper.PluginLoader");
        private static readonly List<Assembly> pluginAssemblies = [];
        /// <summary>
        /// Output plugins list, sorted by Emulated controller path
        /// </summary>
        public readonly static SortedList<string, ConstructorInfo> OutputPlugins = [];
        /// <summary>
        /// Remapper plugins list, sorted by remapper asigned file extension
        /// </summary>
        public readonly static SortedList<string, ConstructorInfo> RemapperPlugins = [];
        /// <summary>
        /// Physical devices scanner plugins (input plugins), sorted by namespace and class name
        /// </summary>
        public readonly static SortedList<string, IDSRDeviceScanner> Scanners = [];
        /// <summary>
        /// Controllers images files as byte arrays
        /// </summary>
        public readonly static SortedList<string, byte[]> ControllerImages = [];
        /// <summary>
        /// Contains a list of all plugin files
        /// </summary>
        internal readonly static Dictionary<string, PluginInfo> Plugins = [];
        /// <summary>
        /// Returns the list with the manifests of all loaded plugins
        /// </summary>
        public static List<Manifest> PluginManifests => Plugins.Values.Select(p => p.Manifest).ToList();
        /// <summary>
        /// Returns the list of all the static 'PluginFree' methods encountered for each plugin.
        /// </summary>
        public readonly static List<Action> PluginFreeMethods = [];

        private static Assembly? PluginAssemblyResolverEventHandler(object? sender, ResolveEventArgs args)
        {
            string assemblyName=args.Name.Split(",")[0];
            Assembly? asm = pluginAssemblies.Find(a => (a.FullName ?? "").Equals(args.Name));
            asm ??= pluginAssemblies.Find(a => (a.FullName?.Split(",")[0] ?? "").Equals(assemblyName));
            return asm;
        }

        static PluginLoader()
        {
            AppDomain.CurrentDomain.AssemblyResolve += PluginAssemblyResolverEventHandler;
        }

        private static readonly HashSet<string> pluginExtensions = new(StringComparer.OrdinalIgnoreCase) { ".dsrp", ".zip" };

        /// <summary>
        /// Retrives all the plugin files that are located in the <see cref="DSRPaths.PluginsPath"/>
        /// </summary>
        /// <returns>List of plugin files</returns>
        public static FileInfo[] GetPluginFiles =>
            [..DSRPaths.PluginsPath.GetFiles("*.*", SearchOption.AllDirectories)
                .Where((f)=> pluginExtensions.Contains(f.Extension))];

        /// <summary>
        /// Recognice all plugin files available on the plugins folder
        /// </summary>
        public static void RegisterPlugins()
        {
            foreach (FileInfo file in GetPluginFiles)
            {
                logger.LogInformation($"Scanning file: {file.FullName}");
                //string pluginId = Path.GetRelativePath(DSRPaths.PluginsPath.FullName, file.FullName);
                ZipArchive archive = new(file.OpenRead());
                ZipArchiveEntry? manifestEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("manifest.json"));
                if (manifestEntry != null)
                {
                    using Stream manifestStream = manifestEntry.Open();
                    Manifest? manifest = Manifest.FromJson(manifestStream);
                    archive.Dispose();
                    if (manifest != null)
                    {
                        if (Plugins.TryAdd(manifest.Name, new PluginInfo(file, manifest)))
                            logger.LogInformation($"Plugin found: {manifest.Name} ({manifest.Version}, Core: {manifest.CoreVersion}, Framework: {manifest.FrameworkVersion})");
                        else
                            logger.LogWarning($"Plugin is duplicated: {manifest.Name} ({manifest.Version}, Core: {manifest.CoreVersion}, Framework: {manifest.FrameworkVersion})");
                    }
                    else
                        logger.LogWarning($"Invalid plugin manifest: {file.FullName}");
                }
                else
                    logger.LogWarning($"Plugin manifest not found: {file.FullName}");
            }
        }
        
        /// <summary>
        /// Loads all the assemblies available on the registered plugins that are compatible
        /// </summary>
        public static void LoadPluginAssemblies()
        {
            foreach ((string pluginId, PluginInfo info) in Plugins)
            {
                if (!info.LoadPlugin(CoreVersion, FrameworkVersion, out SortedList<string, byte[]> images, logger))
                {
                    logger.LogWarning($"Plugin is not compatible >>> Current Version: (Core: {CoreVersion}, Framework: {FrameworkVersion}) >>> Plugin Version (Version: {info.Manifest.Version}, Core: {info.Manifest.CoreVersion}, Framework: {info.Manifest.FrameworkVersion})");
                    continue;
                }

                logger.LogInformation($"Plugin loaded: {info.Manifest.Name} (Version: {info.Manifest.Version}, Core: {info.Manifest.CoreVersion}, Framework: {info.Manifest.FrameworkVersion})");
                foreach ((string key, byte[] value) in images)
                    ControllerImages.Add(key, value);
            }
            pluginAssemblies.AddRange(Plugins.Values.SelectMany(p => p.Assemblies));
        }
        /// <summary>
        /// Returns the byte array of a image from a input controller
        /// </summary>
        /// <param name="ctrl">An input controller to get the image from</param>
        /// <returns>The image as a byte array</returns>
        public static byte[] GetControllerImage(IDSRInputController ctrl)
        {
            string imgPath = $"{ctrl.ImgPath}";
            //string imgPath = $"{GetPluginPath(ctrl.GetType().Assembly)}/{ctrl.ImgPath}";
            if (ControllerImages.TryGetValue(imgPath, out byte[]? img))
                return img;

            return [];
        }

        /// <summary>
        /// Find and update all static lists of this class using the assemblies loaded by 'LoadPluginAssemblies' function
        /// </summary>
        public static void LoadPlugins()
        {
            IEnumerable<Type> types = pluginAssemblies.SelectMany(a => a.GetTypes());
            foreach (Type type in types)
            {
                if (type.IsInterface || !type.IsVisible)
                    continue;

                if (type.IsAssignableTo(typeof(IDSROutputController))) // Output Plugin
                {
                    PluginFreeMethods.Add(type.GetMethod("PluginFree", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<Action>() ?? (() => { }));

                    string? path = type.GetCustomAttribute<EmulatedControllerAttribute>()?.DevicePath;
                    if (path != null)
                    {
                        //ConstructorInfo? staticCtr = type.GetConstructor(BindingFlags.NonPublic | BindingFlags.Static, Type.EmptyTypes);
                        //logger.LogInformation($"Output plugin static constructor: {staticCtr != null} >> {type.FullName}.{staticCtr?.Name}()");
                        type.TypeInitializer?.Invoke(null, null);

                        ConstructorInfo? ctr = type.GetConstructor(Type.EmptyTypes);
                        if (ctr != null)
                        {
                            if (OutputPlugins.TryAdd(path, ctr))
                                logger.LogInformation($"Output plugin found: {type.FullName}");
                            else
                                logger.LogWarning($"Output plugin is duplicated: {type.FullName}");
                        }
                        else
                            logger.LogWarning($"{type.FullName}: Output plugin doesn't have a public parameterless constructor");
                    }
                    else
                        logger.LogWarning($"{type.FullName}: Output plugin doesn't have a path assigned");
                }
                else if (type.IsAssignableTo(typeof(IDSRemapper))) // Remapper Plugin
                {
                    PluginFreeMethods.Add(type.GetMethod("PluginFree", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<Action>() ?? (() => { }));
                    string[]? fileExts = type.GetCustomAttribute<RemapperAttribute>()?.FileExts;
                    if (fileExts != null)
                    {
                        ConstructorInfo? ctr = type.GetConstructor([typeof(IDSROutput), typeof(DSRLogger)]);
                        if (ctr != null)
                        {
                            foreach (string fileExt in fileExts)
                            {
                                if (RemapperPlugins.TryAdd(fileExt, ctr))
                                {
                                    logger.LogInformation($"Remapper plugin for \".{fileExt}\" files found: {type.FullName}");
                                }
                                else
                                    logger.LogError($"{type.FullName}: Remapper plugin for extension \"{fileExt}\" is already loaded");
                            }
                        }
                        else
                            logger.LogWarning($"{type.FullName}: Remapper plugin doesn't have a public suitable constructor");
                    }
                    else
                        logger.LogWarning($"{type.FullName}: Remapper plugin doesn't have a file extension assigned");
                }
                else if (type.IsAssignableTo(typeof(IDSRDeviceScanner))) // Input Plugin
                {
                    PluginFreeMethods.Add(type.GetMethod("PluginFree", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<Action>() ?? (() => { }));
                    ConstructorInfo? ctr = type.GetConstructor(Type.EmptyTypes);
                    if (ctr != null)
                    {
                        if (Scanners.TryAdd(type.FullName ?? "Unknown", (IDSRDeviceScanner)ctr.Invoke(null)))
                            logger.LogInformation($"Scanner plugin found: {type.FullName}");
                        else
                            logger.LogWarning($"Scanner plugin is duplicated: {type.FullName}");
                    }
                    else
                        logger.LogWarning($"{type.FullName}: Scanner plugin doesn't have a public parameterless constructor");
                }
            }
        }
        /// <summary>
        /// Calls all registered PluginFree methods in the loaded plugins to free any unmanaged resources allocated by the plugins.
        /// </summary>
        public static void FreeAllPlugins()
        {
            foreach(Action freeMethod in PluginFreeMethods)
                freeMethod();
        }
    }
}
