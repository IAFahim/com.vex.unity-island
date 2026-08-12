using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Vex.Island
{
    public enum IslandMode
    {
        Idle,
        Files
    }

    /// <summary>
    /// Session + edge presence + local control port.
    /// File managers / future hooks talk here; UITK only reads state.
    /// </summary>
    public sealed class IslandHost
    {
        public const int Port = 17321;

        public IslandMode Mode { get; private set; } = IslandMode.Idle;
        public IslandEdge Edge { get; private set; } = IslandEdge.Top;
        public IslandSpan Span { get; private set; } = IslandSpan.ActiveMonitor;
        public bool Visible { get; private set; } = true;
        public IReadOnlyList<string> Files => _files;

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
            try
            {
                _listen = new TcpListener(IPAddress.Loopback, Port);
                _listen.Start();
                _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "island-ipc" };
                _thread.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Island IPC " + e.Message);
            }
        }

        public void Stop()
        {
            _run = false;
            try { _listen?.Stop(); } catch { }
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

            Mode = _files.Count > 0 ? IslandMode.Files : IslandMode.Idle;
            Visible = true;
            Changed?.Invoke();
        }

        public IslandPlacement ShownPlacement(IslandRect[] screens, int px, int py)
        {
            return IslandLayout.Dock(screens, px, py, Edge, Span,
                IslandWindow.Width, IslandWindow.Height, IslandWindow.TopMargin);
        }

        public IslandPlacement HiddenPlacement(IslandRect[] screens, int px, int py)
        {
            return ShownPlacement(screens, px, py).Hidden(IslandWindow.Height);
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
                    Mode = IslandMode.Idle;
                    Visible = true;
                    break;
                case "FILES":
                    _files.Clear();
                    for (var i = 1; i < parts.Length; i++)
                        _files.Add(parts[i]);
                    Mode = _files.Count > 0 ? IslandMode.Files : IslandMode.Idle;
                    Visible = true;
                    break;
            }
        }

        void AcceptLoop()
        {
            while (_run)
            {
                try
                {
                    using (var c = _listen.AcceptTcpClient())
                    using (var r = new StreamReader(c.GetStream(), Encoding.UTF8))
                    using (var w = new StreamWriter(c.GetStream(), Encoding.UTF8) { AutoFlush = true })
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            lock (_gate)
                                _inbox.Add(line);
                            w.WriteLine("OK");
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
