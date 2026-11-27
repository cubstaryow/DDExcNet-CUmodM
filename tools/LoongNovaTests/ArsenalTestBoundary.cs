public sealed class Recipe
{
    public int INT;
    public Recipes.RecipeCategory category;
    public RecipeResult result;
    public System.Collections.Generic.List<RecipeItem> items;
}
public sealed class RecipeResult { public string id; public int amount; public float resultCondition; }
public static class Recipes { public enum RecipeCategory { Default=0, Equipment=1 } }
namespace DDEN.Shared
{
    internal static class Reflect
    {
        internal static System.Type Type(string name) => name == "Together.ItemSync" ? typeof(OfflineReachCheck)
            : name == "Together.SyncInfo" ? typeof(OfflineItemSyncInfo) : name == "Together.NetBody" ? typeof(OfflineNetBody) : name == "Together.MedicalSync" ? typeof(OfflineMedicalSync)
            : name == "Together.BodyDamageEvent" ? typeof(OfflineReviveEvent) : name == "Together.SharedMain" ? typeof(OfflinePlayerLists)
            : name == "CasualtiesTogetherUtils.Util" ? typeof(OfflineLimbRepair) : null;
        internal static object GetInstance(object value,string name) => value?.GetType().GetField(name)?.GetValue(value)
            ?? value?.GetType().GetProperty(name)?.GetValue(value);
    }
}
public static class OfflineReachCheck
{
    public static readonly System.Collections.Generic.Dictionary<Item,OfflineItemSyncInfo> Items = new System.Collections.Generic.Dictionary<Item,OfflineItemSyncInfo>();
    public static bool TryGetSyncInfo(Item item,out OfflineItemSyncInfo info) => Items.TryGetValue(item,out info);
    public static bool Allowed;
    public static int Calls;
    public static float Radius;
    public static bool Obstruction;
    public static bool CheckIfBodyReachThisItem(Item item,Body body,float maxdist,bool check_obstruction)
    { Calls++; Radius=maxdist; Obstruction=check_obstruction; return Allowed; }
}

public struct OfflineItemSyncId { public ushort id; }
public sealed class OfflineItemSyncInfo { public OfflineItemSyncId syncId; }
public static class OfflineLimbRepair
{
    public static readonly System.Collections.Generic.List<Limb> Restored=new();
    public static int Rebinds;
    public static void RestoreDetachedLimb(Limb part){Restored.Add(part);}
    public static void IgnoreOwnLimbCollisions(Body body){}
    public static void RebindLimbHinges(Body body){Rebinds++;}
}
