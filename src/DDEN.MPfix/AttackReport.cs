using System;
using System.Buffers.Binary;

namespace DDEN.MPfix
{
    // Versioned trailer on the existing reliable attack packet. No separate message/JSON/GZip.
    // Older hosts ignore trailing bytes; older clients simply have no trailer.
    internal readonly struct AttackReport
    {
        internal const int Size = 24;
        private const uint Magic = 0x314E4444;
        private const uint EndMagic = 0x44444E31;
        internal readonly int Branch;
        internal readonly float Damage, Structural, Power;

        internal AttackReport(int branch, float damage, float structural, float power)
        { Branch = branch; Damage = damage; Structural = structural; Power = power; }

        private static bool Valid(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f;
        internal bool ValidValues => (Branch == 1 || Branch == 2) && Valid(Damage) && Valid(Structural)
            && Valid(Power) && Power <= 100f && Damage * Power <= 200000f && Structural * Power <= 200000f;

        internal void Write(Span<byte> bytes)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(4), Branch);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(8), BitConverter.SingleToInt32Bits(Damage));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(12), BitConverter.SingleToInt32Bits(Structural));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(16), BitConverter.SingleToInt32Bits(Power));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(20), EndMagic);
        }

        internal static bool TryRead(ReadOnlySpan<byte> bytes, out AttackReport report)
        {
            report = default;
            if (bytes.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(20)) != EndMagic) return false;
            report = new AttackReport(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4)),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16))));
            return report.ValidValues;
        }

        // Reject unbounded reports, including when the host has no attack power/tool damage.
        internal bool Credible(float hostDamage, float hostStructural, float hostPower, float worldMultiplier)
        {
            if (!ValidValues || !Valid(hostDamage) || !Valid(hostStructural) || !Valid(hostPower)) return false;
            float mult = Valid(worldMultiplier) && worldMultiplier > 0f ? worldMultiplier : 1f;
            return Damage * Power <= hostDamage * hostPower * mult * 3f + 0.001f
                && Structural * Power <= hostStructural * hostPower * mult * 3f + 0.001f;
        }
    }
}
