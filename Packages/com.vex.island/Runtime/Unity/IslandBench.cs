using UnityEngine;
using UnityEngine.UIElements;

namespace Vex.Island
{
    sealed class IslandBench
    {
        readonly VisualElement _stage;
        readonly VisualElement _bench;
        readonly Label _kicker;
        readonly Label _line;
        readonly Label _count;
        readonly Button _act;
        readonly VisualElement _speak;
        readonly VisualElement _photo;
        readonly VisualElement _preview;
        readonly VisualElement _mix;
        readonly VisualElement _voice;
        readonly VisualElement _feel;
        readonly Image _shot;
        readonly Label _empty;
        readonly TextField _date;
        readonly TextField _time;
        readonly TextField _addr;
        readonly VisualElement _when;
        readonly VisualElement _day;
        readonly VisualElement _light;
        IslandVoice _voiceData = IslandVoice.Load();
        Texture2D _tex;
        string _texPath;
        bool _open;
        bool _photoMode;
        bool _ignore;

        public bool Open => _open;
        public Button Act => _act;
        public bool Editing => Focused(_date) || Focused(_time) || Focused(_addr);

        public IslandBench(VisualElement root)
        {
            _stage = root.Q("stage") ?? root;
            _bench = root.Q("bench");
            _kicker = root.Q<Label>("bench-kicker");
            _line = root.Q<Label>("bench-line");
            _count = root.Q<Label>("bench-count");
            _act = root.Q<Button>("bench-act");
            _speak = root.Q("bench-speak");
            _photo = root.Q("bench-photo");
            _preview = root.Q("bench-preview");
            _mix = root.Q("bench-mix");
            _voice = root.Q("bench-voice");
            _feel = root.Q("bench-feel");

            _shot = new Image { scaleMode = ScaleMode.ScaleToFit };
            _shot.AddToClassList("shot");
            _shot.style.flexGrow = 1;
            _shot.style.width = Length.Percent(100);
            _shot.style.height = Length.Percent(100);
            _empty = new Label("Drop an image");
            _empty.AddToClassList("empty");
            if (_preview != null)
            {
                _preview.Add(_shot);
                _preview.Add(_empty);
                _preview.RegisterCallback<ClickEvent>(_ =>
                {
                    if (IslandPhoto.Current.Count < 2)
                        return;
                    IslandPhoto.Current.Show(IslandPhoto.Current.Index + 1);
                });
                _preview.RegisterCallback<WheelEvent>(e =>
                {
                    e.StopPropagation();
                    if (IslandPhoto.Current.Count < 2)
                        return;
                    IslandPhoto.Current.Show(IslandPhoto.Current.Index + (e.delta.y < 0 ? 1 : -1));
                });
            }

            _when = new VisualElement();
            _when.AddToClassList("when");
            _when.style.flexDirection = FlexDirection.Column;
            _date = Field("2026-01-01", "YYYY-MM-DD");
            _time = Field("12:00:00 PM", "h:mm:ss AM");
            _when.Add(Named("Date", _date));
            _when.Add(Named("Time", _time));
            _date.RegisterCallback<BlurEvent>(_ => CommitDate());
            _time.RegisterCallback<BlurEvent>(_ => CommitTime());
            _date.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    CommitDate();
            });
            _time.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    CommitTime();
            });
            _date.RegisterCallback<WheelEvent>(WheelDate);
            _time.RegisterCallback<WheelEvent>(WheelTime);

            _day = new VisualElement();
            _day.AddToClassList("chips");
            Chip(_day, "Taken", "taken", OnDay);
            Chip(_day, "Today", "today", OnDay);
            Chip(_day, "–", "prev", OnDay);
            Chip(_day, "+", "next", OnDay);
            var clock = new VisualElement();
            clock.AddToClassList("chips");
            Chip(clock, "−1h", "h-", OnClock);
            Chip(clock, "+1h", "h+", OnClock);
            Chip(clock, "−15", "m-", OnClock);
            Chip(clock, "+15", "m+", OnClock);

            _addr = Field("", "Address (optional)");
            _addr.RegisterValueChangedCallback(e =>
            {
                if (_ignore)
                    return;
                IslandPhoto.Current.Address = e.newValue ?? "";
                IslandPhoto.Current.MarkDirty();
                IslandPhoto.Current.SavePrefs();
            });

            _light = new VisualElement();
            _light.AddToClassList("chips");
            Chip(_light, "Off", "off", OnLight);
            Chip(_light, "Light", "light", OnLight);
            Chip(_light, "Heavy", "heavy", OnLight);

            if (_photo != null)
            {
                var lightLab = new Label("Light");
                lightLab.AddToClassList("sec");
                _photo.Add(_when);
                _photo.Add(_day);
                _photo.Add(clock);
                _photo.Add(Named("Address", _addr));
                _photo.Add(lightLab);
                _photo.Add(_light);
            }

            if (_mix != null)
            {
                Row(_mix, "Speed", 0.5f, 3f, 1, v =>
                {
                    _voiceData.Speed = v;
                    _voiceData.Save();
                }, () => (float)_voiceData.Speed, v => v.ToString("0.0") + "×");
                Row(_mix, "Pitch", 0.5f, 2f, 2, v =>
                {
                    _voiceData.Pitch = v;
                    _voiceData.Save();
                }, () => (float)_voiceData.Pitch, v => v.ToString("0.00"));
                Row(_mix, "Volume", 0.25f, 2f, 2, v =>
                {
                    _voiceData.Volume = v;
                    _voiceData.Save();
                }, () => (float)_voiceData.Volume, v => ((int)(v * 100)).ToString() + "%");
            }

            Chip(_voice, "Google", "google");
            Chip(_voice, "Local", "inflect");
            Chip(_voice, "Robot", "spd");
            ChipFeel(_feel, "Sensitive", "sensitive");
            ChipFeel(_feel, "Normal", "normal");
            ChipFeel(_feel, "Firm", "firm");
            ChipFeel(_feel, "Stubborn", "stubborn");
            SetOpen(false);
        }

        public void SetOpen(bool open)
        {
            _open = open;
            if (_stage == null)
                return;
            _stage.EnableInClassList("open", open);
            _stage.EnableInClassList("closed", !open);
        }

        public void SetEdge(IslandEdge edge)
        {
            if (_stage != null)
                _stage.EnableInClassList("right", edge == IslandEdge.Right);
        }

        public void Paint(IslandHost host)
        {
            var kind = host != null ? host.Context.Kind : IslandKind.Idle;
            var photo = kind == IslandKind.Image;
            var speak = kind == IslandKind.Speak;
            SetPane(photo, speak);
            if (photo)
                PaintPhoto(host);
            else if (speak)
                PaintSpeak(host);
            else
                PaintFiles(host);
        }

        void SetPane(bool photo, bool speak)
        {
            var flipped = _photoMode != photo;
            _photoMode = photo;
            if (_stage != null)
            {
                _stage.EnableInClassList("photo", photo);
                _stage.EnableInClassList("speak", speak);
            }
            if (_speak != null)
                _speak.style.display = speak ? DisplayStyle.Flex : DisplayStyle.None;
            if (_photo != null)
                _photo.style.display = photo ? DisplayStyle.Flex : DisplayStyle.None;
            if (_preview != null)
                _preview.style.display = photo ? DisplayStyle.Flex : DisplayStyle.None;
            if (!photo)
                ReleaseTex();
            if (_act != null)
                _act.style.display = photo || speak ? DisplayStyle.Flex : DisplayStyle.None;
            if (flipped && _act != null && _bench != null)
            {
                if (photo)
                    _act.BringToFront();
                else if (_speak != null)
                    _bench.Insert(_bench.IndexOf(_speak), _act);
            }
        }

        void PaintSpeak(IslandHost host)
        {
            _voiceData = IslandVoice.Load();
            if (_kicker != null)
                _kicker.text = "Speak";
            if (_count != null)
            {
                _count.text = "";
                _count.EnableInClassList("show", false);
            }

            var preview = host != null ? host.Line : "";
            if (_line != null)
            {
                var path = !string.IsNullOrEmpty(preview) && (preview[0] == '/' || preview.IndexOf('\\') >= 0);
                _line.text = string.IsNullOrEmpty(preview) || path
                    ? "Select text, then wiggle."
                    : preview;
            }

            if (_act != null)
            {
                var live = IslandSpeak.IsLive;
                _act.text = live ? "Stop" : (string.IsNullOrEmpty(preview) ? "Read" : "Read again");
            }

            PaintChips();
        }

        void PaintFiles(IslandHost host)
        {
            if (_kicker != null)
                _kicker.text = host != null && host.Context.Kind == IslandKind.Mixed ? "Mixed" : "Files";
            if (_count != null)
            {
                _count.text = "";
                _count.EnableInClassList("show", false);
            }
            if (_line != null)
            {
                if (host == null || host.Files.Count == 0)
                    _line.text = "Drop photos to stamp, or text to read.";
                else
                    _line.text = host.Context.Detail + " — drop only photos for date and light.";
            }
        }

        void PaintPhoto(IslandHost host)
        {
            var p = IslandPhoto.Current;
            if (_kicker != null)
                _kicker.text = "Photo";
            if (_line != null)
                _line.text = string.IsNullOrEmpty(host.Line) ? p.StampLine() : host.Line;
            if (_act != null)
            {
                if (p.LastCount > 0 && !p.PreviewStale)
                    _act.text = p.LastCount == 1 ? "Stamped" : "Stamped " + p.LastCount;
                else
                    _act.text = p.Count <= 1 ? "Stamp" : "Stamp " + p.Count;
            }

            if (_count != null)
            {
                if (p.Count > 1)
                {
                    _count.text = (p.Index + 1) + " / " + p.Count;
                    _count.EnableInClassList("show", true);
                }
                else
                {
                    _count.text = "";
                    _count.EnableInClassList("show", false);
                }
            }

            _ignore = true;
            if (_date != null && !Focused(_date))
                _date.SetValueWithoutNotify(p.DateField());
            if (_time != null && !Focused(_time))
                _time.SetValueWithoutNotify(p.TimeField());
            if (_addr != null && !Focused(_addr))
                _addr.SetValueWithoutNotify(p.Address ?? "");
            _ignore = false;

            PaintDay(p);
            PaintLight(p);
            ShowPreview(p.PreviewPath);
        }

        void PaintDay(IslandPhoto p)
        {
            if (_day == null)
                return;
            var today = p.DateOverride.HasValue && p.DateOverride.Value.Date == System.DateTime.Today;
            for (var i = 0; i < _day.childCount; i++)
            {
                var id = _day[i].userData as string;
                _day[i].EnableInClassList("on",
                    (id == "taken" && !p.DateOverride.HasValue) || (id == "today" && today));
            }
        }

        void PaintLight(IslandPhoto p)
        {
            if (_light == null)
                return;
            var want = IslandPhoto.Current.Drop == IslandDrop.Off ? "off"
                : IslandPhoto.Current.Drop == IslandDrop.Heavy ? "heavy" : "light";
            for (var i = 0; i < _light.childCount; i++)
                _light[i].EnableInClassList("on", _light[i].userData as string == want);
        }

        void ShowPreview(string path)
        {
            if (_shot == null)
                return;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                _shot.image = null;
                if (_empty != null)
                    _empty.style.display = DisplayStyle.Flex;
                return;
            }

            if (path == _texPath && _tex != null)
                return;
            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                if (_tex == null)
                    _tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (_tex.LoadImage(bytes))
                {
                    _texPath = path;
                    _shot.image = _tex;
                    if (_empty != null)
                        _empty.style.display = DisplayStyle.None;
                }
            }
            catch
            {
            }
        }

        void ReleaseTex()
        {
            _shot.image = null;
            _texPath = null;
            if (_tex == null)
                return;
            Object.Destroy(_tex);
            _tex = null;
        }

        void OnDay(string id)
        {
            var p = IslandPhoto.Current;
            switch (id)
            {
                case "taken":
                    p.ClearDate();
                    p.ClearTime();
                    break;
                case "today":
                    p.SetToday();
                    break;
                case "prev":
                    p.NudgeDay(-1);
                    break;
                case "next":
                    p.NudgeDay(1);
                    break;
            }
        }

        void OnClock(string id)
        {
            var p = IslandPhoto.Current;
            switch (id)
            {
                case "h-":
                    p.NudgeTime(-60);
                    break;
                case "h+":
                    p.NudgeTime(60);
                    break;
                case "m-":
                    p.NudgeTime(-15);
                    break;
                case "m+":
                    p.NudgeTime(15);
                    break;
            }
        }

        void OnLight(string id)
        {
            var drop = id == "off" ? IslandDrop.Off : id == "heavy" ? IslandDrop.Heavy : IslandDrop.Light;
            IslandPhoto.Current.ApplyDrop(drop);
            IslandPhoto.Current.SavePrefs();
            PaintLight(IslandPhoto.Current);
        }

        void WheelDate(WheelEvent e)
        {
            e.StopPropagation();
            IslandPhoto.Current.NudgeDay(e.delta.y < 0 ? 1 : -1);
            _ignore = true;
            if (_date != null)
                _date.SetValueWithoutNotify(IslandPhoto.Current.DateField());
            _ignore = false;
        }

        void WheelTime(WheelEvent e)
        {
            e.StopPropagation();
            var step = e.shiftKey ? 1 : 15;
            IslandPhoto.Current.NudgeTime(e.delta.y < 0 ? step : -step);
            _ignore = true;
            if (_time != null)
                _time.SetValueWithoutNotify(IslandPhoto.Current.TimeField());
            _ignore = false;
        }

        void CommitDate()
        {
            if (_date == null)
                return;
            IslandPhoto.Current.TrySetDate(_date.value);
            _ignore = true;
            _date.SetValueWithoutNotify(IslandPhoto.Current.DateField());
            _ignore = false;
        }

        void CommitTime()
        {
            if (_time == null)
                return;
            IslandPhoto.Current.TrySetTime(_time.value);
            _ignore = true;
            _time.SetValueWithoutNotify(IslandPhoto.Current.TimeField());
            _ignore = false;
        }

        static bool Focused(VisualElement el)
        {
            if (el == null || el.focusController == null)
                return false;
            var f = el.focusController.focusedElement as VisualElement;
            return f != null && (f == el || el.Contains(f));
        }

        static VisualElement Named(string name, VisualElement field)
        {
            var col = new VisualElement();
            col.AddToClassList("named");
            var n = new Label(name);
            n.AddToClassList("sec");
            col.Add(n);
            col.Add(field);
            return col;
        }

        static TextField Field(string value, string hint)
        {
            var tf = new TextField { value = value };
            tf.AddToClassList("field");
            if (tf.labelElement != null)
            {
                tf.labelElement.style.display = DisplayStyle.None;
                tf.labelElement.style.width = 0;
                tf.labelElement.style.minWidth = 0;
            }
            tf.style.flexGrow = 1;
            tf.style.marginLeft = 0;
            tf.style.marginRight = 0;
            if (!string.IsNullOrEmpty(hint))
            {
#if UNITY_2022_1_OR_NEWER
                tf.textEdition.placeholder = hint;
#endif
            }

            return tf;
        }

        void PaintChips()
        {
            if (_voice != null)
            {
                for (var i = 0; i < _voice.childCount; i++)
                    _voice[i].EnableInClassList("on", _voice[i].userData as string == _voiceData.Engine);
            }

            if (_feel != null)
            {
                for (var i = 0; i < _feel.childCount; i++)
                    _feel[i].EnableInClassList("on", _feel[i].userData as string == _voiceData.WiggleFeel);
            }
        }

        void Chip(VisualElement row, string label, string engine)
        {
            Chip(row, label, engine, id =>
            {
                _voiceData = IslandVoice.Load();
                _voiceData.Engine = id;
                _voiceData.Save();
                PaintChips();
            });
        }

        void ChipFeel(VisualElement row, string label, string feel)
        {
            Chip(row, label, feel, id =>
            {
                _voiceData = IslandVoice.Load();
                _voiceData.ApplyFeel(id);
                _voiceData.Save();
                PaintChips();
            });
        }

        static void Chip(VisualElement row, string label, string id, System.Action<string> click)
        {
            if (row == null)
                return;
            var b = new Button { text = label, userData = id };
            b.AddToClassList("chip");
            b.clicked += () => click(id);
            row.Add(b);
        }

        static void Row(VisualElement parent, string name, float lo, float hi, int digits,
            System.Action<float> set, System.Func<float> get, System.Func<float, string> fmt)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            var n = new Label(name);
            n.AddToClassList("row-name");
            var sl = new Slider { lowValue = lo, highValue = hi, value = get(), showInputField = false };
            sl.AddToClassList("mix-slider");
            sl.style.flexGrow = 1;
            sl.style.minWidth = 90;
            sl.style.height = 18;
            SkinSlider(sl);
            var val = new Label(fmt(sl.value));
            val.AddToClassList("row-val");
            sl.RegisterValueChangedCallback(e =>
            {
                var v = (float)System.Math.Round(e.newValue, digits);
                val.text = fmt(v);
                set(v);
            });
            row.Add(n);
            row.Add(sl);
            row.Add(val);
            parent.Add(row);
        }

        static void SkinSlider(Slider sl)
        {
            sl.schedule.Execute(() =>
            {
                HideTheme(sl, "unity-base-slider__dragger-border");
                HideTheme(sl, "unity-base-slider__tracker");
                HideTheme(sl, "unity-base-slider__dragger");
                var track = sl.Q(className: "unity-base-slider__tracker");
                if (track != null)
                {
                    track.style.backgroundImage = StyleKeyword.None;
                    track.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
                    track.style.height = 3;
                    track.style.borderTopWidth = 0;
                    track.style.borderBottomWidth = 0;
                    track.style.borderLeftWidth = 0;
                    track.style.borderRightWidth = 0;
                }

                var thumb = sl.Q(className: "unity-base-slider__dragger");
                if (thumb != null)
                {
                    thumb.style.backgroundImage = StyleKeyword.None;
                    thumb.style.backgroundColor = new Color(0.965f, 0.839f, 0.588f);
                    thumb.style.width = 12;
                    thumb.style.height = 12;
                    thumb.style.borderTopWidth = 0;
                    thumb.style.borderBottomWidth = 0;
                    thumb.style.borderLeftWidth = 0;
                    thumb.style.borderRightWidth = 0;
                    thumb.style.borderTopLeftRadius = 6;
                    thumb.style.borderTopRightRadius = 6;
                    thumb.style.borderBottomLeftRadius = 6;
                    thumb.style.borderBottomRightRadius = 6;
                }
            });
        }

        static void HideTheme(VisualElement root, string cls)
        {
            if (root == null)
                return;
            root.Query(className: cls).ForEach(el =>
            {
                el.style.backgroundImage = StyleKeyword.None;
                el.style.unityBackgroundImageTintColor = Color.clear;
            });
        }
    }
}
