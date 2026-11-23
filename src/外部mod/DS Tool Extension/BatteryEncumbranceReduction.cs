using UnityEngine;

namespace DsToolExtension
{
    /// <summary>
    /// 装电池的容器减重（与重力袋一致）：有电时容器负重倍率 0，没电/没电池时恢复为 1。
    /// 不会爆炸，耐久归零也不销毁（由物品定义决定）。
    /// </summary>
    public class BatteryEncumbranceReduction : MonoBehaviour
    {
        private Item item;
        private Container container;

        private void Awake()
        {
            item = GetComponent<Item>();
            container = GetComponent<Container>();
        }

        private void Update()
        {
            if (item == null || container == null) return;
            float target = (item.battery != null && item.battery.hasCharge) ? 0f : 1f;
            if (Mathf.Abs(container.encumberanceMult - target) > 0.0001f)
            {
                container.encumberanceMult = target;
            }
        }
    }
}
