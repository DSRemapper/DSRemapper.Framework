using DSRemapper.Core;

namespace DSRemapper.Framework
{
    /// <summary>
    /// This class manage all remap profiles
    /// </summary>
    public static class ProfileManager
    {
        /// <summary>
        /// Get all the files inside the DSRemapper plugins folder and subfolders
        /// </summary>
        /// <returns>An array containing all remap profile files</returns>
        public static string[] GetProfiles() =>
            Directory.GetFiles(DSRPaths.ProfilesPath, "*.*", SearchOption.AllDirectories)
            .Select((f) => Path.GetRelativePath(DSRPaths.ProfilesPath, f)).ToArray();
    }
}
