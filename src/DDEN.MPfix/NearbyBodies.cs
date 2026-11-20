using System;
using System.Collections.Generic;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 附近角色的共享扫描缓存。
    ///
    /// 起因：`WornItemSnap` / `WearableVisualFix` 原来各自每 2 秒做一次
    /// `FindObjectsOfType<Body>()` 并遍历全部结果（半径 40 / 45 米）。在主机上角色一多，
    /// 这两次扫描会在同一帧撞在一起，形成肉眼可见的周期性卡顿。
    ///
    /// 现在：**整局只保留一份** Body 列表，每 2 秒重建一次，且只保留本地玩家 25 米内的角色。
    /// 三个模块各自读这份缓存（它们自己的节拍是错开的），所以一次扫描供三个用。
    ///
    /// 另外做自降级：某次重建耗时超过 <see cref="SlowMs"/>，就把间隔翻倍（最多 8 秒），
    /// 免得"贴身巡检"本身成为卡顿源。
    /// </summary>
    internal static class NearbyBodies
    {
        /// <summary>只关心身边这么近的角色（物品贴合/贴图残留都只可能发生在近处）。</summary>
        internal const float Radius = 25f;

        private const float BaseInterval = 2f;
        private const float SlowMs = 1.5f;
        private const float MaxInterval = 8f;

        private static readonly List<Body> List = new List<Body>(32);
        private static float _next;
        private static float _interval = BaseInterval;
        private static float _lastCostMs;
        private static float _lastLogAt = -99f;

        /// <summary>上次扫描耗时（毫秒），给状态页/日志用。</summary>
        internal static float LastCostMs => _lastCostMs;

        /// <summary>取缓存的附近角色列表；到点会重建一次。返回的列表只读，别改。</summary>
        internal static IReadOnlyList<Body> Get()
        {
            float now = Time.unscaledTime;
            if (now < _next) return List;
            _next = now + _interval;

            float t0 = Time.realtimeSinceStartup;
            try
            {
                List.Clear();
                Body local = Game.LocalBody;
                Vector2 at = local != null ? (Vector2)local.transform.position : Vector2.zero;
                Body[] all = UnityEngine.Object.FindObjectsOfType<Body>();
                for (int i = 0; i < all.Length; i++)
                {
                    Body b = all[i];
                    if (b == null) continue;
                    if (local != null && Vector2.Distance(b.transform.position, at) > Radius) continue;
                    List.Add(b);
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("附近角色扫描失败：" + ex.Message);
            }

            _lastCostMs = (Time.realtimeSinceStartup - t0) * 1000f;

            // 自降级：扫描本身太贵就拉长间隔（只加不减，本局内不再试探回去）
            if (_lastCostMs > SlowMs && _interval < MaxInterval)
            {
                _interval = Mathf.Min(MaxInterval, _interval * 2f);
                if (Time.unscaledTime - _lastLogAt > 30f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Info($"[巡检] 附近角色扫描耗时 {_lastCostMs:0.0}ms，扫描间隔放宽到 {_interval:0.#}s（避免巡检本身拖帧）。");
                }
            }

            return List;
        }
    }
}
