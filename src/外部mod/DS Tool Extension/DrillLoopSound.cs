using UnityEngine;

namespace DsToolExtension
{
    /// <summary>
    /// 重型钻机同款循环音效（Resources 里的 drillLoop）。
    /// 物品被本地玩家握在手里且处于攻击状态时播放，否则渐弱停止——与原生 heavydrill 的 Update 行为一致。
    /// </summary>
    public class DrillLoopSound : MonoBehaviour
    {
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
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            if (source.clip == null)
                Plugin.LogWarning("[DS Tool Extension] DrillLoopSound: 未找到音效 'drillLoop'。");
            else
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
            Transform parent = transform.parent;
            if (parent != null && parent.TryGetComponent<InventorySlot>(out var slot) && slot.body != null)
            {
                Body body = slot.body;
                if (body.GetItem(body.handSlot) == item && body.attackCooldown > -0.1f)
                {
                    on = true;
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
                source.volume = Mathf.MoveTowards(source.volume, 1f, Time.deltaTime * 3f);
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

            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 5f;
                bool held = false;
                if (parent != null && parent.TryGetComponent<InventorySlot>(out var s) && s.body != null)
                    held = s.body.GetItem(s.body.handSlot) == item;
                Plugin.LogInfo($"[DS Tool Extension] DrillLoopSound: on={on} held={held} playing={source.isPlaying} vol={source.volume:0.00} parent={(parent != null ? parent.name : "null")}");
            }
        }
    }
}
