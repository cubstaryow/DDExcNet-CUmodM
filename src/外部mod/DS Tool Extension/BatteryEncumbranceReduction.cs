using UnityEngine;

namespace DsToolExtension
{
    /// <summary>
    /// 装电池的容器减重（与重力袋一致）：有电减 95% 负重（0.05），没电/没电池不减（1）。
    /// 性能优化版：每 0.5 秒检查一次，不每帧写容器字段。不会爆炸，耐久归零也不销毁。
    /// </summary>
    public class BatteryEncumbranceReduction : MonoBehaviour
    {
        private const float CheckInterval = 0.5f;

        private Item item;
        private Container container;
        private float nextCheck;

        private void Awake()
        {
            item = GetComponent<Item>();
            container = GetComponent<Container>();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckInterval;
            if (item == null || container == null) return;
            float target = (item.battery != null && item.battery.hasCharge) ? 0.05f : 1f;   // 有电减 95%，没电/没电池不减
            if (Mathf.Abs(container.encumberanceMult - target) > 0.0001f)
            {
                container.encumberanceMult = target;
            }
        }
    }
}
