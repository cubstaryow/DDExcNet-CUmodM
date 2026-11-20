using System;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 穿戴/手持物品的“贴合修正”。
    ///
    /// 症状：物品明明还穿在主机角色身上，却**浮在角色上方**（错位）。
    /// 正常流程下这个偏移不该存在：
    ///  - 原版 `Body.WearWearable` 会 `SetParent(limb)` + `localPosition = zero`；
    ///  - Together 的 `HeldSeat.Seat` 装手/背包位同样 `localPosition = zero`。
    /// 所以一旦局部坐标明显非零，就说明被别的东西改过（旧版物理保险的位移、同步写回、其它模组等）。
    ///
    /// 这里做两件事：
    ///  ① `Body.WearWearable` 之后立刻把局部坐标归零（幂等，正常情况就是 no-op）；
    ///  ② 每 2 秒扫一遍附近角色身上/手里的物品，发现 |localPosition| > 0.1 就归零，
    ///     并把 parent / localPos / localScale / worldPos 打进日志（便于下次定位到底是谁改的）。
    ///
    /// 安全：只改 localPosition / localRotation，**不动 localScale**（Together 的 HeldSeat 会按肢体缩放
    /// 设置 localScale，动了会变形）；最多修正 N 次后自动停手；异常只记 Debug。
    /// </summary>
    internal static class WornItemSnap
    {
        private static float _nextScan;
        private static int _fixed;
        private static bool _disabled;
        private static float _lastLogAt = -99f;

        [HarmonyPatch(typeof(Body), "WearWearable")]
        internal static class Body_WearWearable_SnapPatch
        {
            private static void Postfix(Item item)
            {
                Snap(item, "WearWearable", false);
            }
        }

        internal static void Tick()
        {
            if (_disabled) return;
            if (!MPfixPlugin.CfgSnapWornItems.Value) return;

            try
            {
                float now = Time.unscaledTime;
                if (now < _nextScan) return;
                _nextScan = now + 2f;

                Body local = Game.LocalBody;
                if (local != null) ScanBody(local);

                foreach (Body body in UnityEngine.Object.FindObjectsOfType<Body>())
                {
                    if (body == null || body == local) continue;
                    if (local != null && Vector2.Distance(body.transform.position, local.transform.position) > 40f) continue;
                    ScanBody(body);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("WornItemSnap.Tick 失败：" + ex.Message);
            }
        }

        private static void ScanBody(Body body)
        {
            try
            {
                List<Item> worn = body.GetAllWearables();
                if (worn != null)
                {
                    for (int i = 0; i < worn.Count; i++) Snap(worn[i], "scan(worn)", true);
                }

                if (body.slots != null)
                {
                    for (int i = 0; i < body.slots.Length; i++)
                    {
                        if (body.slots[i] == null) continue;
                        Item inSlot = body.GetItem(i);
                        if (inSlot != null) Snap(inSlot, "scan(slot)", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("WornItemSnap.ScanBody 失败：" + ex.Message);
            }
        }

        private static void Snap(Item item, string why, bool quiet)
        {
            if (item == null) return;
            try
            {
                if (!MPfixPlugin.CfgSnapWornItems.Value) return;
                Transform t = item.transform;
                if (t == null || t.parent == null) return;      // 只有“挂在身上/手里/容器里”的才做
                if (t.parent.GetComponentInParent<Body>() == null) return;   // 必须确实挂在某个 Body 上

                Vector3 lp = t.localPosition;
                // 阈值 0.6 个世界单位：正常穿戴/持握都是 0，超过这个量级才是“浮空错位”。
                if (lp.sqrMagnitude <= 0.36f) return;

                if (_fixed >= Mathf.Max(1, MPfixPlugin.CfgSnapWornMax.Value))
                {
                    if (!_disabled)
                    {
                        _disabled = true;
                        MPfixPlugin.Log.Warn("[物品贴合] 本次会话修正次数已达上限，停止修正（避免影响游戏）。");
                    }
                    return;
                }

                _fixed++;
                if (Time.unscaledTime - _lastLogAt > 1f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Warn(
                        $"[物品贴合] {item.id} 在 {why} 时局部坐标异常 localPos={lp} " +
                        $"parent={(t.parent != null ? t.parent.name : "null")} localScale={t.localScale} " +
                        $"worldPos={t.position} → 已归零");
                }

                t.localPosition = Vector3.zero;   // 只归零位置：旋转要保留 slotRotation，缩放要保留 HeldSeat 的肢体补偿
            }
            catch (Exception ex)
            {
                Log.Debug("物品贴合失败：" + ex.Message);
            }
        }
    }
}
