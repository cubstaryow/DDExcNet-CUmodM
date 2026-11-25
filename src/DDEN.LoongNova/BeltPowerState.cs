using System;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    // Work is measured in slow-assistance equivalent seconds (0.5 energy each); flight adds sixteen per second. Saved fields retain their original units.
    internal sealed class BeltPowerState
    {
        internal const double FlightWorkRate = 16;
        internal const double MaxUnconfirmedWork = FlightWorkRate * 2;
        internal readonly EnergyState Cell = new EnergyState();
        internal string Epoch = "";
        internal double WorkTotal;
        internal double PartialSeconds;
        internal long Revision;
        internal float Remaining => Cell.Loaded ? (float)Math.Max(0, Cell.Energy - PartialSeconds * 0.5) : 0;
        // Container uses a weight multiplier: 0 means 100% reduction, 0.6 means 40% reduction.
        internal float EncumbranceMultiplier => Remaining > 0 ? 0 : 0.6f;
        internal float ChargeFraction => Remaining / EnergyState.Capacity;

        internal void Load(float energy, bool favourite)
        {
            Cell.Set(true, energy, favourite);
            Epoch = Guid.NewGuid().ToString("N");
            WorkTotal = PartialSeconds = 0;
            Revision++;
        }
        internal void Clear()
        {
            Cell.Set(false, 0, false);
            Epoch = "";
            WorkTotal = PartialSeconds = 0;
            Revision++;
        }
        internal double Advance(double seconds)
        {
            if (!Cell.Loaded || !Finite(seconds) || seconds <= 0 || Remaining <= 0) return 0;
            seconds = Math.Min(seconds, Math.Max(0, Cell.Energy * 2 - PartialSeconds));
            WorkTotal += seconds;
            PartialSeconds += seconds;
            double units = Math.Floor((PartialSeconds + 1e-9) / 2);
            if (units > 0)
            {
                Cell.Set(true, Math.Max(0, Cell.Energy - (float)units), Cell.Favourite);
                PartialSeconds = Math.Max(0, PartialSeconds - units * 2);
            }
            if (Remaining <= 1e-6f) { Cell.Set(true, 0, Cell.Favourite); PartialSeconds = 0; }
            Revision++;
            return seconds;
        }
        // Charge before applying assistance. The return value limits thrust on the final partial tick.
        internal double AdvanceActivity(double seconds, bool flying, double workBudget = double.PositiveInfinity)
        {
            if (!Finite(seconds) || seconds <= 0 || double.IsNaN(workBudget) || workBudget < 0) return 0;
            double rate = flying ? FlightWorkRate : 1;
            return Advance(Math.Min(seconds * rate, workBudget)) / rate;
        }
        internal JObject Capture() => new JObject
        {
            ["cell"] = Cell.Capture(), ["epoch"] = Epoch, ["work"] = WorkTotal,
            ["partial"] = PartialSeconds, ["revision"] = Revision
        };
        internal bool Restore(JToken data)
        {
            if (!(data is JObject)) return false;
            var cell = new EnergyState();
            string epoch = data.Value<string>("epoch");
            double work = data.Value<double?>("work") ?? double.NaN;
            double partial = data.Value<double?>("partial") ?? double.NaN;
            if (!cell.Restore(data["cell"]) || !Finite(work) || work < 0 || !Finite(partial)
                || partial < 0 || partial >= 2 || partial > work + 1e-7 || partial > cell.Energy * 2
                || (cell.Loaded ? !Guid.TryParseExact(epoch, "N", out _) : epoch != "" || work != 0 || partial != 0)) return false;
            Cell.Restore(data["cell"]);
            Epoch = epoch; WorkTotal = work; PartialSeconds = partial;
            Revision = Math.Max(0, data.Value<long?>("revision") ?? 0);
            return true;
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
