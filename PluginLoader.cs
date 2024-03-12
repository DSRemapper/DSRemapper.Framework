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

namespace DSRemapper.Framework
{    
    /// <summary>
    /// Class used for loading all DSRemapper assemblies and plugins
    /// </summary>
    public class PluginLoader
    {
        private static readonly DSRLogger logger = DSRLogger.GetLogger("DSRemapper.PluginLoader");
        private static readonly List<Assembly> pluginAssemblies = [];
        private static readonly Dictionary<Assembly, string> pluginSource = [];
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

        /// <summary>
        /// Loads the assemblies inside the DSRemapper Plugins folder and subfolders
        /// </summary>
        public static void LoadPluginAssemblies()
        {
            string[] plugins = [..Directory.GetFiles(DSRPaths.PluginsPath, "*.*", SearchOption.AllDirectories)
                .Where((f)=>f.EndsWith(".zip")||f.EndsWith(".dsrp"))];
            foreach (string plugin in plugins)
            {
                ZipArchive za = ZipFile.Open(plugin, ZipArchiveMode.Read);
                foreach (var ent in za.Entries)
                {
                    if (ent.Name.EndsWith(".dll"))
                    {
                        try
                        {
                            Stream s = ent.Open();
                            MemoryStream ms = new();
                            s.CopyTo(ms);
                            Assembly a = Assembly.Load(ms.ToArray());
                            ms.Close();
                            s.Close();
                            pluginAssemblies.Add(a);
                            pluginSource.Add(a, plugin);

                            logger.LogInformation($"Assembly found: {Path.GetRelativePath(DSRPaths.PluginsPath, plugin)}/{ent.FullName}");
                        }
                        catch
                        {
                            logger.LogWarning($"Unable to load {Path.GetRelativePath(DSRPaths.PluginsPath, plugin)}/{ent.FullName}");
                        }
                    }
                    else if (ent.Name.EndsWith(".png") || ent.Name.EndsWith(".jpg"))
                    {
                        try
                        {
                            Stream s = ent.Open();
                            MemoryStream ms = new();
                            s.CopyTo(ms);
                            ControllerImages.Add($"{plugin}/{ent.FullName}", ms.ToArray());
                            ms.Close();
                            s.Close();
                            logger.LogInformation($"Controller image found: {Path.GetRelativePath(DSRPaths.PluginsPath, plugin)}/{ent.FullName}");
                        }
                        catch
                        {
                            logger.LogWarning($"Unable to load {Path.GetRelativePath(DSRPaths.PluginsPath, plugin)}/{ent.FullName}");
                        }
                    }
                    else if (ent.Name.EndsWith(".ndll"))
                    {
                        string fileName = $"{ent.Name[..^5]}.dll";
                        logger.LogInformation($"Native dll found: {Path.GetRelativePath(DSRPaths.PluginsPath, plugin)}/{ent.FullName}");
                        if (!File.Exists(fileName))
                        {
                            logger.LogInformation($"Installing native dll: {Path.GetFullPath(fileName)}");
                            ent.ExtractToFile(fileName);
                        }
                        else
                            logger.LogInformation($"Native dll currently installed: {Path.GetFullPath(fileName)}");

                    }
                }
            }
        }
        /// <summary>
        /// Returns the plugin file path corresponding to the assembly.
        /// </summary>
        /// <param name="asm">An assembly corresponding to a plugin</param>
        /// <returns>The plugin file path, null if the assembly is not from a plugin</returns>
        public static string? GetPluginPath(Assembly asm)
        {
            if (pluginSource.TryGetValue(asm, out string? file))
                return file;
            return null;
        }
        /// <summary>
        /// Returns the byte array of a image from a input controller
        /// </summary>
        /// <param name="ctrl">An input controller to get the image from</param>
        /// <returns>The image as a byte array</returns>
        public static byte[] GetControllerImage(IDSRInputController ctrl)
        {
            string imgPath = $"{GetPluginPath(ctrl.GetType().Assembly)}/{ctrl.ImgPath}";
            if (ControllerImages.TryGetValue(imgPath, out byte[]? img))
                return img;

            return [];
        }

        /// <summary>
        /// Loads the assemblies inside the DSRemapper Plugins folder and subfolders
        /// </summary>
        public static void LoadPluginAssembliesLegacy()
        {
            string[] plugins = Directory.GetFiles(DSRPaths.PluginsPath, "*.dll", SearchOption.AllDirectories);
            foreach (string plugin in plugins)
            {
                try
                {
                    pluginAssemblies.Add(Assembly.LoadFrom(plugin));
                    logger.LogInformation($"Assembly found: {Path.GetRelativePath(DSRPaths.PluginsPath, plugin)}");
                }
                catch
                {
                    logger.LogWarning($"Unable to load {Path.GetRelativePath(DSRPaths.PluginsPath, plugin)}");
                }
            }
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

                if (type.IsAssignableTo(typeof(IDSROutputController)))
                {
                    string? path = type.GetCustomAttribute<EmulatedControllerAttribute>()?.DevicePath;
                    if (path != null)
                    {
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
                else if (type.IsAssignableTo(typeof(IDSRemapper)))
                {
                    string[]? fileExts = type.GetCustomAttribute<RemapperAttribute>()?.FileExts;
                    if (fileExts != null)
                    {
                        ConstructorInfo? ctr = type.GetConstructor(Type.EmptyTypes);
                        if (ctr != null) {
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
                            logger.LogWarning($"{type.FullName}: Remapper plugin doesn't have a public parameterless constructor");
                    }
                    else
                        logger.LogWarning($"{type.FullName}: Remapper plugin doesn't have a file extension assigned");
                }
                else if (type.IsAssignableTo(typeof(IDSRDeviceScanner)))
                {
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
    }
}
