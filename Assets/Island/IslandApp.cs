using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vex.Island
{
    public sealed class IslandApp : MonoBehaviour
    {
        const int IdleFps = 15;
        const float SlideSeconds = 0.22f;

        readonly IslandHost _host = new IslandHost();

        Label _clock;
        Label _status;
        Label _brand;
        VisualElement _pill;
        VisualElement _dot;
        float _nextClock;
        bool _dragging;
        bool _dirty = true;
        float _slide;
        IslandPlacement _from;
        IslandPlacement _to;
        bool _sliding;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = IdleFps;
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
            _host.Changed += () => _dirty = true;
            _host.Start();
            SnapTo(_host.Visible);
            Paint();
        }

        void OnDestroy()
        {
            _host.Stop();
        }

        void Update()
        {
            _host.Pump();

            if (Time.unscaledTime >= _nextClock)
            {
                _nextClock = Time.unscaledTime + 1f;
                if (_host.Mode == IslandMode.Idle && _clock != null)
                    _clock.text = DateTime.Now.ToString("HH:mm:ss");
            }

            if (_dirty)
            {
                _dirty = false;
                Paint();
                BeginSlide(_host.Visible);
            }

            if (_sliding)
                TickSlide();

            if (_dragging)
            {
                if (!Input.GetMouseButton(0))
                {
                    _dragging = false;
                    IslandWindow.EndDrag();
                    Application.targetFrameRate = IdleFps;
                }
                else
                    IslandWindow.Drag();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                Application.Quit();

#if !UNITY_EDITOR
            if (!IslandWindow.Applied && Time.frameCount < 30 && Time.frameCount % 5 == 0)
                IslandWindow.Apply();
#endif
        }

        void Paint()
        {
            if (_pill == null)
                return;

            if (_host.Mode == IslandMode.Files && _host.Files.Count > 0)
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

        void BeginSlide(bool show)
        {
            int px, py;
            if (!IslandWindow.TryPointer(out px, out py))
            {
                px = IslandWindow.X + IslandWindow.Width / 2;
                py = IslandWindow.Y + IslandWindow.Height / 2;
            }

            var screens = IslandWindow.QueryScreens();
            _from = new IslandPlacement(IslandWindow.X, IslandWindow.Y, _host.Edge,
                _host.ShownPlacement(screens, px, py).Bound);
            _to = show ? _host.ShownPlacement(screens, px, py) : _host.HiddenPlacement(screens, px, py);
            _slide = 0f;
            _sliding = true;
            Application.targetFrameRate = 60;
        }

        void TickSlide()
        {
            _slide += Time.unscaledDeltaTime / SlideSeconds;
            var t = _slide >= 1f ? 1f : EaseOut(_slide);
            var x = Mathf.RoundToInt(Mathf.Lerp(_from.X, _to.X, t));
            var y = Mathf.RoundToInt(Mathf.Lerp(_from.Y, _to.Y, t));
            IslandWindow.Move(x, y);
            if (_slide >= 1f)
            {
                _sliding = false;
                if (!_dragging)
                    Application.targetFrameRate = IdleFps;
            }
        }

        void SnapTo(bool show)
        {
            int px, py;
            IslandWindow.TryPointer(out px, out py);
            var p = show
                ? _host.ShownPlacement(IslandWindow.QueryScreens(), px, py)
                : _host.HiddenPlacement(IslandWindow.QueryScreens(), px, py);
            IslandWindow.Move(p.X, p.Y);
        }

        static float EaseOut(float t)
        {
            return 1f - (1f - t) * (1f - t);
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
