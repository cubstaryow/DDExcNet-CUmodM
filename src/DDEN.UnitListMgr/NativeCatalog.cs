using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace DDEN.UnitListMgr
{
    internal static class NativeCatalog
    {
        private static HashSet<string> _ids;
        internal static bool Contains(string id)
        {
            if (_ids == null) _ids = ReadIds();
            return _ids.Contains(id);
        }
        private static HashSet<string> ReadIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                // Read the original game's registration metadata without calling SetupItems,
                // modifying its dictionary, loading every prefab or maintaining a stale ID list.
                MethodInfo setup = typeof(Item).GetMethod("SetupItems", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                byte[] il = setup?.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return ids;
                var codes = new Dictionary<short, OpCode>();
                foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (field.GetValue(null) is OpCode code) codes[code.Value] = code;
                bool registry = false;
                for (int i = 0; i < il.Length;)
                {
                    short value = il[i++];
                    if (value == 0xfe) value = unchecked((short)(0xfe00 | il[i++]));
                    if (!codes.TryGetValue(value, out var op)) throw new InvalidOperationException("Unknown IL opcode");
                    int size;
                    switch (op.OperandType)
                    {
                        case OperandType.InlineNone: size = 0; break;
                        case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                        case OperandType.InlineVar: size = 2; break;
                        case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                        case OperandType.InlineSwitch: size = checked(4 + 4 * BitConverter.ToInt32(il, i)); break;
                        default: size = 4; break;
                    }
                    if (i + size > il.Length || size < 0) throw new InvalidOperationException("Invalid registration metadata");
                    if (op == OpCodes.Ldstr && registry) ids.Add(setup.Module.ResolveString(BitConverter.ToInt32(il, i)));
                    registry = op == OpCodes.Ldsfld && setup.Module.ResolveField(BitConverter.ToInt32(il, i)).Name == "GlobalItems";
                    i += size;
                }
            }
            catch (Exception ex)
            {
                ids.Clear(); // Unknown remains Other; never guess that all unrecognized IDs are vanilla.
                UnitListPlugin.Log.Debug("Native item categories: " + ex.Message);
            }
            return ids;
        }
    }
}
