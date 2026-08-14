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
        IslandBench _bench;
        VisualElement _root;
        VisualElement _scrim;
        VisualElement _stage;
        float _nextClock;
        bool _dragging;
        bool _dirty;
        bool _dragSession;
        bool _press;
        bool _wasLive;
        bool _wasPhoto;
        float _armedAt;
        float _nextPreview;
        float _downX, _downY;
        int _hotY;
        bool _shown;
        bool _over;
        float _touched;
        float _shownAt;
        float _fade = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = HiddenFps;
            QualitySettings.vSyncCount = 0;
            Screen.fullScreen = false;
            Screen.fullScreenMode = FullScreenMode.Windowed;
#if UNITY_EDITOR
            Screen.SetResolution(IslandMetrics.OpenWidth, IslandWindow.Height, FullScreenMode.Windowed);
#endif

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
            if (IslandWiggle.Consume())
                OnWiggle();

            var speaking = IslandSpeak.IsLive;
            if (_wasLive != speaking)
            {
                _wasLive = speaking;
                Paint();
            }

            if (_host.Mode == IslandMode.Photo && IslandPhoto.Current.PreviewStale
                && Time.unscaledTime >= _nextPreview)
            {
                _nextPreview = Time.unscaledTime + 0.12f;
                IslandPhoto.Current.RenderPreview();
                _host.Line = IslandPhoto.Current.StampLine();
                Paint();
            }

            if (_dirty)
            {
                _dirty = false;
                Touch();
                if (_bench != null && _bench.Open != _host.Frame.Bench)
                {
                    IslandWindow.ApplyShape(_host.Frame.Bench, _host.Edge);
                    _bench.SetOpen(_host.Frame.Bench);
                }
                _wasPhoto = _host.Frame.OpensBench;
                if (_host.Mode == IslandMode.Photo && IslandPhoto.Current.PreviewStale)
                    IslandPhoto.Current.RenderPreview();
                Paint();
                if (!_dragging)
                    Snap(ShowPill());
            }

            if (!ShowPill() && _shown)
                Snap(false);

            var dropped = IslandWindow.PollDrop();
            var live = Time.unscaledTime >= _armedAt && IslandWindow.DragLive();
            if (dropped != null && dropped.Length > 0)
            {
                _host.TakeDrop(dropped);
                RememberDrop(dropped);
                Touch();
                _dragSession = !IslandSpeak.IsLive && !_host.Frame.Holds;
                if (_host.Frame.OpensBench)
                {
                    OpenBench(true);
                    _wasPhoto = true;
                    IslandPhoto.Current.RenderPreview();
                    _host.Line = IslandPhoto.Current.StampLine();
                }

                Paint();
                Snap(ShowPill());
                _dirty = false;
            }
            else if (_dragSession && !live)
            {
                if (IslandSpeak.IsLive || _host.Frame.Holds)
                    _dragSession = false;
                else
                {
                    _host.Dismiss();
                    _dragSession = false;
                    Paint();
                    Snap(false);
                    _dirty = false;
                }
            }

            if (Time.unscaledTime >= _nextClock)
            {
                _nextClock = Time.unscaledTime + (speaking ? 0.4f : 1f);
                if (speaking || _host.Mode == IslandMode.Speak)
                    Paint();
                else if (_host.Mode == IslandMode.Idle && _clock != null)
                    _clock.text = DateTime.Now.ToString("HH:mm:ss");
            }

            if (_dragging)
            {
                if (!Input.GetMouseButton(0))
                    EndSlide();
                else
                    Slide();
            }

            if (IslandWindow.WantQuit() || Input.GetKeyDown(KeyCode.Escape))
                Retreat();

            TickQuiet();

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

        bool ShowPill()
        {
            if (_bench != null && _bench.Open)
                return true;
            return _host.Frame.Shows;
        }

        void OnWiggle()
        {
            if (IslandSpeak.IsLive)
            {
                _host.SpeakNow("");
            }
            else
            {
                var text = IslandSpeak.Selection();
                if (text.Length == 0)
                    return;
                _host.SpeakNow(text);
            }

            _dragSession = false;
            Touch();
            Paint();
            Snap(true);
            _dirty = false;
        }

        void OpenBench(bool open)
        {
            if (_bench == null || _bench.Open == open)
                return;
            IslandWindow.ApplyShape(open, _host.Edge);
            _host.SetBench(open);
            _bench.SetOpen(open);
        }

        void Retreat()
        {
            if (_dragging)
                EndSlide();
            if (_bench != null && _bench.Open)
            {
                OpenBench(false);
                Touch();
                Paint();
                Snap(ShowPill());
                _dirty = false;
                return;
            }

            Vanish();
        }

        void Vanish()
        {
            if (_dragging)
                EndSlide();
            if (!_host.Visible && !_dragSession && (_bench == null || !_bench.Open))
                return;
            _host.Dismiss();
            _dragSession = false;
            OpenBench(false);
            _wasPhoto = false;
            _over = false;
            Paint();
            Snap(false);
            _dirty = false;
        }

        void Touch()
        {
            _touched = Time.unscaledTime;
            if (_shown && _fade < 1f)
                ApplyFade(1f);
        }

        bool Held()
        {
            if (_dragging || _press || _dragSession || _over)
                return true;
            if (IslandSpeak.IsLive)
                return true;
            if (IslandWindow.DragLive())
                return true;
            return _bench != null && _bench.Editing;
        }

        void TickQuiet()
        {
            if (!ShowPill())
            {
                ApplyFade(1f);
                return;
            }

            if (Held())
                _touched = Time.unscaledTime;

            var wait = IslandQuiet.Wait(_bench != null && _bench.Open);
            var phase = IslandQuiet.Of(true, Time.unscaledTime - _shownAt, Time.unscaledTime - _touched, wait, Held());
            ApplyFade(IslandQuiet.Opacity(phase, Time.unscaledTime - _shownAt, Time.unscaledTime - _touched, wait));
            if (phase == IslandQuiet.Phase.Appearing || phase == IslandQuiet.Phase.Fading)
                Application.targetFrameRate = 60;
            else
                RestFps();
            if (phase == IslandQuiet.Phase.Gone)
                Vanish();
        }

        void ApplyFade(float op)
        {
            if (op < 0f)
                op = 0f;
            if (op > 1f)
                op = 1f;
            if (op == _fade)
                return;
            _fade = op;
            if (_stage != null)
                _stage.style.opacity = op;
            if (_scrim != null)
                _scrim.style.opacity = op;
        }

        void ToggleBench()
        {
            if (_bench == null)
                return;
            OpenBench(!_bench.Open);
            Paint();
            Snap(ShowPill());
        }

        void Paint()
        {
            if (_pill == null)
                return;

            var show = ShowPill();
            _pill.style.visibility = show
                ? UnityEngine.UIElements.Visibility.Visible
                : UnityEngine.UIElements.Visibility.Hidden;

            var ctx = _host.Context;
            var cls = KindClass(ctx);
            SetKindClass(_pill, cls);
            SetKindClass(_dot, cls);
            if (_bench != null)
            {
                _bench.SetEdge(_host.Edge);
                _bench.Paint(_host);
            }

            if (show)
            {
                var live = IslandSpeak.Status();
                _status.text = live.Length > 0
                    ? live
                    : (string.IsNullOrEmpty(_host.LastNote) ? ctx.Detail : _host.LastNote);
                _clock.text = _host.Mode == IslandMode.Photo
                    ? IslandPhoto.Current.Effective().ToString("MMM d")
                    : ctx.Label;
                _brand.text = ctx.Count > 1 ? "+" + (ctx.Count - 1) : IslandSense.LabelOf(ctx.Kind);
            }
            else
            {
                _status.text = IslandWindow.StatusLabel;
                _clock.text = DateTime.Now.ToString("HH:mm:ss");
                _brand.text = "Island";
            }
        }

        void Snap(bool show)
        {
            if (_dragging)
            {
                Slide();
                return;
            }

            int px, py;
            if (!IslandWindow.TryPointer(out px, out py))
            {
                px = IslandWindow.X + IslandWindow.Width / 2;
                py = IslandWindow.Y + IslandWindow.Height / 2;
            }

            var screens = IslandWindow.QueryScreens();
            // Only pick an edge when the island appears. Re-picking from
            // wherever the pointer happens to be jumps the capsule mid-edit.
            if (show && !_shown)
                _host.PickOuterEdge(screens, px);
            var shown = _host.ShownPlacement(screens, px, py, IslandMetrics.OpenWidth);
            if (show)
            {
                if (!_shown)
                {
                    _shownAt = Time.unscaledTime;
                    _touched = _shownAt;
                    _fade = 1f;
                    ApplyFade(0f);
                }
                _shown = true;
                if (_pill != null)
                    _pill.style.visibility = UnityEngine.UIElements.Visibility.Visible;
                var open = _bench != null && _bench.Open;
                IslandWindow.Present(shown, open);
                Seat(shown, open);
                IslandWindow.SetVisible(true);
                IslandWindow.PlaceDropTarget(shown.X, shown.Y);
            }
            else
            {
                _shown = false;
                if (_pill != null)
                    _pill.style.visibility = UnityEngine.UIElements.Visibility.Hidden;
                IslandWindow.OverlayHide();
                IslandWindow.SetVisible(false);
                IslandWindow.ClearShape();
            }
            RestFps();
        }

        void Seat(IslandPlacement cap, bool open)
        {
            if (_root != null)
            {
                _root.EnableInClassList("cover", IslandWindow.Covering);
                _root.EnableInClassList("takeover", open && IslandWindow.Covering);
            }

            if (_stage != null && IslandWindow.Covering)
            {
                _stage.style.left = IslandWindow.CapX;
                _stage.style.top = IslandWindow.CapY;
            }
        }

        void BeginSlide()
        {
            int px, py;
            if (!IslandWindow.TryPointer(out px, out py))
                return;
            var capY = IslandWindow.Covering ? IslandWindow.Y + IslandWindow.CapY : IslandWindow.Y;
            _hotY = py - capY;
            _dragging = true;
            Application.targetFrameRate = 60;
            Slide();
        }

        void Slide()
        {
            int px, py;
            if (!IslandWindow.TryPointer(out px, out py))
                return;
            var screens = IslandWindow.QueryScreens();
            var edge = IslandLayout.NearerOuter(screens, px);
            var place = IslandLayout.Along(screens, px, py, _hotY, edge, _host.Span,
                IslandMetrics.OpenWidth, IslandWindow.Height, IslandWindow.TopMargin);
            _host.Pose(place.Edge, place.Y);
            if (_bench != null)
                _bench.SetEdge(place.Edge);
            var open = _bench != null && _bench.Open;
            IslandWindow.Present(place, open);
            Seat(place, open);
        }

        void EndSlide()
        {
            _dragging = false;
            _press = false;
            var y = IslandWindow.Covering ? IslandWindow.Y + IslandWindow.CapY : IslandWindow.Y;
            _host.Pose(_host.Edge, y);
            if (_host.Visible || ShowPill())
                IslandWindow.PlaceDropTarget(IslandWindow.X, IslandWindow.Y);
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

        string KindClass(IslandContext ctx)
        {
            if (ctx.Kind == IslandKind.Speak)
                return "speak";
            if (ctx.Kind == IslandKind.Mixed || ctx.Kind == IslandKind.Idle)
                return "files";
            var offer = IslandOffers.Resolve(_host.Files);
            return offer != null ? offer.Id : "files";
        }

        static void SetKindClass(VisualElement el, string want)
        {
            if (el == null)
                return;
            var names = IslandOffers.ClassNames();
            for (var i = 0; i < names.Count; i++)
                el.EnableInClassList(names[i], names[i] == want);
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

            _root = root.Q("root") ?? root;
            _scrim = root.Q("scrim");
            _stage = root.Q("stage");
            _clock = root.Q<Label>("clock");
            _status = root.Q<Label>("status");
            _brand = root.Q<Label>("brand");
            _pill = root.Q("pill") ?? root;
            _dot = root.Q("dot");
            _bench = new IslandBench(root);
            if (_scrim != null)
            {
                _scrim.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0)
                        return;
                    e.StopPropagation();
                    Retreat();
                });
            }
            if (_stage != null)
            {
                _stage.RegisterCallback<PointerEnterEvent>(_ => _over = true);
                _stage.RegisterCallback<PointerLeaveEvent>(_ => _over = false);
                _stage.RegisterCallback<PointerDownEvent>(e =>
                {
                    Touch();
                    e.StopPropagation();
                });
                _stage.RegisterCallback<WheelEvent>(_ => Touch());
                _stage.RegisterCallback<KeyDownEvent>(_ => Touch());
            }
            if (_bench.Act != null)
            {
                _bench.Act.clicked += () =>
                {
                    Touch();
                    _host.Act();
                    _dirty = true;
                };
            }

            _pill.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                Touch();
                _downX = e.position.x;
                _downY = e.position.y;
                _press = true;
                _dragging = false;
                _pill.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            _pill.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_press || _dragging)
                    return;
                var dx = e.position.x - _downX;
                var dy = e.position.y - _downY;
                if (dx * dx + dy * dy < 36)
                    return;
                BeginSlide();
            });
            _pill.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0)
                    return;
                var dragged = _dragging;
                if (_pill.HasPointerCapture(e.pointerId))
                    _pill.ReleasePointer(e.pointerId);
                if (dragged)
                {
                    EndSlide();
                    return;
                }

                _press = false;
                ToggleBench();
            });
            _pill.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_dragging)
                    EndSlide();
                _press = false;
            });
            return true;
        }
    }
}
