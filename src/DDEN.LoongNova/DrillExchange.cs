using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal enum DrillExchangeResult { Success, MissingCases, InsufficientQuality, SpawnFailed }
    internal static class DrillExchange
    {
        internal const int Rounds = 4;
        internal const float QualityUnits = 400;
        internal static DrillExchangeResult Prepare<T>(float condition, int cases, Func<T> create, Action<T> rollback,
            out float remaining, out List<T> made) where T : class
        {
            made = new List<T>(); remaining = condition;
            if (cases < Rounds) return DrillExchangeResult.MissingCases;
            if (!DragonizationRules.TrySpend(condition, QualityUnits, out float after)) return DrillExchangeResult.InsufficientQuality;
            try
            {
                for (int i = 0; i < Rounds; i++)
                {
                    var round = create();
                    if (round == null) throw new InvalidOperationException("Drill round spawn failed");
                    made.Add(round);
                }
            }
            catch
            {
                foreach (var round in made) rollback(round);
                made.Clear(); return DrillExchangeResult.SpawnFailed;
            }
            remaining = after; return DrillExchangeResult.Success;
        }
    }
}
