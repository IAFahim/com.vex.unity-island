using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Vex.Island
{
    public enum IslandMode
    {
        Idle,
        Files,
        Speak,
        Photo
    }

    public sealed class IslandHost
    {
        public const int PortBase = 17321;
        public const int MaxInstances = 8;

        public string Id { get; private set; } = "";
        public int Port { get; private set; } = PortBase;
        public IslandFrame Frame { get; private set; } =
            IslandKernel.Idle(IslandEdge.Left, int.MinValue, IslandSpan.VirtualDesktop);
        public IslandMode Mode => Frame.Mode;
        public IslandEdge Edge
        {
            get => Frame.Edge;
            set => Commit(IslandKernel.Pose(Frame, value, Frame.SlideY));
        }
        public int SlideY
        {
            get => Frame.SlideY;
            set => Commit(IslandKernel.Pose(Frame, Frame.Edge, value));
        }
        public IslandSpan Span => Frame.Span;
        public bool Visible => Frame.Visible;
        public bool ShouldQuit { get; private set; }
        public IReadOnlyList<string> Files => Frame.Files;
        public IslandContext Context => Frame.Context;
        public string LastNote => Frame.Note;
        public string Line
        {
            get => Frame.Line;
            set
            {
                if (Frame.Line == value)
                    return;
                Commit(IslandKernel.Noted(Frame, Frame.Note, value));
            }
        }

        public event Action Changed;
        readonly object _gate = new object();
        readonly List<string> _inbox = new List<string>();
        TcpListener _listen;
        Thread _thread;
        volatile bool _run;

        public void Start()
        {
            if (_run)
                return;
            _run = true;
            var envPort = 0;
            int.TryParse(Environment.GetEnvironmentVariable("ISLAND_PORT"), out envPort);
            var envId = Environment.GetEnvironmentVariable("ISLAND_ID");
            Exception last = null;
            var first = envPort > 0 ? envPort : PortBase;
            for (var p = first; p < PortBase + MaxInstances; p++)
            {
                try
                {
                    _listen = new TcpListener(IPAddress.Loopback, p);
                    _listen.Start();
                    Port = p;
                    last = null;
                    break;
                }
                catch (Exception e)
                {
                    last = e;
                    _listen = null;
                }
            }

            if (_listen == null)
            {
                Console.Error.WriteLine("Island IPC " + (last != null ? last.Message : "no port"));
                return;
            }

            Id = string.IsNullOrEmpty(envId) ? "i" + Port : envId;
            WriteCard();
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "island-ipc" };
            _thread.Start();
        }

        public void Stop()
        {
            _run = false;
            try { _listen?.Stop(); } catch { }
            RemoveCard();
        }

        public void Pump()
        {
            lock (_gate)
            {
                if (_inbox.Count == 0)
                    return;
                for (var i = 0; i < _inbox.Count; i++)
                    ApplyLine(_inbox[i]);
                _inbox.Clear();
            }

            Changed?.Invoke();
        }

        void Commit(IslandFrame next)
        {
            Frame = next;
            WriteCard();
            Changed?.Invoke();
        }

        public void ShowFiles(IEnumerable<string> paths)
        {
            BindWork(IslandKernel.Hold(Frame, IslandKernel.Normalize(AsList(paths))));
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            var next = IslandKernel.Append(Frame.Files, AsList(paths));
            if (next.Length == 0)
                return;
            BindWork(IslandKernel.Hold(Frame, next));
        }

        void BindWork(IslandFrame held)
        {
            if (held.Context.Kind != IslandKind.Image)
                IslandPhoto.Current.Clear();
            else
            {
                IslandPhoto.Current.Bind(held.Files);
                held = IslandKernel.Noted(held, "", IslandPhoto.Current.StampLine());
            }

            Commit(held);
        }

        public string ProcessFiles()
        {
            var note = IslandOffers.Process(Frame.Files);
            var line = Frame.Context.Kind == IslandKind.Image
                ? IslandPhoto.Current.ResultLine()
                : Frame.Line;
            Commit(IslandKernel.Noted(Frame, note, line));
            return note;
        }

        public string Act()
        {
            if (IslandSpeak.IsLive)
                return SpeakNow("");
            if (Frame.Context.Kind == IslandKind.Speak)
                return SpeakNow(IslandSpeak.Selection());
            return ProcessFiles();
        }

        public string SpeakNow(string text)
        {
            text = IslandSpeak.Clean(text);
            if (text.Length == 0)
            {
                var note = IslandSpeak.IsLive ? IslandSpeak.Stop() : "empty";
                Commit(IslandKernel.Noted(Frame, note, Frame.Line));
                return note;
            }

            var spoken = IslandSpeak.Speak(text);
            Commit(IslandKernel.Speak(Frame, IslandSpeak.Preview(text, 48), spoken));
            return spoken;
        }

        public string TakeDrop(IEnumerable<string> paths)
        {
            ShowFiles(paths);
            if (Frame.Count == 0)
                return "";
            if (!Frame.ActsOnDrop)
                return Frame.Note;
            if (Frame.Context.Kind != IslandKind.Speak)
                return ProcessFiles();
            try
            {
                var text = File.ReadAllText(Frame.Files[0]);
                if (text.Length > 256 * 1024)
                    text = text.Substring(0, 256 * 1024);
                return SpeakNow(text);
            }
            catch
            {
                Commit(IslandKernel.Noted(Frame, "speak:0", Frame.Line));
                return "speak:0";
            }
        }

        public string ExportPhoto()
        {
            return ProcessFiles();
        }

        public void SetBench(bool open)
        {
            if (Frame.Bench == open)
                return;
            Commit(IslandKernel.Bench(Frame, open));
        }

        public void Pose(IslandEdge edge, int slideY)
        {
            Commit(IslandKernel.Pose(Frame, edge, slideY));
        }

        public void Reveal()
        {
            Commit(IslandKernel.Reveal(Frame, true));
        }

        public void Dismiss()
        {
            IslandSpeak.Stop();
            IslandPhoto.Current.Clear();
            Commit(IslandKernel.Dismiss(Frame));
        }

        public void PickOuterEdge(IslandRect[] screens, int pointerX)
        {
            Pose(IslandLayout.NearerOuter(screens, pointerX), Frame.SlideY);
        }

        static IReadOnlyList<string> AsList(IEnumerable<string> paths)
        {
            if (paths == null)
                return Array.Empty<string>();
            if (paths is IReadOnlyList<string> list)
                return list;
            return new List<string>(paths);
        }

        public IslandPlacement ShownPlacement(IslandRect[] screens, int px, int py)
        {
            return ShownPlacement(screens, px, py, IslandMetrics.Width);
        }

        public IslandPlacement ShownPlacement(IslandRect[] screens, int px, int py, int width)
        {
            var dock = IslandLayout.Dock(screens, px, py, Edge, Span,
                width, IslandMetrics.Height, IslandMetrics.TopMargin);
            if (SlideY == int.MinValue)
                return dock;
            return new IslandPlacement(dock.X,
                IslandLayout.ClampY(dock.Bound, SlideY, IslandMetrics.Height, IslandMetrics.TopMargin),
                dock.Edge, dock.Bound);
        }

        public IslandPlacement HiddenPlacement(IslandRect[] screens, int px, int py)
        {
            return ShownPlacement(screens, px, py).Hidden(IslandMetrics.Height);
        }

        void ApplyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            var parts = Split(line);
            if (parts.Length == 0)
                return;
            switch (parts[0].ToUpperInvariant())
            {
                case "QUIT":
                    IslandSpeak.Stop();
                    ShouldQuit = true;
                    break;
                case "SHOW":
                    Reveal();
                    break;
                case "HIDE":
                    Commit(IslandKernel.Reveal(Frame, false));
                    break;
                case "TOGGLE":
                    Commit(IslandKernel.Reveal(Frame, !Frame.Visible));
                    break;
                case "EDGE":
                    if (parts.Length > 1 && TryParseEdge(parts[1], out var edge))
                        Pose(edge, Frame.SlideY);
                    break;
                case "SPAN":
                    if (parts.Length > 1 && TryParseSpan(parts[1], out var span))
                        Commit(IslandKernel.WithSpan(Frame, span));
                    break;
                case "IDLE":
                    Dismiss();
                    break;
                case "ADD":
                    var add = new List<string>();
                    for (var i = 1; i < parts.Length; i++)
                    {
                        var path = IslandPaths.DecodeFileToken(parts[i]);
                        if (path.Length > 0)
                            add.Add(path);
                    }
                    AddFiles(add);
                    break;
                case "FILES":
                    var files = new List<string>();
                    for (var i = 1; i < parts.Length; i++)
                    {
                        var path = IslandPaths.DecodeFileToken(parts[i]);
                        if (path.Length > 0)
                            files.Add(path);
                    }
                    ShowFiles(files);
                    break;
                case "PROCESS":
                    ProcessFiles();
                    break;
                case "PING":
                    break;
            }
        }

        public static string RegistryDir()
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Path.GetTempPath(), "island-" + Environment.UserName);
            return Path.Combine(dir, "island");
        }

        void WriteCard()
        {
            try
            {
                if (string.IsNullOrEmpty(Id))
                    return;
                var dir = RegistryDir();
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, Id),
                    "id=" + Id + "\n" +
                    "pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + "\n" +
                    "port=" + Port + "\n" +
                    "files=" + Frame.Count + "\n" +
                    "kind=" + Frame.Context.Kind + "\n" +
                    "offer=" + Frame.OfferId + "\n" +
                    "note=" + Frame.Note + "\n" +
                    "edge=" + Frame.Edge + "\n" +
                    "visible=" + (Frame.Visible ? "1" : "0") + "\n");
            }
            catch
            {
            }
        }

        void RemoveCard()
        {
            try
            {
                if (!string.IsNullOrEmpty(Id))
                    File.Delete(Path.Combine(RegistryDir(), Id));
            }
            catch
            {
            }
        }

        void AcceptLoop()
        {
            while (_run)
            {
                try
                {
                    using (var c = _listen.AcceptTcpClient())
                    using (var r = new StreamReader(c.GetStream(), new UTF8Encoding(false)))
                    using (var w = new StreamWriter(c.GetStream(), new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            string reply;
                            lock (_gate)
                            {
                                if (TryReply(line, out reply))
                                    _inbox.Add("PING");
                                else
                                {
                                    _inbox.Add(line);
                                    reply = "OK";
                                }
                            }

                            w.WriteLine(reply);
                            if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                                break;
                        }
                    }
                }
                catch (SocketException)
                {
                    if (!_run)
                        break;
                }
                catch
                {
                    if (!_run)
                        break;
                }
            }
        }

        bool TryReply(string line, out string reply)
        {
            var parts = Split(line);
            if (parts.Length == 0)
            {
                reply = "OK";
                return false;
            }

            switch (parts[0].ToUpperInvariant())
            {
                case "PROCESS":
                    reply = ProcessFiles();
                    return true;
                case "SPEAK":
                    var spoken = parts.Length > 1
                        ? string.Join(" ", parts, 1, parts.Length - 1)
                        : IslandSpeak.Selection();
                    reply = SpeakNow(spoken);
                    return true;
                case "STOP":
                    IslandSpeak.Stop();
                    if (Frame.Mode == IslandMode.Speak)
                        Commit(IslandKernel.Noted(IslandKernel.Reveal(Frame, false), "stop", Frame.Line));
                    else
                        Commit(IslandKernel.Noted(Frame, "stop", Frame.Line));
                    reply = "stop";
                    return true;
                case "NOTE":
                    reply = string.IsNullOrEmpty(LastNote) ? "none" : LastNote;
                    return true;
                case "CONTEXT":
                    reply = Context.Kind + " " + Context.Count + " " + Context.Detail;
                    return true;
                default:
                    reply = "OK";
                    return false;
            }
        }

        static bool TryParseEdge(string s, out IslandEdge edge)
        {
            switch (s.ToUpperInvariant())
            {
                case "BOTTOM": edge = IslandEdge.Bottom; return true;
                case "LEFT": edge = IslandEdge.Left; return true;
                case "RIGHT": edge = IslandEdge.Right; return true;
                case "TOP": edge = IslandEdge.Top; return true;
                default: edge = IslandEdge.Top; return false;
            }
        }

        static bool TryParseSpan(string s, out IslandSpan span)
        {
            switch (s.ToUpperInvariant())
            {
                case "VIRTUAL":
                case "ALL":
                    span = IslandSpan.VirtualDesktop;
                    return true;
                case "PRIMARY":
                    span = IslandSpan.Primary;
                    return true;
                default:
                    span = IslandSpan.ActiveMonitor;
                    return true;
            }
        }

        static string[] Split(string line)
        {
            return line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
