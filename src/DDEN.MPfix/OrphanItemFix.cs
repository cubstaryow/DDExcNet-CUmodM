using System;
using System.Collections.Generic;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「有件真实物品一直卡在玩家中心：能打开内容、扫描也能看到，但既不算穿戴也不在背包里」。
    ///
    /// 成因：Together 用 <c>HeldSeat</c> 把物品“塞”进某个 <c>InventorySlot</c>（父级 = 槽位、
    /// localPosition = 0），靠它来表现“远端玩家的手/背包”。某些情况下槽位记账没对上
    /// （物品被搬走但槽位还指着它、或客户端本地预测留下的孤儿），结果就是：
    /// 物品仍然挂在身体下面 → 跟着人走、正好在身体中心；但 <c>body.GetItem(slot)</c> /
    /// <c>GetAllWearables()</c> 里都没有它 → 界面显示“没穿戴”。
    ///
    /// 修法（只做“归位”，不删除任何东西）：
    ///  每 2 秒检查附近身体下挂着的 Item：
    ///   - 父级里没有别的 Item（排除容器内容物）；
    ///   - 不属于该身体任何槽位、也不在穿戴列表里；
    ///  连续两次（≥2.5 秒）都成立 → 判为孤儿：
    ///    ① 先试 <c>body.AutoPickUpItem(item)</c>，能正常进背包就结束（悬浮消失、物品还在）；
    ///    ② 进不去（背包满/不可拿）→ 从身上摘下来放回地面（<c>SetParent(null)</c> + 打开物理），
    ///       走 <see cref="ItemPhysicsGuard"/> 的保险，保证它正常落地而不是掉进墙里。
    ///
    /// 全程 try/catch；每局最多处理 <c>联机修正/MaxOrphanFixPerSession</c> 次；只碰“确实不属于身体”的物品。
    /// </summary>
    internal static class OrphanItemFix
    {
        private const float GraceSeconds = 2.5f;

        private static readonly Dictionary<int, float> Pending = new Dictionary<int, float>();
        private static int _fixed;
        private static float _nextScan;
        private static float _lastLogAt = -99f;

        internal static void Tick()
        {
            if (!MPfixPlugin.CfgOrphanItemFix.Value) return;

            try
            {
                if (!Mp.IsRunning) return;      // 单机也会有残留，但风险收益比不值；只在联机跑

                float now = Time.unscaledTime;
                if (now < _nextScan) return;
                _nextScan = now + 2f;

                // 只处理“自己身上”的孤儿物品：别人的身体在本地只是远端表现，
                // 去动他们的槽位容易和 Together 的 HeldSeat 打架。
                Body local = Game.LocalBody;
                if (local == null) return;
                ScanBody(local, now);
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("孤儿物品巡检失败：" + ex.Message);
            }
        }

        private static void ScanBody(Body body, float now)
        {
            if (_fixed >= MPfixPlugin.CfgOrphanFixMax.Value) return;

            Item[] items;
            try { items = body.GetComponentsInChildren<Item>(true); }
            catch { return; }
            if (items == null || items.Length == 0) return;

            List<Item> worn = null;
            try { worn = body.GetAllWearables(); } catch { worn = null; }

            for (int i = 0; i < items.Length; i++)
            {
                Item it = items[i];
                if (it == null) continue;

                Transform parent = it.transform.parent;
                if (parent == null) continue;                              // 自由物品，不归我们管
                if (parent.GetComponentInParent<Item>() != null) continue; // 容器内容物
                if (IsOwnedByBody(body, it, worn)) continue;               // 正常穿戴/手持/在槽位

                int key = it.GetInstanceID();
                float since;
                if (!Pending.TryGetValue(key, out since))
                {
                    if (Pending.Count > 512) Pending.Clear();
                    Pending[key] = now;
                    continue;
                }
                if (now - since < GraceSeconds) continue;

                Pending.Remove(key);
                Resolve(body, it);
            }
        }

        private static bool IsOwnedByBody(Body body, Item item, List<Item> worn)
        {
            try
            {
                if (worn != null)
                {
                    for (int i = 0; i < worn.Count; i++)
                    {
                        if (worn[i] == item) return true;
                    }
                }

                if (body.slots != null)
                {
                    for (int i = 0; i < body.slots.Length; i++)
                    {
                        if (body.slots[i] == null) continue;
                        if (body.GetItem(i) == item) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("判定物品归属失败：" + ex.Message);
                return true;   // 判不了就当“属于”，宁可不处理
            }
            return false;
        }

        private static void Resolve(Body body, Item item)
        {
            if (item == null || body == null) return;

            // ① 先试正常拾取：成功就说明它本来就该在背包里，悬浮自然消失
            try
            {
                body.AutoPickUpItem(item);
                if (IsOwnedByBody(body, item, body.GetAllWearables()))
                {
                    _fixed++;
                    LogOnce($"[孤儿物品] {item.id} 卡在 {body.name} 身上但不在背包/穿戴里，已重新拾取归位。");
                    return;
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("孤儿物品重新拾取失败：" + ex.Message);
            }

            // ② 拾取不了 → 从身上摘下来，放回地面（物品本体保留）
            try
            {
                item.transform.SetParent(null, true);
                item.transform.localScale = Vector3.one;
                ItemPhysicsGuard.Guard(item, "orphan-detach");

                _fixed++;
                LogOnce($"[孤儿物品] {item.id} 无法放回背包，已从 {body.name} 身上摘下并放回地面。");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn($"[孤儿物品] 摘除 '{item.id}' 失败，已跳过：{ex.Message}");
            }
        }

        private static void LogOnce(string message)
        {
            if (Time.unscaledTime - _lastLogAt < 1f) return;
            _lastLogAt = Time.unscaledTime;
            MPfixPlugin.Log.Info(message);
        }
    }
}
