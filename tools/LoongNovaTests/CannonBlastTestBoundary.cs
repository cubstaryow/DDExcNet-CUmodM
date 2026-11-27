// Offline physics/terrain doubles. CannonBlast.cs itself is linked unchanged.
public sealed class BuildingEntity
{
    public bool animal, cantHit, indestructible;
    public float health = 2000;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
}
public static class Utils
{
    public static bool GetBody(UnityEngine.GameObject obj, out Body body) { body = obj.Body; return body != null; }
}
public struct RangeF { public float Min, Max; public RangeF(float min, float max) { Min = min; Max = max; } }
public sealed class ExplosionParams
{
    public UnityEngine.Vector2 position;
    public float range = 12, structuralDamage = 500, velocity = 60;
    public RangeF muscleDamage = new RangeF(0,60), skinDamage = new RangeF(0,75), bleedAmount = new RangeF(4,30);
    public float skinDamageChance = 0.2f, boneBreakChance = 0.06f, dislocationChance = 0.135f,
        disfigureChance = 0.34f, bleedChance = 0.15f, shrapnelChance = 0.4f, dismemberChance;
}
public static class WorldGeneration
{
    public static readonly System.Collections.Generic.List<ExplosionParams> Explosions = new System.Collections.Generic.List<ExplosionParams>();
    public static void CreateExplosion(ExplosionParams parameters) => Explosions.Add(parameters);
}
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x=x; this.y=y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x+b.x,a.y+b.y);
        public static Vector2 operator *(Vector2 a, float factor) => new Vector2(a.x*factor,a.y*factor);
    }
    public enum SendMessageOptions { DontRequireReceiver }
    public sealed class Collider2D
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class => gameObject.Building as T;
    }
    public struct RaycastHit2D { public Collider2D collider; public float distance; public Vector2 point; }
    public static class LayerMask { public static int GetMask(params string[] names) => 1; }
    public static class Physics2D
    {
        public static void IgnoreCollision(Collider2D a,Collider2D b,bool ignore){}
        public static RaycastHit2D[] Hits = System.Array.Empty<RaycastHit2D>();
        public static Collider2D[] Nearby = System.Array.Empty<Collider2D>();
        public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, float range, int mask) => (RaycastHit2D[])Hits.Clone();
        public static Collider2D[] OverlapCircleAll(Vector2 point, float radius) => Nearby;
    }
}
