using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vex.Island
{
    public sealed class IslandApp : MonoBehaviour
    {
        const int ShownFps = 15;
        const int HiddenFps = 10;

        readonly IslandHost _host = new IslandHost();

        Label _clock;
        Label _status;
        Label _brand;
        VisualElement _pill;
        VisualElement _dot;
        float _nextClock;
        bool _dragging;
        bool _dirty;
        bool _dragSession;
        float _armedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = HiddenFps;
            QualitySettings.vSyncCount = 0;
            Screen.fullScreen = false;
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(IslandWindow.Width, IslandWindow.Height, FullScreenMode.Windowed);

            var cam = Camera.main;
            if (cam != null)
                cam.enabled = false;

            var go = new GameObject("IslandApp");
            DontDestroyOnLoad(go);
            go.AddComponent<IslandApp>();
        }

        void Start()
        {
            if (!BuildHud())
            {
                Debug.LogError("Island UITK assets missing");
                return;
            }

            IslandWindow.Apply();
            IslandWindow.ArmEdges();
            Snap(false);
            Application.targetFrameRate = HiddenFps;
            _armedAt = Time.unscaledTime + 0.8f;
            _host.Changed += () => _dirty = true;
            _host.Start();
            Paint();
        }

        void OnDestroy()
        {
            _host.Stop();
        }

        void Update()
        {
            _host.Pump();
            if (_dirty)
            {
                _dirty = false;
                Paint();
                Snap(HasFile());
            }

            if (!HasFile())
            {
                IslandWindow.ClearShape();
                IslandWindow.SetVisible(false);
            }

            var dropped = IslandWindow.PollDrop();
            var live = Time.unscaledTime >= _armedAt && IslandWindow.DragLive();
            if (dropped != null && dropped.Length > 0)
            {
                _host.ShowFiles(dropped);
                RememberDrop(dropped);
                _dragSession = true;
                Paint();
                Snap(true);
                _dirty = false;
            }
            else if (_dragSession && !live)
            {
                _host.Dismiss();
                _dragSession = false;
                Paint();
                Snap(false);
                _dirty = false;
            }

            if (Time.unscaledTime >= _nextClock)
            {
                _nextClock = Time.unscaledTime + 1f;
                if (_host.Mode == IslandMode.Idle && _clock != null)
                    _clock.text = DateTime.Now.ToString("HH:mm:ss");
            }

            if (_dragging)
            {
                if (!Input.GetMouseButton(0))
                {
                    _dragging = false;
                    IslandWindow.EndDrag();
                    if (_host.Visible)
                        IslandWindow.PlaceDropTarget(IslandWindow.X, IslandWindow.Y);
                    RestFps();
                }
                else
                    IslandWindow.Drag();
            }

            if (IslandWindow.WantQuit() || Input.GetKeyDown(KeyCode.Escape))
            {
                if (_host.Visible || _dragSession)
                {
                    _host.Dismiss();
                    _dragSession = false;
                    Paint();
                    Snap(false);
                    _dirty = false;
                }
            }

            if (_host.ShouldQuit)
            {
                Application.Quit();
#if !UNITY_EDITOR
                System.Environment.Exit(0);
#endif
            }

#if !UNITY_EDITOR
            if (!IslandWindow.Applied && Time.frameCount < 30 && Time.frameCount % 5 == 0)
            {
                IslandWindow.Apply();
                IslandWindow.ArmEdges();
                if (!_host.Visible)
                    Snap(false);
            }
#endif
        }

        bool HasFile()
        {
            return _host.Visible && _host.Mode == IslandMode.Files && _host.Files.Count > 0;
        }

        void Paint()
        {
            if (_pill == null)
                return;

            var show = HasFile();
            _pill.style.visibility = show
                ? UnityEngine.UIElements.Visibility.Visible
                : UnityEngine.UIElements.Visibility.Hidden;

            if (show)
            {
                _pill.EnableInClassList("files", true);
                _dot.EnableInClassList("files", true);
                var n = _host.Files.Count;
                _status.text = n == 1 ? "1 file" : n + " files";
                _clock.text = ShortName(_host.Files[0]);
                _brand.text = n > 1 ? "+" + (n - 1) : "open";
            }
            else
            {
                _pill.EnableInClassList("files", false);
                _dot.EnableInClassList("files", false);
                _status.text = IslandWindow.StatusLabel;
                _clock.text = DateTime.Now.ToString("HH:mm:ss");
                _brand.text = "Island";
            }
        }

        void Snap(bool show)
        {
            int px, py;
            if (!IslandWindow.TryPointer(out px, out py))
            {
                px = IslandWindow.X + IslandWindow.Width / 2;
                py = IslandWindow.Y + IslandWindow.Height / 2;
            }

            var screens = IslandWindow.QueryScreens();
            _host.PickOuterEdge(screens, px);
            var shown = _host.ShownPlacement(screens, px, py);
            if (show)
            {
                if (_pill != null)
                    _pill.style.visibility = UnityEngine.UIElements.Visibility.Visible;
                IslandWindow.SetVisible(true);
                IslandWindow.Move(shown.X, shown.Y);
                IslandWindow.ApplyShape();
                IslandWindow.PlaceDropTarget(shown.X, shown.Y);
            }
            else
            {
                // Don't XUnmap (Unity remaps onto a monitor). Don't rely
                // on off-screen coords (XWayland/Mutter clamp to the desk).
                // Empty XShape = no pixels, so no smear if Unity remaps.
                if (_pill != null)
                    _pill.style.visibility = UnityEngine.UIElements.Visibility.Hidden;
                IslandWindow.OverlayHide();
                IslandWindow.ClearShape();
                IslandWindow.SetVisible(false);
            }
            RestFps();
        }

        void RestFps()
        {
            if (_dragging)
                return;
            Application.targetFrameRate = _host.Visible ? ShownFps : HiddenFps;
        }

        static void RememberDrop(string[] paths)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Application.persistentDataPath, "last-drop.txt"),
                    string.Join("\n", paths) + "\n");
            }
            catch
            {
            }
        }

        static string ShortName(string path)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
                name = path;
            if (name.Length > 18)
                return name.Substring(0, 16) + "…";
            return name;
        }

        bool BuildHud()
        {
            var tree = Resources.Load<VisualTreeAsset>("Island");
            var settings = Resources.Load<PanelSettings>("IslandPanel");
            if (tree == null || settings == null)
                return false;

            var ui = gameObject.AddComponent<UIDocument>();
            ui.panelSettings = settings;
            ui.visualTreeAsset = tree;

            var root = ui.rootVisualElement;
            if (root == null)
                return false;

            _clock = root.Q<Label>("clock");
            _status = root.Q<Label>("status");
            _brand = root.Q<Label>("brand");
            _pill = root.Q("pill") ?? root;
            _dot = root.Q("dot");

            _pill.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                _dragging = true;
                Application.targetFrameRate = 60;
                IslandWindow.BeginDrag();
                e.StopPropagation();
            });
            return true;
        }
    }
}
