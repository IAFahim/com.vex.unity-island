#!/usr/bin/env python3
"""Optional AT-SPI sidecar. The player no longer starts this.

Drag-start is XFixes on XdndSelection inside libisland (event, not a 200ms
poll). Drop is Xdnd ClientMessages on island-drop. Run this by hand only
if you want sidebar click names from file managers that expose AT-SPI.
"""
from __future__ import annotations

import ctypes
import os
import socket
import sys
import time
import urllib.parse

PORT = 17321
FM = ("nautilus", "files", "nemo", "thunar", "dolphin", "caja", "org.gnome.nautilus")

libc = ctypes.CDLL("libc.so.6", use_errno=True)
try:
    libc.prctl(1, 15)  # PR_SET_PDEATHSIG = SIGTERM
except Exception:
    pass

x11 = ctypes.CDLL("libX11.so.6")
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XDefaultRootWindow.restype = ctypes.c_ulong
x11.XInternAtom.restype = ctypes.c_ulong
x11.XGetSelectionOwner.restype = ctypes.c_ulong
x11.XCreateSimpleWindow.restype = ctypes.c_ulong

dpy = x11.XOpenDisplay(None)
xdnd_atom = x11.XInternAtom(dpy, b"XdndSelection", False) if dpy else 0
_req_win = 0


class _XEvent(ctypes.Structure):
    _fields_ = [("pad", ctypes.c_long * 24)]


def pointer():
    if not dpy:
        return False, 0, 0
    root = x11.XDefaultRootWindow(dpy)
    rr = ctypes.c_ulong()
    child = ctypes.c_ulong()
    rx = ctypes.c_int()
    ry = ctypes.c_int()
    wx = ctypes.c_int()
    wy = ctypes.c_int()
    mask = ctypes.c_uint()
    ok = x11.XQueryPointer(
        dpy,
        root,
        ctypes.byref(rr),
        ctypes.byref(child),
        ctypes.byref(rx),
        ctypes.byref(ry),
        ctypes.byref(wx),
        ctypes.byref(wy),
        ctypes.byref(mask),
    )
    if not ok:
        return False, 0, 0
    return bool(mask.value & (1 << 8)), int(rx.value), int(ry.value)


def xdnd_hot():
    if not dpy or not xdnd_atom:
        return False
    return x11.XGetSelectionOwner(dpy, xdnd_atom) != 0


def xdnd_uris() -> list[str]:
    """Read the live XdndSelection. Works while Files is dragging over X11."""
    if not dpy or not xdnd_atom or not xdnd_hot():
        return []
    global _req_win
    root = x11.XDefaultRootWindow(dpy)
    if not _req_win:
        _req_win = x11.XCreateSimpleWindow(dpy, root, 0, 0, 1, 1, 0, 0, 0)
        x11.XSelectInput(dpy, _req_win, 1 << 22)
    prop = x11.XInternAtom(dpy, b"ISLAND_WATCH_DROP", False)
    targets = (b"text/uri-list", b"UTF8_STRING", b"text/plain", b"STRING")
    SelectionNotify = 31
    for name in targets:
        target = x11.XInternAtom(dpy, name, False)
        x11.XConvertSelection(dpy, xdnd_atom, target, prop, _req_win, 0)
        x11.XFlush(dpy)
        deadline = time.time() + 0.15
        while time.time() < deadline:
            if not x11.XPending(dpy):
                time.sleep(0.01)
                continue
            ev = _XEvent()
            x11.XNextEvent(dpy, ctypes.byref(ev))
            typ = ctypes.c_int.from_buffer(ev).value
            if typ != SelectionNotify:
                continue
            actual = ctypes.c_ulong()
            fmt = ctypes.c_int()
            nitems = ctypes.c_ulong()
            more = ctypes.c_ulong()
            data = ctypes.c_void_p()
            if x11.XGetWindowProperty(
                dpy,
                _req_win,
                prop,
                0,
                1024,
                True,
                0,
                ctypes.byref(actual),
                ctypes.byref(fmt),
                ctypes.byref(nitems),
                ctypes.byref(more),
                ctypes.byref(data),
            ) != 0 or not data.value:
                break
            raw = ctypes.string_at(data.value, int(nitems.value))
            x11.XFree(data)
            text = raw.decode("utf-8", "replace")
            out = []
            for line in text.replace("\r", "\n").split("\n"):
                line = line.strip()
                if line and not line.startswith("#"):
                    out.append(decode_uri(line) if line.startswith("file:") else line)
            if out:
                return out
            break
    return []


def send(line: str) -> None:
    try:
        s = socket.create_connection(("127.0.0.1", PORT), 0.3)
        s.sendall((line + "\n").encode())
        s.close()
    except OSError:
        pass


def send_files(names: list[str]) -> None:
    toks = []
    for n in names[:8]:
        if n.startswith("file://"):
            toks.append(n.replace(" ", "%20"))
        elif n.startswith("/"):
            toks.append("file://" + urllib.parse.quote(n))
        else:
            toks.append(urllib.parse.quote(n, safe="._-+@"))
    send("FILES " + " ".join(toks))


_atspi = None


def atspi_selected() -> list[str]:
    global _atspi
    if _atspi is False:
        return []
    try:
        if _atspi is None:
            import gi

            gi.require_version("Atspi", "2.0")
            from gi.repository import Atspi

            Atspi.init()
            _atspi = Atspi
    except Exception:
        _atspi = False
        return []

    Atspi = _atspi
    out: list[str] = []
    try:
        n = Atspi.get_desktop_count()
    except Exception:
        return []
    for d in range(n):
        try:
            desk = Atspi.get_desktop(d)
            apps = desk.get_child_count()
        except Exception:
            continue
        for i in range(min(apps, 40)):
            try:
                app = desk.get_child_at_index(i)
                name = (app.get_name() or "").lower()
            except Exception:
                continue
            if not any(k in name for k in FM):
                continue
            walk(app, out, 0)
    # unique, keep order
    seen = set()
    uniq = []
    for x in out:
        if x not in seen:
            seen.add(x)
            uniq.append(x)
    return uniq


def walk(acc, out: list[str], depth: int) -> None:
    if depth > 10 or len(out) >= 8:
        return
    try:
        st = acc.get_state_set()
        selected = st.contains(_atspi.StateType.SELECTED)
        name = acc.get_name() or ""
        role = acc.get_role()
    except Exception:
        return
    if selected and name and looks_like_file(name, role):
        out.append(name)
    try:
        kids = acc.get_child_count()
    except Exception:
        return
    for i in range(min(kids, 80)):
        try:
            walk(acc.get_child_at_index(i), out, depth + 1)
        except Exception:
            continue


def looks_like_file(name: str, role) -> bool:
    if len(name) > 120 or len(name) < 1:
        return False
    skip = {
        "files",
        "home",
        "recent",
        "starred",
        "trash",
        "other locations",
        "open personal folder",
        "recent files",
        "starred files",
        "open network locations",
        "open trash",
    }
    low = name.strip().lower()
    if low in skip or low.startswith("open ") or low.startswith("mount and open"):
        return False
    try:
        rname = str(role.value_nick if hasattr(role, "value_nick") else role)
    except Exception:
        rname = ""
    if any(k in rname.lower() for k in ("menu", "push", "toggle", "scroll", "filler")):
        return False
    return True


def decode_uri(u: str) -> str:
    u = u.strip()
    if u.startswith("file://"):
        return urllib.parse.unquote(u[7:])
    return urllib.parse.unquote(u)


def main() -> int:
    # Click/select is enough when AT-SPI exposes the item (sidebar, Nemo, …).
    # GNOME Files' GTK4 grid is a black hole — drop on the island instead.
    last: tuple[str, ...] | None = None
    was_dnd = False
    from_dnd = False
    while True:
        names = tuple(atspi_selected())
        dnd = xdnd_hot()
        dragged = tuple(xdnd_uris()) if dnd else ()
        if dragged and dragged != last:
            send_files(list(dragged))
            last = dragged
            from_dnd = True
        elif names and names != last:
            send_files(list(names))
            last = names
            from_dnd = False
        elif not names and not dragged and last and not dnd and not from_dnd:
            # A drop should leave the name up. Only hide AT-SPI click-select.
            send("HIDE")
            last = None
        if dnd and not was_dnd and not last:
            send("SHOW")
        was_dnd = dnd
        time.sleep(0.2)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(0)
