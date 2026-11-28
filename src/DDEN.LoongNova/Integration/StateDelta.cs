using Newtonsoft.Json.Linq;
namespace DDEN.LoongNova
{
    internal static class StateDelta
    {
        internal static void Compact(JObject reply, JToken request)
        {
            if (request?.Value<string>("operation") != "read" || reply.Value<bool?>("ok") != true) return;
            if (reply["energyMode"] != null && request.Value<int?>("knownEnergyMode") != reply.Value<int>("energyMode")) return;
            var revision = reply["state"]?.Value<long?>("revision");
            if (revision.HasValue && request.Value<long?>("knownRevision") == revision.Value)
            { reply.Remove("state"); reply["unchanged"] = true; }
        }
    }
}
