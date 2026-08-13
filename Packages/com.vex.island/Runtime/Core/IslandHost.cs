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
        Files
    }

    /// <summary>
    /// One island process: many files, one pill. More pills = more processes.
    /// Binds 17321+ and writes $XDG_RUNTIME_DIR/island/&lt;id&gt; so ctl can
    /// address any live instance.
    /// </summary>
    public sealed class IslandHost
    {
        public const int PortBase = 17321;
        public const int MaxInstances = 8;

        public string Id { get; private set; } = "";
        public int Port { get; private set; } = PortBase;
        public IslandMode Mode { get; private set; } = IslandMode.Idle;
        public IslandEdge Edge { get; private set; } = IslandEdge.Left;
        public IslandSpan Span { get; private set; } = IslandSpan.VirtualDesktop;
        public bool Visible { get; private set; }
        public bool ShouldQuit { get; private set; }
        public IReadOnlyList<string> Files => _files;
        public IslandContext Context { get; private set; }
        public string LastNote { get; private set; } = "";

        public event Action Changed;

        readonly List<string> _files = new List<string>();
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

        public void ShowFiles(IEnumerable<string> paths)
        {
            _files.Clear();
            if (paths != null)
            {
                foreach (var p in paths)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                        _files.Add(p.Trim());
                }
            }

            TouchFiles();
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            if (paths != null)
            {
                foreach (var raw in paths)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;
                    var p = raw.Trim();
                    if (!_files.Contains(p))
                        _files.Add(p);
                }
            }

            if (_files.Count == 0)
                return;
            TouchFiles();
        }

        public string ProcessFiles()
        {
            LastNote = IslandOffers.Process(_files);
            WriteCard();
            Changed?.Invoke();
            return LastNote;
        }

        void TouchFiles()
        {
            LastNote = "";
            Mode = _files.Count > 0 ? IslandMode.Files : IslandMode.Idle;
            Visible = _files.Count > 0;
            Context = IslandOffers.Read(_files);
            WriteCard();
            Changed?.Invoke();
        }

        public void Reveal()
        {
            Visible = true;
            WriteCard();
            Changed?.Invoke();
        }

        public void Dismiss()
        {
            _files.Clear();
            LastNote = "";
            Mode = IslandMode.Idle;
            Visible = false;
            Context = IslandOffers.Read(_files);
            WriteCard();
            Changed?.Invoke();
        }

        public void PickOuterEdge(IslandRect[] screens, int pointerX)
        {
            Edge = IslandLayout.NearerOuter(screens, pointerX);
        }

        public IslandPlacement ShownPlacement(IslandRect[] screens, int px, int py)
        {
            return IslandLayout.Dock(screens, px, py, Edge, Span,
                IslandMetrics.Width, IslandMetrics.Height, IslandMetrics.TopMargin);
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
                    ShouldQuit = true;
                    break;
                case "SHOW":
                    Visible = true;
                    break;
                case "HIDE":
                    Visible = false;
                    break;
                case "TOGGLE":
                    Visible = !Visible;
                    break;
                case "EDGE":
                    if (parts.Length > 1 && TryParseEdge(parts[1], out var edge))
                        Edge = edge;
                    break;
                case "SPAN":
                    if (parts.Length > 1 && TryParseSpan(parts[1], out var span))
                        Span = span;
                    break;
                case "IDLE":
                    _files.Clear();
                    TouchFiles();
                    Visible = false;
                    break;
                case "ADD":
                    for (var i = 1; i < parts.Length; i++)
                    {
                        var path = IslandPaths.DecodeFileToken(parts[i]);
                        if (path.Length > 0 && !_files.Contains(path))
                            _files.Add(path);
                    }
                    TouchFiles();
                    break;
                case "FILES":
                    _files.Clear();
                    for (var i = 1; i < parts.Length; i++)
                    {
                        var path = IslandPaths.DecodeFileToken(parts[i]);
                        if (path.Length > 0)
                            _files.Add(path);
                    }
                    TouchFiles();
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
                var offer = IslandOffers.Resolve(_files);
                var dir = RegistryDir();
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, Id),
                    "id=" + Id + "\n" +
                    "pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + "\n" +
                    "port=" + Port + "\n" +
                    "files=" + _files.Count + "\n" +
                    "kind=" + Context.Kind + "\n" +
                    "offer=" + (offer != null ? offer.Id : "") + "\n" +
                    "note=" + LastNote + "\n" +
                    "edge=" + Edge + "\n" +
                    "visible=" + (Visible ? "1" : "0") + "\n");
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

        // ponytail: Process/Note/Context run on the IPC thread. Offers must
        // stay free of Unity objects; marshal onto Pump if you need Texture2D.
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
                    LastNote = IslandOffers.Process(_files);
                    WriteCard();
                    reply = LastNote;
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
