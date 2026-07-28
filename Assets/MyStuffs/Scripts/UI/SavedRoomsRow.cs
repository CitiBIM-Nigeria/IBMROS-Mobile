using IBMROS.Designer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Populates the main screen's "My Rooms" row (MainAppScreen.uxml already
/// ships the section: RoomsRow + AddRoomButton + RoomsEmptyHint — it was
/// simply never fed). Saved plans render newest-first after the New Room
/// card; tapping one opens it in the designer. Rebuilt every time the
/// MainApp screen is shown, so rooms saved in the designer appear on return.
/// </summary>
public class SavedRoomsRow : MonoBehaviour
{
    private VisualElement _row;
    private VisualElement _emptyHint;
    private bool _bound;

    private void OnEnable()
    {
        UIManager.OnScreensReady += Bind;
        ScreenNavigator.OnScreenChanged += OnScreenChanged;
        if (UIManager.Instance != null && UIManager.Instance.IsReady)
            Bind();
    }

    private void OnDisable()
    {
        UIManager.OnScreensReady -= Bind;
        ScreenNavigator.OnScreenChanged -= OnScreenChanged;
    }

    private void OnScreenChanged(ScreenName screen)
    {
        if (screen == ScreenName.MainApp && _bound)
            Refresh();
    }

    private void Bind()
    {
        if (_bound)
            return;
        VisualElement container = UIManager.Instance.GetScreenContainer(ScreenName.MainApp);
        if (container == null)
            return;
        _row = container.Q<VisualElement>("RoomsRow");
        _emptyHint = container.Q<VisualElement>("RoomsEmptyHint");
        ScrollView scroll = container.Q<ScrollView>("RoomsScroll");
        if (scroll != null)
        {
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        }
        if (_row == null)
        {
            Debug.LogError("[SavedRoomsRow] RoomsRow missing from MainAppScreen.uxml.");
            return;
        }
        _bound = true;
        Refresh();
    }

    private void Refresh()
    {
        // Everything after the AddRoomButton (index 0) is ours to rebuild.
        while (_row.childCount > 1)
            _row.RemoveAt(_row.childCount - 1);

        string dir = System.IO.Path.Combine(Application.persistentDataPath, "FloorMaps");
        var plans = new System.Collections.Generic.List<System.IO.FileInfo>();
        if (System.IO.Directory.Exists(dir))
        {
            foreach (string f in System.IO.Directory.GetFiles(dir, "*.json"))
            {
                if (f.EndsWith(".furniture.json") || f.Contains(".json.bak"))
                    continue;
                plans.Add(new System.IO.FileInfo(f));
            }
        }
        plans.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

        if (_emptyHint != null)
            _emptyHint.style.display = plans.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

        int shown = 0;
        foreach (System.IO.FileInfo fi in plans)
        {
            if (shown++ >= 12)
                break;
            string name = System.IO.Path.GetFileNameWithoutExtension(fi.Name);

            var card = new Button();
            card.AddToClassList("main-room-card");
            card.style.flexShrink = 0;
            card.style.justifyContent = Justify.Center;

            var title = new Label(name);
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(title);

            var when = new Label(fi.LastWriteTime.ToString("d MMM, HH:mm"));
            when.style.fontSize = 11;
            when.style.opacity = 0.6f;
            when.style.marginTop = 4;
            when.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(when);

            string planName = name;
            card.clicked += () =>
            {
                Debug.Log($"[SavedRoomsRow] Opening saved plan '{planName}'.");
                RoomDesignLaunch.SetLoadPlan(planName);
                SceneManager.LoadScene("RoomDesigner");
            };
            _row.Add(card);
        }
    }
}
