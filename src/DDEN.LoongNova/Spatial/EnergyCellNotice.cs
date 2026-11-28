using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class EnergyCellNotice : MonoBehaviour
    {
        private Talker _talker;
        private float _next;
        internal static void Show(Body body)
        {
            if (body == null || PlayerCamera.main == null || PlayerCamera.main.body != body) return;
            var notice = body.GetComponent<EnergyCellNotice>() ?? body.gameObject.AddComponent<EnergyCellNotice>();
            notice.Display(body);
        }
        private void Display(Body body)
        {
            if (Time.unscaledTime < _next || body.limbs == null || body.limbs.Length == 0 || body.limbs[(int)Body.LimbNum.Head] == null) return;
            _next = Time.unscaledTime + 1;
            var head = body.limbs[(int)Body.LimbNum.Head];
            if (_talker == null)
            {
                var template = Resources.Load<GameObject>("watch")?.GetComponent<Talker>();
                if (template == null || template.textPrefab == null) return;
                _talker = new GameObject("LoongNova cell notice").AddComponent<Talker>();
                _talker.textPrefab = template.textPrefab; _talker.talkSoundCustom = template.talkSoundCustom;
                _talker.talkTimeMult = template.talkTimeMult; _talker.isSoundGlobal = template.isSoundGlobal;
                var text = UnityEngine.Object.Instantiate(template.textPrefab, head.transform.position, Quaternion.identity);
                AccessTools.Field(typeof(Talker), "text").SetValue(_talker, text.GetComponent(AccessTools.TypeByName("TMPro.TextMeshPro")));
            }
            _talker.transform.SetParent(head.transform, false); _talker.transform.localPosition = Vector3.zero;
            _talker.TalkDelayed(0, Registration.Text("rechargerActivationNeed"));
        }
        private void OnDestroy()
        {
            if (_talker == null) return;
            var text = Reflect.GetInstance(_talker, "text") as Component;
            UnityEngine.Object.Destroy(_talker.gameObject);
            if (text != null) UnityEngine.Object.Destroy(text.gameObject, 0.1f);
        }
    }
}
