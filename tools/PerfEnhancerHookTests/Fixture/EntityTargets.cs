using System.Runtime.CompilerServices;
namespace LiteEntitySystem
{
    public class EntityManager { [MethodImpl(MethodImplOptions.NoInlining)] public virtual void Update() {} }
    public class ServerEntityManager : EntityManager { [MethodImpl(MethodImplOptions.NoInlining)] public void OnLogicTick() {} }
    public class ClientEntityManager : EntityManager
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public override void Update() { base.Update(); OnLogicTick(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnLogicTick() {}
    }
}
