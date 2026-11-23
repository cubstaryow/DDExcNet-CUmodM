using UnityEngine;

namespace DsToolExtension
{
    /// <summary>
    /// 重型钻机同款循环音效（Resources 里的 drillLoop）。
    /// 本地玩家手持且攻击时正常播放；其他玩家的钻机在 12 米内以较低音量播放，远处静音。
    /// </summary>
    public class DrillLoopSound : MonoBehaviour
    {
        private const float OtherPlayerRange = 12f;
        private const float OtherPlayerVolume = 0.5f;

        private Item item;
        private AudioSource source;
        private float nextLog;

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
            if (source.clip == null)
                Plugin.LogWarning("[DS Tool Extension] DrillLoopSound: 未找到音效 'drillLoop'。");
            else if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.LogInfo($"[DS Tool Extension] DrillLoopSound ready: clip={source.clip.name}, item={(item != null ? item.id : "null")}");
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

            bool on = false;
            float targetVolume = 1f;
            Transform parent = transform.parent;
            Body local = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (local != null && parent != null && parent.TryGetComponent<InventorySlot>(out var slot) && slot.body != null)
            {
                Body owner = slot.body;
                if (owner.GetItem(owner.handSlot) == item && owner.attackCooldown > -0.1f)
                {
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
            if (WorldGeneration.world != null && WorldGeneration.world.generatingWorld)
            {
                on = false;
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

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 5f;
                bool held = false;
                float dist = -1f;
                if (local != null && parent != null && parent.TryGetComponent<InventorySlot>(out var s) && s.body != null)
                {
                    held = s.body.GetItem(s.body.handSlot) == item;
                    dist = Vector2.Distance(s.body.transform.position, local.transform.position);
                }
                Plugin.LogInfo($"[DS Tool Extension] DrillLoopSound: on={on} held={held} playing={source.isPlaying} vol={source.volume:0.00} dist={dist:0.0} parent={(parent != null ? parent.name : "null")}");
            }
        }
    }
}
