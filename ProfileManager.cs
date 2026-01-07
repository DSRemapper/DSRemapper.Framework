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
            DSRPaths.ProfilesPath.GetFiles("*.*", SearchOption.AllDirectories)
                .Where(f => f.FullName.StartsWith(DSRPaths.ProfilesPath.FullName, StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(DSRPaths.ProfilesPath.FullName, f.FullName)).ToArray();
        /// <summary>
        /// Resolves a profile file from it's relative path from the profiles folder
        /// </summary>
        /// <param name="profile">The relative path to the profile file. If it's null, empry of whitespaces only the funtion returns false and the <see cref="FileInfo"/> as null.</param>
        /// <param name="file">The <see cref="FileInfo"/> of the profile, whether it exists or not. If it is not inside the profile folder returns null.</param>
        /// <returns>True if the profile file exists and it's inside the profiles folder</returns>
        public static bool TryGetProfile(string profile, out FileInfo? file)
        {
            file = null;
            if (string.IsNullOrWhiteSpace(profile))
                return false;

            file = DSRPaths.ProfilesPath.GetFile(profile);
            if (file.FullName.StartsWith(DSRPaths.ProfilesPath.FullName, StringComparison.OrdinalIgnoreCase))
                return file.Exists;

            file = null;
            return false;
        }
    }
}
