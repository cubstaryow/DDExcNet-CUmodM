using System;
using System.Reflection;
using DDEN.Shared;

namespace DDEN.UnitListMgr
{
    internal static class AchievementPolicy
    {
        private static MethodInfo _broadcast;
        internal static void Mark(bool configured, int created)
        {
            if (!SpawnPolicy.ShouldMark(configured, created) || SteamManager.disableAchievements) return;
            // SaveSystem saves this runtime value as cheatedSave. Never clear an existing flag.
            SteamManager.disableAchievements = true;
            if (!Mp.IsRunning || !Mp.IsServer) return;
            if (_broadcast == null)
            {
                var type = Reflect.Type("Together.AchievementBreakSync");
                if (type != null)
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        if (method.Name == "Server_Broadcast" && method.GetParameters().Length == 2
                            && method.GetParameters()[0].ParameterType == typeof(bool)) { _broadcast = method; break; }
            }
            if (_broadcast == null) throw new InvalidOperationException("MP achievement sync API unavailable");
            _broadcast.Invoke(null, new object[] { false, null });
        }
    }
}
