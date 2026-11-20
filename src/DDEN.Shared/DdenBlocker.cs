using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDEN.Shared
{
    /// <summary>
    /// 模态遮罩：跟 Together 的界面一个路子 —— 在自己的高层 Canvas 上放一张全屏透明 Image（raycastTarget=true）。
    /// uGUI 的射线从上往下打，先命中这张遮罩，游戏自己的按钮（开始/退出…）就收不到点击了；
    /// EventSystem 仍然正常工作，所以其它模组的 uGUI 界面不会被“关掉”，只是被压在遮罩下面。
    /// IMGUI 面板不受影响（IMGUI 与 uGUI 是两套输入）。
    /// </summary>
    public static class DdenBlocker
    {
        private const int SortingOrder = 32000;

        private static GameObject _root;
        private static Image _image;
        private static bool _failed;

        public static bool Available => _root != null;

        public static void Show(float dimAlpha = 0f)
        {
            try
            {
                Ensure();
                if (_root == null) return;
                if (!_root.activeSelf) _root.SetActive(true);
                if (_image != null)
                {
                    Color c = _image.color;
                    c.a = Mathf.Clamp01(dimAlpha);
                    _image.color = c;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("DdenBlocker.Show 失败：" + ex.Message);
            }
        }

        public static void Hide()
        {
            try
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
            }
            catch (Exception ex)
            {
                Log.Debug("DdenBlocker.Hide 失败：" + ex.Message);
            }
        }

        public static void Destroy()
        {
            try
            {
                if (_root != null) UnityEngine.Object.Destroy(_root);
            }
            catch { /* ignored */ }
            _root = null;
            _image = null;
            _failed = false;
        }

        private static void Ensure()
        {
            if (_root != null || _failed) return;

            try
            {
                _root = new GameObject("DDEN_InputBlocker");
                UnityEngine.Object.DontDestroyOnLoad(_root);

                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;

                _root.AddComponent<CanvasScaler>();
                _root.AddComponent<GraphicRaycaster>();

                var imgGo = new GameObject("Blocker");
                imgGo.transform.SetParent(_root.transform, false);

                _image = imgGo.AddComponent<Image>();
                _image.raycastTarget = true;
                _image.color = new Color(0f, 0f, 0f, 0f);   // 默认全透明，仅吃射线

                RectTransform rt = imgGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                _root.SetActive(false);
                Log.Info("[Blocker] 已创建 uGUI 模态遮罩（面板打开时吃掉鼠标点击，游戏按钮不再被点穿）。");
            }
            catch (Exception ex)
            {
                _failed = true;
                _root = null;
                Log.Warn("[Blocker] 创建失败，将不拦截游戏点击：" + ex.Message);
            }
        }
    }
}
