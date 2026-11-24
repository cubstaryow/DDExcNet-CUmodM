using System.Collections.Generic;
using UnityEngine;

namespace DsToolExtension
{
    /// <summary>
    /// 重型钻机同款循环音效（Resources 里的 drillLoop）。
    /// 性能优化版：缓存 clip / InventorySlot / 本地 Body，状态每 0.2 秒评估一次；
    /// 不在玩家槽位时禁用自身（每 1 秒由 Plugin 兜底重新检查），静默时 Update 早退。
    /// </summary>
    public class DrillLoopSound : MonoBehaviour
    {
        private const float OtherPlayerRange = 12f;
        private const float OtherPlayerVolume = 0.5f;
        private const float LocalRefreshInterval = 0.5f;
        private const float EvaluateInterval = 0.2f;

        private static readonly List<DrillLoopSound> All = new List<DrillLoopSound>();
        private static AudioClip _cachedClip;

        private Item item;
        private AudioSource source;
        private InventorySlot slot;
        private Body local;
        private float nextLocalRefresh;
        private float nextEvaluate;
        private float nextLog;
        private bool on;
        private float targetVolume = 1f;

        private void Awake()
        {
            All.Add(this);
            item = GetComponent<Item>();
            source = gameObject.AddComponent<AudioSource>();
            source.clip = LoadDrillClip();
            source.loop = true;
            source.volume = 0f;
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 2D：物品在背包槽位时 3D 坐标不可靠，避免声音过小
            source.minDistance = 1f;
            source.maxDistance = 500f;
            RefreshSlot();
            if (source.clip == null)
                Plugin.LogWarning("[DS Tool Extension] DrillLoopSound: 未找到音效 'drillLoop'。");
            else if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.LogInfo($"[DS Tool Extension] DrillLoopSound ready: clip={source.clip.name}, item={(item != null ? item.id : "null")}");
        }

        private void OnDestroy()
        {
            All.Remove(this);
        }

        private void OnTransformParentChanged()
        {
            RefreshSlot();
        }

        private void OnEnable()
        {
            // 被重新启用时刷新一次槽位，避免 Parent 变化未触发时长期失效。
            if (slot == null) RefreshSlot();
        }

        private void RefreshSlot()
        {
            slot = transform.parent != null ? transform.parent.GetComponent<InventorySlot>() : null;
            // 不在任何玩家的槽位里（地上/普通容器）就没有播放意义，直接停掉 Update。
            enabled = slot != null && slot.body != null;
        }

        internal static void SweepAll()
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                DrillLoopSound s = All[i];
                if (s == null)
                {
                    All.RemoveAt(i);
                    continue;
                }
                if (!s.enabled) s.RefreshSlot();
            }
        }

        /// <summary>静态缓存：所有钻机共享同一个 drillLoop clip；资源被卸载后重新加载。</summary>
        private static AudioClip LoadDrillClip()
        {
            if (_cachedClip != null) return _cachedClip;
            try
            {
                GameObject prefab = Resources.Load<GameObject>("heavydrill");
                if (prefab != null)
                {
                    AudioSource prefabSource = prefab.GetComponent<AudioSource>();
                    if (prefabSource != null && prefabSource.clip != null)
                        return _cachedClip = prefabSource.clip;
                }
            }
            catch { }
            _cachedClip = Resources.Load<AudioClip>("drillLoop");
            return _cachedClip;
        }

        private void Update()
        {
            if (source == null) return;

            float now = Time.unscaledTime;
            if (now >= nextLocalRefresh)
            {
                nextLocalRefresh = now + LocalRefreshInterval;
                local = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            }
            if (now >= nextEvaluate)
            {
                nextEvaluate = now + EvaluateInterval;
                Evaluate();
            }
            if (!on && source.volume <= 0f && !source.isPlaying) return;

            if (on)
            {
                if (!source.isPlaying) source.Play();
                source.pitch = Mathf.MoveTowards(source.pitch, 1f, Time.deltaTime * 3f);
                source.volume = Mathf.MoveTowards(source.volume, targetVolume, Time.deltaTime * 3f);
            }
            else
            {
                source.pitch = Mathf.MoveTowards(source.pitch, 0.2f, Time.deltaTime * 1.3f);
                source.volume = Mathf.MoveTowards(source.volume, 0f, Time.deltaTime * 1.3f);
                if (source.volume <= 0f && source.isPlaying)
                {
                    source.Stop();
                }
            }

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value && now >= nextLog)
            {
                nextLog = now + 5f;
                bool held = slot != null && slot.body != null && slot.body.GetItem(slot.body.handSlot) == item;
                float dist = (local != null && slot != null && slot.body != null)
                    ? Vector2.Distance(slot.body.transform.position, local.transform.position) : -1f;
                Plugin.LogInfo($"[DS Tool Extension] DrillLoopSound: on={on} held={held} playing={source.isPlaying} vol={source.volume:0.00} dist={dist:0.0}");
            }
        }

        private void Evaluate()
        {
            on = false;
            if (item == null || local == null || slot == null || slot.body == null) return;
            Body owner = slot.body;
            if (owner.GetItem(owner.handSlot) != item || owner.attackCooldown <= -0.1f) return;
            if (WorldGeneration.world != null && WorldGeneration.world.generatingWorld) return;

            if (owner == local)
            {
                on = true;
                targetVolume = 1f;
            }
            else if (Vector2.Distance(owner.transform.position, local.transform.position) <= OtherPlayerRange)
            {
                on = true;
                targetVolume = OtherPlayerVolume;
            }
        }
    }
}
