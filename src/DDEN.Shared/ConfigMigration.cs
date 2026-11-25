using System.Collections;
using System.Reflection;
using BepInEx.Configuration;

namespace DDEN.Shared
{
    /// <summary>Remove retired definitions, including unbound entries retained by BepInEx.</summary>
    public static class ConfigMigration
    {
        public static void Retire(ConfigFile config, params string[] definitions)
        {
            try { RemoveEntries(config, definitions); }
            catch (System.Exception ex) { Log.Warn("Retired settings could not be removed: " + ex.GetType().Name); }
        }
        private static void RemoveEntries(ConfigFile config, string[] definitions)
        {
            bool changed = false;
            var orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(config) as IDictionary;
            foreach (string path in definitions)
            {
                int slash = path.IndexOf('/'); if (slash <= 0) continue;
                var key = new ConfigDefinition(path.Substring(0, slash), path.Substring(slash + 1));
                changed |= config.Remove(key);
                if (orphans?.Contains(key) == true) { orphans.Remove(key); changed = true; }
            }
            if (changed) config.Save();
        }
    }
}
