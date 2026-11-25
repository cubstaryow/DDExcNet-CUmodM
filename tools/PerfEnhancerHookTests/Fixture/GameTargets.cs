using System.Runtime.CompilerServices;
public class Item
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Awake() {}
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() {}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { HandleDecay(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void HandleDecay() {}
}
public class Body
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { HandlePhysics(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void FixedUpdate() {}
    [MethodImpl(MethodImplOptions.NoInlining)] public void HandlePhysics() {}
}

public class LiquidAffect { [MethodImpl(MethodImplOptions.NoInlining)] public void FixedUpdate() {} }
public class IKHandle
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { UpdateIK(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateIK() {}
}
public class Wearable { [MethodImpl(MethodImplOptions.NoInlining)] public void Awake() {} }
