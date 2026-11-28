using System;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    internal static class SpatialCatalog
    {
        internal const string MembraneId = "dden.loongnova.spatial-membrane";
        internal const string ComponentId = "dden.loongnova.dual-space-component";
        internal const string CrateId = "dden.loongnova.micro-space-supply-crate";
        internal const string ChargerId = "dden.loongnova.spatial-recharger";
        internal static bool IsDevice(string id) => id == CrateId || id == ChargerId;
        internal const double DrainPerSecond = 0.1;
        internal static float WeightMultiplier(bool loaded, double energy) => loaded && energy > 0 ? 0 : 0.9f;
    }
    internal sealed class SpatialPowerState
    {
        internal readonly EnergyState Cell = new EnergyState();
        internal double Energy { get; private set; }
        internal bool Activated { get; private set; }
        internal bool Powered => Cell.Loaded && Energy > 0;
        internal float Multiplier => SpatialCatalog.WeightMultiplier(Cell.Loaded, Energy);
        internal void Load(float charge, bool favourite)
        { Activated = false; Energy = Math.Max(0, Math.Min(EnergyState.Capacity, charge)); Cell.Set(true, (float)Energy, favourite); }
        internal bool Activate(float energy, bool favourite)
        {
            if (!BeltPowerState.Finite(energy) || energy < 200 || Cell.Loaded) return false;
            Load(energy - 200, favourite); Activated = true; return true;
        }
        internal void SelfCharge(double seconds)
        {
            if (!Activated || !Cell.Loaded || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || Energy >= EnergyState.Capacity) return;
            Energy = Math.Min(EnergyState.Capacity, Energy + seconds); Cell.Set(true, (float)Energy, Cell.Favourite);
        }
        internal void Clear() { Activated = false; Energy = 0; Cell.Set(false, 0, false); }
        internal void Advance(double seconds, bool occupied)
        {
            if (!occupied || !Cell.Loaded || Energy <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            Energy = Math.Max(0, Energy - seconds * SpatialCatalog.DrainPerSecond);
            if (Energy < 1e-8) Energy = 0;
            Cell.Set(true, (float)Energy, Cell.Favourite);
        }
        internal JObject Capture()
        { var data = Cell.Capture(); data["exactEnergy"] = Energy; data["activated"] = Activated; return data; }
        internal bool Restore(JToken data)
        {
            var next = new EnergyState();
            if (!next.Restore(data)) return false;
            double exact = data.Value<double?>("exactEnergy") ?? next.Energy;
            if (double.IsNaN(exact) || double.IsInfinity(exact) || exact < 0 || exact > EnergyState.Capacity) return false;
            Cell.Restore(data); Energy = Cell.Loaded ? exact : 0; Activated = Cell.Loaded && data.Value<bool?>("activated") == true; return true;
        }
    }
}
