using System;
using System.Collections.Generic;
using System.Linq;
using CUCoreLib.Data;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class SupplyLootCatalog
    {
        internal static IReadOnlyList<SupplyCandidate> Build()
        {
            var result = new List<SupplyCandidate>();
            foreach (var pair in Item.GlobalItems.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string id = pair.Key; var info = pair.Value;
                if (id == SupplyCrateRules.SealedId || id == SupplyCrateRules.OpenId || id == Registration.TacticalGogglesId || SpatialCatalog.IsDevice(id) || info == null
                    || info.category == "unobtainable" || info.category == "nospawn"
                    || float.IsNaN(info.weight) || float.IsInfinity(info.weight) || info.weight < 0 || info.weight > SupplyCrateRules.MaxItemWeight) continue;
                bool container = info.category == "container";
                if (info is CustomItemInfo custom) container |= custom.Container != null;
                else if (!container && (info.wearable || info.category == "custom"))
                    container = Resources.Load<GameObject>(id)?.GetComponent<Container>() != null;
                SupplyPool pools = SupplyPool.None;
                if ((info.category == "medical" || info.HasTag("medicine")) && id != MedicalCatalog.InjectorId
                    && id != MedicalCatalog.MedicalId && id != MedicalCatalog.RegenerationId) pools |= SupplyPool.Medical;
                if (info.wearable && !container && id != Registration.BeltId) pools |= SupplyPool.Equipment;
                if (container) pools |= SupplyPool.Container;
                if ((info.category == "tool" || info.HasTag("tool")) && id != Registration.WrenchId) pools |= SupplyPool.Tool;
                result.Add(new SupplyCandidate(id, info.weight, pools));
            }
            return result;
        }
    }
}
