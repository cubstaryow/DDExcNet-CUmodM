using System;
using System.Collections;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    [Flags]
    internal enum SupplyPool { None = 0, Medical = 1, Equipment = 2, Container = 4, Tool = 8 }
    internal readonly struct SupplyCandidate
    {
        internal readonly string Id;
        internal readonly float Weight;
        internal readonly SupplyPool Pools;
        internal SupplyCandidate(string id, float weight, SupplyPool pools) { Id = id; Weight = weight; Pools = pools; }
    }

    internal static class SupplyCrateRules
    {
        internal const string SealedId = "dden.loongnova.sealed-supply-crate";
        internal const string OpenId = "dden.loongnova.supply-crate";
        internal const float SealedWeight = 5, OpenWeight = 2, Capacity = 20, MaxItemWeight = 5;
        internal const float EncumbranceMultiplier = 0.9f;
        internal const float DecayMinutes = 240;
        internal const int DecayInfo = 5; // Native flags: contains items (1) and carrying player is moving (4).
        internal const int VariantCount = 5, TotalVariantWeight = 9;
        internal const int CannonRounds = 10;
        internal static int PickVariant(int roll)
        {
            if (roll < 0 || roll >= TotalVariantWeight) throw new ArgumentOutOfRangeException(nameof(roll));
            return roll < 2 ? 0 : roll < 4 ? 1 : roll < 5 ? 2 : roll < 7 ? 3 : 4;
        }
        internal static int PlayerCount(bool multiplayer, IEnumerable players)
        {
            if (!multiplayer || players == null) return 1;
            int count = 0;
            foreach (var player in players) if (player != null) count++;
            return Math.Max(1, count); // Connected players count even while dead; never count world NPCs.
        }
        internal static int DropCount(int players, int bonus) => Math.Max(1, players) + Math.Max(0, Math.Min(2, bonus));

        internal static IReadOnlyList<string> Contents(int variant, IReadOnlyList<SupplyCandidate> catalog,
            Func<int, int> randomIndex, Func<double> randomRoll)
        {
            var items = new List<string>();
            var randomPools = new List<SupplyPool>();
            var weights = new Dictionary<string, float>();
            foreach (var entry in catalog) weights[entry.Id] = entry.Weight;
            double used = 0;
            switch (variant)
            {
                case 0:
                    Add(MedicalCatalog.InjectorId, 2); Add(MedicalCatalog.MedicalId, 2); Add(MedicalCatalog.RegenerationId, 2);
                    for (int i = 0; i < 6; i++) randomPools.Add(SupplyPool.Medical);
                    break;
                case 1:
                    Add(Registration.CellId); Add("largebattery", 2); Add("mediumbattery", 2);
                    Add("nutrientbar", 6); Add("waterbottle", 4); Add("scaffoldingpack");
                    break;
                case 2:
                    Add(Registration.ChargeGunId);
                    bool mystery = false;
                    for (int i = 0; i < CannonRounds; i++)
                    {
                        string id = CannonSpawnWeights.Pick(randomRoll(), !mystery);
                        if (!CannonAmmoState.IsRound(id)) throw new InvalidOperationException("Invalid supply ammunition");
                        mystery |= id == CannonAmmoState.RevivalId;
                        Add(id);
                    }
                    break;
                case 3:
                    Add(Registration.GunId); Add(Registration.CellId, 2);
                    Add("riflemagazine", 2); Add("boxof12gauge", 2); Add("smallmagazine", 2);
                    break;
                case 4:
                    Add(Registration.WrenchId); Add(Registration.BeltId); Add(Registration.CellId);
                    randomPools.Add(SupplyPool.Equipment); randomPools.Add(SupplyPool.Equipment);
                    randomPools.Add(SupplyPool.Container); randomPools.Add(SupplyPool.Tool);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(variant));
            }
            for (int i = 0; i < randomPools.Count; i++)
            {
                double reserve = 0;
                for (int next = i + 1; next < randomPools.Count; next++) reserve += Minimum(randomPools[next]);
                double budget = Math.Min(MaxItemWeight, Capacity - used - reserve);
                var eligible = new List<SupplyCandidate>();
                foreach (var candidate in catalog)
                    if ((candidate.Pools & randomPools[i]) != 0 && Valid(candidate.Weight) && candidate.Weight <= budget)
                        eligible.Add(candidate);
                if (eligible.Count == 0) throw new InvalidOperationException("No fitting supply item: " + randomPools[i]);
                int pick = randomIndex(eligible.Count);
                if (pick < 0 || pick >= eligible.Count) throw new InvalidOperationException("Invalid supply item roll");
                Add(eligible[pick].Id);
            }
            return items;

            void Add(string id, int count = 1)
            {
                if (id == SealedId || id == OpenId || !weights.TryGetValue(id, out var weight) || !Valid(weight)
                    || used + weight * count > Capacity) throw new InvalidOperationException("Supply item missing or overweight: " + id);
                for (int n = 0; n < count; n++) items.Add(id);
                used += weight * count;
            }
            double Minimum(SupplyPool pool)
            {
                double minimum = double.PositiveInfinity;
                foreach (var candidate in catalog)
                    if ((candidate.Pools & pool) != 0 && Valid(candidate.Weight)) minimum = Math.Min(minimum, candidate.Weight);
                if (double.IsInfinity(minimum)) throw new InvalidOperationException("Missing supply item pool: " + pool);
                return minimum;
            }
        }
        private static bool Valid(float weight) => !float.IsNaN(weight) && !float.IsInfinity(weight) && weight >= 0 && weight <= MaxItemWeight;
    }

    internal sealed class SupplyCrateOpening
    {
        private bool _busy, _opened;
        internal bool TryOpen<T>(IReadOnlyList<string> contents, Func<T> createBox, Func<string, T> createItem,
            Func<T, T, bool> store, Action<T> rollback, Action consume, out T box, out Exception error) where T : class
        {
            box = null; error = null;
            if (_busy || _opened) return false;
            _busy = true;
            T preparedBox = null;
            var items = new List<T>();
            try
            {
                preparedBox = createBox();
                if (preparedBox == null) throw new InvalidOperationException("Supply container creation failed");
                foreach (string id in contents)
                {
                    var item = createItem(id);
                    if (item == null) throw new InvalidOperationException("Supply item creation failed: " + id);
                    items.Add(item);
                    if (!store(preparedBox, item)) throw new InvalidOperationException("Supply item could not enter its container: " + id);
                }
                consume();
                _opened = true; box = preparedBox;
                return true;
            }
            catch (Exception failure)
            {
                error = failure;
                foreach (var item in items) Clean(item);
                if (preparedBox != null) Clean(preparedBox);
                return false;
            }
            finally { _busy = false; }

            void Clean(T item) { try { rollback(item); } catch { /* Continue cleaning the other prepared objects. */ } }
        }
    }
}
