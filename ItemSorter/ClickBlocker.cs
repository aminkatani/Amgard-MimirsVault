using UnityEngine;
using UnityEngine.UI;

namespace ItemSorter
{
    /// <summary>
    /// An invisible game-UI panel placed exactly behind the sorter window. The window is drawn with
    /// IMGUI, which doesn't stop clicks from also reaching Valheim's menus; this panel swallows them.
    /// </summary>
    internal static class ClickBlocker
    {
        private static GameObject _root;
        private static RectTransform _blocker;

        internal static void SetVisible(bool visible, Rect imguiRect)
        {
            if (!visible)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                return;
            }

            if (_root == null) Create();
            if (!_root.activeSelf) _root.SetActive(true);

            // IMGUI's origin is top-left, uGUI's is bottom-left.
            _blocker.anchoredPosition = new Vector2(imguiRect.x, Screen.height - imguiRect.y - imguiRect.height);
            _blocker.sizeDelta = new Vector2(imguiRect.width, imguiRect.height);
        }

        private static void Create()
        {
            _root = new GameObject("ItemSorter_ClickBlocker");
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            _root.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Blocker", typeof(RectTransform));
            panel.transform.SetParent(_root.transform, false);
            _blocker = panel.GetComponent<RectTransform>();
            _blocker.anchorMin = _blocker.anchorMax = Vector2.zero;
            _blocker.pivot = Vector2.zero;
            var image = panel.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
        }
    }
}
