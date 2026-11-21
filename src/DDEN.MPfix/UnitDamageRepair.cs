using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;

namespace DDEN.MPfix
{
    /// <summary>
    /// Basic mode repairs only the host's remote-attack calculation. Aggressive mode supplies
    /// the client's calculation in the SAME native attack packet: native target/reach/cooldown/PVP
    /// validation and the native damage method still run once. No delayed second damage event.
    /// Together is deliberately accessed by reflection, never an assembly reference.
    /// </summary>
    internal static class UnitDamageRepair
    {
        private static Type _attackType;
        private static FieldInfo _hitBuilding, _hitLimb, _stateAttack, _statePower, _stateCanAttack, _stateBody;
        private static PropertyInfo _writerData, _writerLength, _readerData, _readerSize, _readerPosition;
        private static MethodInfo _putBytes;
        private static ReceiveScope _receiving;
        private static readonly byte[] Trailer = new byte[AttackReport.Size];
        private static bool _transportReady;

        private sealed class ReceiveScope
        {
            internal AttackReport Report;
            internal bool Used;
        }

        internal static bool Enabled(int branch) => branch == 1
            ? MPfixPlugin.CfgSyncBuildingDamage.Value : MPfixPlugin.CfgSyncBodyDamage.Value;

        internal static void Install(Harmony harmony)
        {
            try
            {
                _attackType = AccessTools.TypeByName("Together.Body_Attack_MultiplayerPatch");
                if (_attackType == null) throw new MissingMemberException("Body_Attack_MultiplayerPatch");
                var bodyDamage = AccessTools.Method(_attackType, "DoPVPMeleeDamage");
                if (bodyDamage == null) throw new MissingMethodException("DoPVPMeleeDamage");
                harmony.Patch(bodyDamage, prefix: new HarmonyMethod(typeof(UnitDamageRepair), nameof(BeforeBodyDamage)));
                MPfixPlugin.ClearPatchError("身体/肢体伤害重算");
                MPfixPlugin.Log.Info("已启用补丁：身体/肢体近战伤害重算");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("身体/肢体伤害补丁安装失败：" + ex.Message);
                MPfixPlugin.NotePatchError("身体/肢体伤害重算", ex.Message);
                return;
            }

            try
            {
                Type writer = AccessTools.TypeByName("LiteNetLib.Utils.NetDataWriter");
                Type reader = AccessTools.TypeByName("LiteNetLib.Utils.NetDataReader");
                MethodInfo postfix = AccessTools.Method(_attackType, "Postfix");
                MethodInfo receiver = AccessTools.Method(_attackType, "ServerReceiver_PlayerBodyAttack");
                Type state = postfix.GetParameters()[4].ParameterType.GetElementType();
                _hitBuilding = AccessTools.Field(_attackType, "_LastHitBuilding");
                _hitLimb = AccessTools.Field(_attackType, "_LastHitLimb");
                _stateAttack = AccessTools.Field(state, "atk");
                _statePower = AccessTools.Field(state, "atkpwr");
                _stateCanAttack = AccessTools.Field(state, "candoattack");
                _stateBody = AccessTools.Field(state, "body");
                _writerData = AccessTools.Property(writer, "Data");
                _writerLength = AccessTools.Property(writer, "Length");
                _putBytes = AccessTools.Method(writer, "Put", new[] { typeof(byte[]) });
                _readerData = AccessTools.Property(reader, "RawData");
                _readerSize = AccessTools.Property(reader, "RawDataSize");
                _readerPosition = AccessTools.Property(reader, "Position");
                if (receiver == null || _hitBuilding == null || _hitLimb == null || _stateBody == null
                    || _stateAttack == null || _statePower == null || _stateCanAttack == null
                    || _writerData == null || _writerLength == null || _putBytes == null
                    || _readerData == null || _readerSize == null || _readerPosition == null)
                    throw new MissingMemberException("attack transport signature");

                harmony.Patch(postfix, transpiler: new HarmonyMethod(typeof(UnitDamageRepair), nameof(AddReportBeforeSend)));
                harmony.Patch(receiver,
                    prefix: new HarmonyMethod(typeof(UnitDamageRepair), nameof(BeforeReceive)),
                    finalizer: new HarmonyMethod(typeof(UnitDamageRepair), nameof(AfterReceive)));
                _transportReady = true;
                MPfixPlugin.ClearPatchError("激进攻击上报");
                MPfixPlugin.Log.Info("已启用补丁：激进模式单位/身体伤害随原攻击消息上报（不另发消息）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.NotePatchError("激进攻击上报", ex.Message);
                MPfixPlugin.Log.Warn("激进单位伤害上报安装失败，保留基础重算：" + ex);
            }
        }

        private static IEnumerable<CodeInstruction> AddReportBeforeSend(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 2; i < codes.Count; i++)
            {
                if (!(codes[i].operand is MethodInfo method) || method.Name != "Client_Send"
                    || method.DeclaringType.FullName != "Together.Net") continue;
                if ((codes[i - 1].opcode != OpCodes.Ldloca_S && codes[i - 1].opcode != OpCodes.Ldloca)
                    || (codes[i - 2].opcode != OpCodes.Ldloca_S && codes[i - 2].opcode != OpCodes.Ldloca))
                    throw new InvalidOperationException("Native attack send arguments changed");
                var loadWriter = new CodeInstruction(OpCodes.Ldloc, codes[i - 1].operand);
                loadWriter.MoveLabelsFrom(codes[i - 2]);
                codes.InsertRange(i - 2, new[] { loadWriter,
                    new CodeInstruction(OpCodes.Ldarg_S, (byte)4), new CodeInstruction(OpCodes.Ldind_Ref),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(UnitDamageRepair), nameof(AttachReport))) });
                return codes;
            }
            throw new MissingMethodException("Native attack packet send site");
        }

        private static void AttachReport(object writer, object state)
        {
            try
            {
                if (!_transportReady || !MPfixPlugin.Active || !MPfixPlugin.CfgAggressiveConverge.Value
                    || !Mp.IsClient || _stateBody.GetValue(state) as Body != Game.LocalBody) return;
                if (!(bool)_stateCanAttack.GetValue(state)) return;
                int branch = _hitLimb.GetValue(null) is Limb limb && limb != null ? 2
                    : _hitBuilding.GetValue(null) is BuildingEntity building && building != null ? 1 : 0;
                if (branch == 0 || !Enabled(branch)) return;
                AttackInfo atk = (AttackInfo)_stateAttack.GetValue(state);
                var report = new AttackReport(branch, atk.damage, atk.structuralDamage, (float)_statePower.GetValue(state));
                if (report.ValidValues) AppendReport(writer, report);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("准备攻击上报失败：" + ex.Message); }
        }

        private static void AppendReport(object writer, AttackReport report)
        {
            try
            {
                byte[] data = (byte[])_writerData.GetValue(writer);
                int length = (int)_writerLength.GetValue(writer);
                // MP 5.5.4 SERVER_PlayerBodyAttack. Never append to other messages in this scope.
                if (length < 1 || data[0] != 146) return;
                report.Write(Trailer);
                _putBytes.Invoke(writer, new object[] { Trailer });
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("附加攻击上报失败：" + ex.Message); }
        }

        private static void BeforeReceive([HarmonyArgument(1)] object reader, out ReceiveScope __state)
        {
            __state = _receiving;
            _receiving = null;
            try
            {
                if (!_transportReady || !MPfixPlugin.Active || !MPfixPlugin.CfgAggressiveConverge.Value || !Mp.IsServer) return;
                // Only inspect trailing bytes: do not advance the native reader or bypass its checks.
                if (ReadReport(reader, out var report) && Enabled(report.Branch))
                    _receiving = new ReceiveScope { Report = report };
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("读取攻击上报失败：" + ex.Message); }
        }

        private static Exception AfterReceive(Exception __exception, ReceiveScope __state)
        { _receiving = __state; return __exception; }

        private static bool ReadReport(object reader, out AttackReport report)
        {
            report = default;
            byte[] data = (byte[])_readerData.GetValue(reader);
            int size = (int)_readerSize.GetValue(reader);
            int pos = (int)_readerPosition.GetValue(reader);
            return data != null && size <= data.Length && pos >= 0 && size - pos >= AttackReport.Size
                && AttackReport.TryRead(new ReadOnlySpan<byte>(data, size - AttackReport.Size, AttackReport.Size), out report);
        }

        // Runs only with the explicit existing self-test marker. Uses the installed transport, no network send.
        internal static bool SelfTest()
        {
            if (!_transportReady) return false;
            object writer = Activator.CreateInstance(_writerData.DeclaringType);
            _putBytes.Invoke(writer, new object[] { new byte[] { 146, 7, 9 } });
            AppendReport(writer, new AttackReport(2, 333f, 600f, 0.75f));
            int length = (int)_writerLength.GetValue(writer);
            byte[] data = (byte[])_writerData.GetValue(writer);
            if (length != 3 + AttackReport.Size || data[0] != 146 || data[1] != 7 || data[2] != 9) return false;
            byte[] offsetPacket = new byte[length + 5];
            Array.Copy(data, 0, offsetPacket, 5, length);
            object reader = Activator.CreateInstance(_readerData.DeclaringType,
                new object[] { offsetPacket, 5, offsetPacket.Length });
            int position = (int)_readerPosition.GetValue(reader);
            return ReadReport(reader, out var report) && report.Branch == 2 && report.Damage == 333f
                && report.Structural == 600f && report.Power == 0.75f
                && position == (int)_readerPosition.GetValue(reader);
        }

        internal static void Repair(int branch, Body attacker, ref AttackInfo atk, ref float power)
        {
            if (!MPfixPlugin.Active || !Enabled(branch) || !Mp.IsRunning || !Mp.IsServer || attacker == null
                || attacker == Game.LocalBody || atk == null) return;
            float multiplier = BlockDamageRelax.AttackDamageMultiplier();
            AttackInfo copy = null;
            if (MPfixPlugin.CfgAggressiveConverge.Value && _receiving != null && !_receiving.Used
                && _receiving.Report.Branch == branch)
            {
                // Consume once even if rejected, so a native rerun cannot reuse this report.
                _receiving.Used = true;
                var report = _receiving.Report;
                if (report.Credible(atk.damage, atk.structuralDamage, power, multiplier))
                {
                    copy = Copy(atk);
                    copy.damage = report.Damage;
                    copy.structuralDamage = report.Structural;
                    // Damage/strength thresholds match the client; world damage must not amplify knockback.
                    if (branch == 1 && report.Power > 0f) copy.knockBack *= power / report.Power;
                    power = report.Power;
                }
            }
            if (copy == null)
            {
                if (Math.Abs(multiplier - 1f) < 0.0001f) return;
                copy = Copy(atk);
                copy.damage *= multiplier;
                copy.structuralDamage *= multiplier;
            }
            atk = copy;
        }

        private static readonly MethodInfo Memberwise = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static AttackInfo Copy(AttackInfo atk) => (AttackInfo)Memberwise.Invoke(atk, null);

        private static void BeforeBodyDamage(Body attacker, ref AttackInfo atk, ref float armstrength)
        {
            try { Repair(2, attacker, ref atk, ref armstrength); }
            catch (Exception ex) { MPfixPlugin.Log.Debug("身体伤害重算失败：" + ex.Message); }
        }
    }
}
