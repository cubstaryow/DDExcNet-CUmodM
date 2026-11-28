using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    // Pure catalog: native game/CCL sections and stable keys, with no Unity or startup item-table access.
    internal sealed class LoongTextCatalog
    {
        internal const string Prefix = "dden.loongnova.";
        internal static readonly IReadOnlyDictionary<string, string> Items = new Dictionary<string, string>
        {
            ["smelter"] = Registration.SmelterId, ["tacticalGoggles"] = Registration.TacticalGogglesId, ["gun"] = Registration.GunId, ["cell"] = Registration.CellId, ["belt"] = Registration.BeltId,
            ["matter"] = Registration.MatterId, ["dragon"] = Registration.DragonId, ["wrench"] = Registration.WrenchId,
            ["pickaxe"] = Registration.PickaxeId, ["charge"] = Registration.ChargeGunId,
            ["bio"] = CannonAmmoState.BioId, ["demo"] = CannonAmmoState.DemolitionId, ["explosive"] = CannonAmmoState.ExplosiveId,
            ["drill"] = CannonAmmoState.DrillId, ["drain"] = CannonAmmoState.DrainId, ["breach"] = CannonAmmoState.BreachId,
            ["revival"] = CannonAmmoState.RevivalId, ["carrier"] = CannonAmmoState.CarrierId,
            ["injector"] = MedicalCatalog.InjectorId, ["medical"] = MedicalCatalog.MedicalId,
            ["regeneration"] = MedicalCatalog.RegenerationId, ["emptyAmpoule"] = MedicalCatalog.EmptyId,
            ["supplySealed"] = SupplyCrateRules.SealedId, ["supplyOpen"] = SupplyCrateRules.OpenId,
            ["recharger"] = SpatialCatalog.ChargerId, ["membrane"] = SpatialCatalog.MembraneId, ["spatialComponent"] = SpatialCatalog.ComponentId, ["spatialCrate"] = SpatialCatalog.CrateId,
            ["refitted"] = "dden.loongnova.dragon-claw-refitted-wrench"
        };
        private readonly struct Address
        {
            internal readonly string Section, Key;
            internal Address(string section, string key) { Section = section; Key = key; }
        }
        private readonly JObject _english, _chinese;
        private readonly Dictionary<string, Address> _addresses = new Dictionary<string, Address>(StringComparer.Ordinal);
        internal IEnumerable<string> Keys => _addresses.Keys;

        internal LoongTextCatalog(JObject english, JObject chinese)
        {
            _english = english; _chinese = chinese;
            foreach (var item in Items)
            {
                _addresses[item.Key + "Name"] = new Address("item", item.Value);
                _addresses[item.Key + "Description"] = new Address("item", item.Value + "dsc");
            }
            foreach (var property in ((JObject)english["other"]).Properties())
                if (property.Name.StartsWith(Prefix, StringComparison.Ordinal))
                    _addresses[property.Name.Substring(Prefix.Length)] = new Address("other", property.Name);
            _addresses["dragonQuality"] = new Address("other", "cq" + Registration.DragonQuality);
        }
        internal string Text(string key, string language, Func<string, string, string> current = null)
        {
            if (key == null) return string.Empty;
            if (!_addresses.TryGetValue(key, out var address)) return key;
            string active = current?.Invoke(address.Section, address.Key);
            if (!string.IsNullOrWhiteSpace(active)) return active;
            var fallback = language != null && language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? _chinese : _english;
            return fallback[address.Section]?.Value<string>(address.Key) ?? _english[address.Section]?.Value<string>(address.Key) ?? key;
        }
    }
}
