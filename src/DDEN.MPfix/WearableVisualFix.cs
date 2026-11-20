using System;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「客机身上挂着一件装备的贴图，但实际上并没有穿戴」。
    ///
    /// 原理：穿戴装备时会由 <c>Wearable.CreateSprites</c> 在**肢体下面**生成若干纯视觉子物体
    /// （带 <c>WearableExtension</c>，其 <c>reference</c> 指向那件 Item）。
    /// 正常卸下时 <c>Body.DropWearable</c> → <c>Wearable.ClearSprites()</c> 会删掉它们。
    /// 但联机里物品经常是被 Together 用 <c>SetParent(null)</c> / <c>HeldSeat</c> / 同步强制搬走的，
    /// 那条路不经过 <c>DropWearable</c>，于是视觉子物体没人清理 →
    /// 人走了、装备没了，但**贴图一直跟着身体**。
    ///
    /// 修法（只删“确实已经不属于这个身体”的视觉层，绝不碰真实物品）：
    ///  ① 给 <c>Body.DropWearable</c> 挂 postfix，卸下后再补一次 <c>ClearSprites()</c>（幂等）；
    ///  ② 每 2 秒扫一遍附近身体上的 <c>WearableExtension</c>：
    ///     reference 为空、或它挂的 Item 已经不属于这个身体 → 删掉这个视觉子物体。
    /// </summary>
    internal static class WearableVisualFix
    {
        private static float _nextScan;
        private static int _cleaned;
        private static float _lastLogAt = -99f;
        private static readonly Dictionary<int, float> StaleSince = new Dictionary<int, float>();
        private const float GraceSeconds = 1.5f;   // 判定残留要持续这么久才删，避免换装瞬间误删

        [HarmonyPatch(typeof(Body), "DropWearable")]
        internal static class Body_DropWearable_ClearPatch
        {
            private static void Postfix(Item item)
            {
                try
                {
                    if (item == null) return;
                    Wearable wearable;
                    if (item.TryGetComponent<Wearable>(out wearable) && wearable != null)
                    {
                        wearable.ClearSprites();      // 幂等：原版已经清过也没关系
                    }
                }
                catch (Exception ex)
                {
                    MPfixPlugin.Log.Debug("卸下时清理穿戴贴图失败：" + ex.Message);
                }
            }
        }

        /// <summary>错峰起步：让三个周期巡检不要在同一帧一起扫。</summary>
        internal static void PrimeScan(float offsetSeconds)
        {
            _nextScan = Time.unscaledTime + Mathf.Max(0f, offsetSeconds);
        }

        internal static void Tick()
        {
            if (!MPfixPlugin.CfgCleanWearableVisuals.Value) return;

            try
            {
                float now = Time.unscaledTime;
                if (now < _nextScan) return;
                _nextScan = now + Mathf.Max(1f, MPfixPlugin.CfgVisualInterval.Value);

                // 共享缓存：不再自己扫全场景（半径由 NearbyBodies 统一管，25 米）
                foreach (Body body in NearbyBodies.Get())
                {
                    if (body == null) continue;
                    SweepBody(body);
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("穿戴贴图巡检失败：" + ex.Message);
            }
        }

        private static void SweepBody(Body body)
        {
            try
            {
                var extensions = body.GetComponentsInChildren<WearableExtension>(true);
                if (extensions == null || extensions.Length == 0) return;

                for (int i = 0; i < extensions.Length; i++)
                {
                    WearableExtension ext = extensions[i];
                    if (ext == null || ext.gameObject == null) continue;

                    Item reference = ext.reference;
                    bool stale;
                    if (reference == null)
                    {
                        stale = true;                       // 来源物品已经没了
                    }
                    else
                    {
                        // 物品必须仍然“穿在这个身体上”，否则这层贴图就是残留
                        Body owner = reference.transform.parent != null
                            ? reference.transform.parent.GetComponentInParent<Body>()
                            : null;
                        stale = owner != body;
                    }

                    if (!stale)
                    {
                        StaleSince.Remove(ext.gameObject.GetInstanceID());
                        continue;
                    }

                    // 连续两次巡检都判定为残留才删（换装/同步瞬间的 parent 为空不算）
                    int key = ext.gameObject.GetInstanceID();
                    float now = Time.unscaledTime;
                    float since;
                    if (!StaleSince.TryGetValue(key, out since))
                    {
                        if (StaleSince.Count > 512) StaleSince.Clear();
                        StaleSince[key] = now;
                        continue;
                    }
                    if (now - since < GraceSeconds) continue;
                    StaleSince.Remove(key);

                    if (Time.unscaledTime - _lastLogAt > 2f)
                    {
                        _lastLogAt = Time.unscaledTime;
                        MPfixPlugin.Log.Info($"[穿戴贴图] 清理残留视觉层 '{ext.gameObject.name}'" +
                                             $"（来源 {(reference != null ? reference.id : "已销毁")}，实际未穿戴）");
                    }

                    _cleaned++;
                    UnityEngine.Object.Destroy(ext.gameObject);
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("清理穿戴贴图失败：" + ex.Message);
            }
        }
    }
}
