using DDEN.Shared;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    internal static class WingVisualSync
    {
        internal const string Channel = "dden.loongnova.wings.v1";

        internal static void Publish(GravityBelt belt, bool flying)
        {
            if (belt == null || !Mp.CoreReady || !Mp.IsRunning || !Mp.IsServer || !LoongNovaPlugin.Service.Ready) return;
            int id = NetworkAccess.Id(belt.Item);
            if (id == 0) return;
            long sequence = belt.NextWingSequence();
            belt.ReceiveWingPose(sequence, flying);
            Mp.Broadcast(Channel, new JObject { ["belt"] = id, ["sequence"] = sequence, ["flying"] = flying });
        }

        internal static void Receive(JToken data)
        {
            if (!Mp.CoreReady || !Mp.IsRunning || Mp.IsServer || !(data is JObject)
                || data["flying"]?.Type != JTokenType.Boolean || data["belt"]?.Type != JTokenType.Integer
                || data["sequence"]?.Type != JTokenType.Integer || data.ToString(Newtonsoft.Json.Formatting.None).Length > 256) return;
            int id = data.Value<int?>("belt") ?? 0;
            long sequence = data.Value<long?>("sequence") ?? -1;
            if (sequence < 0) return;
            var item = NetworkAccess.Item(id);
            if (item != null && item.id == Registration.BeltId)
                GravityBelt.For(item).ReceiveWingPose(sequence, data.Value<bool>("flying"));
        }
    }
}
