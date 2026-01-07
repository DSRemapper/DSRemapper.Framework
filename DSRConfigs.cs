using DSRemapper.Core;
using System.Text.Json;

namespace DSRemapper.Framework
{
    /// <summary>
    /// Physical controller DSRemapper config class. Manage all configs associated with physical devices inside DSRemapper.
    /// </summary>
    /// <param name="id">Physical controller id for the config object</param>
    [Serializable]
    public class RemapperConfig(string id)
    {
        /// <summary>
        /// Physical controller unique id
        /// </summary>
        public string Id { get; set; } = id;
        /// <summary>
        /// Auto connect field value. If is true, physical controller is automatically connected, when plugged, for remapping.
        /// </summary>
        public bool AutoConnect { get; set; } = false;
        /// <summary>
        /// Las remap profile associated with the physical controller
        /// </summary>
        public string LastProfile { get; set; } = "";
    }
    /// <summary>
    /// Manage saveand load of the physical controllers configs
    /// </summary>
    public static class DSRConfigs
    {
        private static readonly FileInfo configPath = DSRPaths.ConfigPath.GetFile("DSConfigs.json");
        private static readonly List<RemapperConfig> remapperConfigs = [];

        /// <summary>
        /// DSRConfigs static contructor
        /// </summary>
        static DSRConfigs()
        {
            if (configPath.Exists)
                remapperConfigs = LoadConfigFile();
        }
        private static List<RemapperConfig> LoadConfigFile() => JsonSerializer
            .Deserialize<List<RemapperConfig>>(File.ReadAllText(configPath.FullName)) ?? [];
        private static void SaveConfigFile() =>
            File.WriteAllText(configPath.FullName, JsonSerializer.Serialize(remapperConfigs));
        /// <summary>
        /// Gets the controller config associated with the id.
        /// </summary>
        /// <param name="id">Id of a physical controller</param>
        /// <returns></returns>
        public static RemapperConfig GetConfig(string id)
        {
            if (!remapperConfigs.Exists((c) => c.Id.Equals(id)))
                remapperConfigs.Add(new RemapperConfig(id));

            return remapperConfigs.Find((c) => c.Id.Equals(id))!;
        }
        /// <summary>
        /// Sets the controller config associated with the id.
        /// </summary>
        /// <param name="config">The new config for the controller</param>
        /// <param name="id">Id of a physical controller</param>
        /// <returns></returns>
        public static void SetConfig(RemapperConfig config,string id)
        {
            int index = remapperConfigs.IndexOf(remapperConfigs.Find((c) => c.Id.Equals(id))!);
            if (index < 0)
                remapperConfigs.Add(config);
            else
                remapperConfigs[index] = config;
            SaveConfigFile();
        }

        /// <summary>
        /// Sets the last profile field for the physical controller associated with the id.
        /// </summary>
        /// <param name="id">Id of a physical controller</param>
        /// <param name="profile">Profile path (relative to DSRemapper Profiles folder) of the last assigned profile</param>
        public static void SetLastProfile(string id, string profile)
        {
            GetConfig(id).LastProfile = profile;
            SaveConfigFile();
        }
        /// <summary>
        /// Sets the auto connect field for the physical controller associated with the id.
        /// </summary>
        /// <param name="id">Id of a physical controller</param>
        /// <param name="autoConnect">True if the controller has to be connected as soon as it is plugged in</param>
        public static void SetAutoConnect(string id, bool autoConnect)
        {
            GetConfig(id).AutoConnect = autoConnect;
            SaveConfigFile();
        }
    }
}
