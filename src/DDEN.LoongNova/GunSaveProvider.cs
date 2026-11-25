using CUCoreLib.Saving;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    internal sealed class GunSaveProvider : IItemSaveProvider
    {
        public int GetVersion() => 1;
        public JToken Capture(Item item, string key)
        {
            if (item == null) return null;
            if (item.id == Registration.GunId) return DragonGun.For(item).State.Capture();
            if (item.id == Registration.BeltId) return GravityBelt.For(item).State.Capture();
            return null;
        }
        public void Restore(Item item, string key, JToken payload, int version, SaveRestoreContext context)
        {
            if (item == null || version != 1) return;
            if (item.id == Registration.BeltId)
            {
                var belt = GravityBelt.For(item);
                belt.Known = belt.State.Restore(payload);
                belt.RefreshEncumbrance();
                return;
            }
            if (item.id != Registration.GunId) return;
            var gun = DragonGun.For(item);
            gun.Known = gun.State.Restore(payload);
            gun.Normalize();
        }
    }
}
