using UnityEngine;

namespace DsToolExtension
{
    /// <summary>
    /// 重型钻机同款循环音效（Resources 里的 drillLoop）。
    /// 性能优化版：缓存 InventorySlot 与本地 Body，状态每 0.2 秒评估一次，避免每帧 GetComponent/查找。
    /// 本地玩家手持且攻击时正常播放；其他玩家在 12 米内以较低音量播放。
    /// </summary>
    public class DrillLoopSound : MonoBehaviour
    {
        private const float OtherPlayerRange = 12f;
        private const float OtherPlayerVolume = 0.5f;
        private const float LocalRefreshInterval = 0.5f;
        private const float EvaluateInterval = 0.2f;

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

        private void OnTransformParentChanged()
        {
            RefreshSlot();
        }

        private void RefreshSlot()
        {
            slot = transform.parent != null ? transform.parent.GetComponent<InventorySlot>() : null;
        }

        /// <summary>优先从原生 heavydrill prefab 复制 AudioSource.clip，失败再回退 Resources 路径。</summary>
        private static AudioClip LoadDrillClip()
        {
            try
            {
                GameObject prefab = Resources.Load<GameObject>("heavydrill");
                if (prefab != null)
                {
                    AudioSource prefabSource = prefab.GetComponent<AudioSource>();
                    if (prefabSource != null && prefabSource.clip != null)
                        return prefabSource.clip;
                }
            }
            catch { }
            return Resources.Load<AudioClip>("drillLoop");
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
