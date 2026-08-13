/* Linux X11 chrome for the Island player. Linked as libisland.so.
   Declarations for Xext/XShape are local so we do not need -dev packages. */
#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/extensions/XI2.h>
#include <dirent.h>
#include <fcntl.h>
#include <linux/input.h>
#include <poll.h>
#include <pthread.h>
#include <sys/ioctl.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define ShapeSet 0
#define ShapeBounding 0
#define ShapeInput 2
#define Unsorted 0
#define MWM_HINTS_DECORATIONS (1L << 1)

extern int XShapeCombineRectangles(Display *, Window, int, int, int, XRectangle *, int, int, int);

#define XFixesSelectionNotify 0
#define XFixesSetSelectionOwnerNotify 0
#define XFixesSetSelectionOwnerNotifyMask (1L << 0)
#define XFixesSelectionWindowDestroyNotifyMask (1L << 1)
#define XFixesSelectionClientCloseNotifyMask (1L << 2)

typedef struct {
    int type;
    unsigned long serial;
    int send_event;
    Display *display;
    Window window;
    int subtype;
    Window owner;
    Atom selection;
    unsigned long timestamp;
    unsigned long selection_timestamp;
} XFixesSelectionNotifyEvent;

extern int XFixesQueryExtension(Display *, int *, int *);
extern void XFixesSelectSelectionInput(Display *, Window, Atom, unsigned long);

#define GenericEvent 35

typedef struct {
    int deviceid;
    int mask_len;
    unsigned char *mask;
} XIEventMask;

typedef struct {
    int type;
    unsigned long serial;
    int send_event;
    Display *display;
    int extension;
    int evtype;
    unsigned long time;
    int deviceid;
    int sourceid;
    int detail;
    int flags;
} XIRawEvent;

extern int XIQueryVersion(Display *, int *, int *);
extern int XISelectEvents(Display *, Window, XIEventMask *, int);

typedef struct {
    unsigned long flags;
    unsigned long functions;
    unsigned long decorations;
    long input_mode;
    unsigned long status;
} MotifWmHints;

static Display *g_dpy;
static Window g_win;
static int g_have;
static int g_hot_x, g_hot_y;
static int g_dragging;
static unsigned long g_pid;

static int IgnoreX(Display *d, XErrorEvent *e)
{
    (void)d;
    (void)e;
    return 0;
}

static void EnsureFixes(Display *d);
static void EnsureXI2(Display *d);
static void OpenKeyboards(void);
static void StartEscThread(void);
static void GrabEsc(Display *d, int on);

static Display *Dpy(void)
{
    if (!g_dpy)
    {
        g_dpy = XOpenDisplay(NULL);
        if (g_dpy)
        {
            XSetErrorHandler(IgnoreX);
            EnsureFixes(g_dpy);
            EnsureXI2(g_dpy);
            OpenKeyboards();
            StartEscThread();
        }
    }
    return g_dpy;
}

static int WindowPid(Display *d, Window w, unsigned long *out)
{
    Atom pid_atom = XInternAtom(d, "_NET_WM_PID", True);
    if (!pid_atom)
        return 0;
    Atom actual;
    int fmt;
    unsigned long n, bytes;
    unsigned char *prop = NULL;
    int ok = 0;
    if (XGetWindowProperty(d, w, pid_atom, 0, 1, False, XA_CARDINAL,
                           &actual, &fmt, &n, &bytes, &prop) == Success &&
        prop && n > 0)
    {
        *out = *(unsigned long *)prop;
        ok = 1;
    }
    if (prop)
        XFree(prop);
    return ok;
}

static void Consider(Display *d, Window w, unsigned long want_pid, Window *best, int *best_area)
{
    unsigned long pid = 0;
    if (!WindowPid(d, w, &pid) || pid != want_pid)
        return;
    XWindowAttributes wa;
    if (!XGetWindowAttributes(d, w, &wa))
        return;
    if (wa.map_state != IsViewable || wa.width < 32 || wa.height < 16)
        return;
    int area = wa.width * wa.height;
    if (area > *best_area)
    {
        *best_area = area;
        *best = w;
    }
}

static void Walk(Display *d, Window w, unsigned long want_pid, Window *best, int *best_area)
{
    Consider(d, w, want_pid, best, best_area);
    Window root, parent, *kids = NULL;
    unsigned n = 0;
    if (!XQueryTree(d, w, &root, &parent, &kids, &n))
        return;
    for (unsigned i = 0; i < n; i++)
        Walk(d, kids[i], want_pid, best, best_area);
    if (kids)
        XFree(kids);
}

static Window FindByPid(Display *d, unsigned long pid)
{
    Window best = 0;
    int area = -1;
    Atom list = XInternAtom(d, "_NET_CLIENT_LIST", True);
    if (list)
    {
        Atom actual;
        int fmt;
        unsigned long n, bytes;
        unsigned char *prop = NULL;
        if (XGetWindowProperty(d, DefaultRootWindow(d), list, 0, 4096, False, XA_WINDOW,
                               &actual, &fmt, &n, &bytes, &prop) == Success &&
            prop)
        {
            Window *wins = (Window *)prop;
            for (unsigned long i = 0; i < n; i++)
                Consider(d, wins[i], pid, &best, &area);
            XFree(prop);
        }
    }
    if (!best)
        Walk(d, DefaultRootWindow(d), pid, &best, &area);
    return best;
}

static void SendState(Display *d, Window w, const char *name, int add)
{
    Atom state = XInternAtom(d, "_NET_WM_STATE", False);
    Atom atom = XInternAtom(d, name, False);
    XEvent ev;
    memset(&ev, 0, sizeof(ev));
    ev.xclient.type = ClientMessage;
    ev.xclient.window = w;
    ev.xclient.message_type = state;
    ev.xclient.format = 32;
    ev.xclient.data.l[0] = add ? 1 : 0;
    ev.xclient.data.l[1] = (long)atom;
    ev.xclient.data.l[2] = 0;
    ev.xclient.data.l[3] = 1;
    XSendEvent(d, DefaultRootWindow(d), False,
               SubstructureRedirectMask | SubstructureNotifyMask, &ev);
}

static void StripDecor(Display *d, Window w)
{
    Atom motif = XInternAtom(d, "_MOTIF_WM_HINTS", False);
    MotifWmHints hints;
    memset(&hints, 0, sizeof(hints));
    hints.flags = MWM_HINTS_DECORATIONS;
    hints.decorations = 0;
    XChangeProperty(d, w, motif, motif, 32, PropModeReplace, (unsigned char *)&hints, 5);
}

static void SetType(Display *d, Window w)
{
    Atom type = XInternAtom(d, "_NET_WM_WINDOW_TYPE", False);
    Atom utility = XInternAtom(d, "_NET_WM_WINDOW_TYPE_UTILITY", False);
    XChangeProperty(d, w, type, XA_ATOM, 32, PropModeReplace, (unsigned char *)&utility, 1);
}

int Island_Apply(int pid, int x, int y, int w, int h, int flags)
{
    g_pid = (unsigned long)pid;
    Display *d = Dpy();
    if (!d)
        return 0;
    Window win = FindByPid(d, (unsigned long)pid);
    if (!win)
        return 0;
    g_win = win;
    g_have = 1;
    g_pid = (unsigned long)pid;

    if (flags & 1)
        StripDecor(d, win);
    SetType(d, win);
    if (flags & 2)
        SendState(d, win, "_NET_WM_STATE_ABOVE", 1);
    if (flags & 4)
    {
        SendState(d, win, "_NET_WM_STATE_SKIP_TASKBAR", 1);
        SendState(d, win, "_NET_WM_STATE_SKIP_PAGER", 1);
    }

    XMoveResizeWindow(d, win, x, y, (unsigned)w, (unsigned)h);
    /* Invisible until Snap(true) applies the capsule. No raise. */
    XShapeCombineRectangles(d, win, ShapeBounding, 0, 0, NULL, 0, ShapeSet, Unsorted);
    XShapeCombineRectangles(d, win, ShapeInput, 0, 0, NULL, 0, ShapeSet, Unsorted);
    XFlush(d);
    return 1;
}

int Island_Move(int x, int y)
{
    if (!g_have || !Dpy())
        return 0;
    XMoveWindow(g_dpy, g_win, x, y);
    XFlush(g_dpy);
    return 1;
}

int Island_SetVisible(int visible)
{
    if (!g_have || !Dpy())
        return 0;
    if (visible)
        XMapRaised(g_dpy, g_win);
    else
        XUnmapWindow(g_dpy, g_win);
    XFlush(g_dpy);
    return 1;
}

int Island_SetShape(const int *xywh, int count)
{
    if (!g_have || !Dpy())
        return 0;
    if (!xywh || count <= 0)
    {
        XShapeCombineRectangles(g_dpy, g_win, ShapeBounding, 0, 0, NULL, 0, ShapeSet, Unsorted);
        XShapeCombineRectangles(g_dpy, g_win, ShapeInput, 0, 0, NULL, 0, ShapeSet, Unsorted);
        XFlush(g_dpy);
        return 1;
    }
    XRectangle *rects = (XRectangle *)malloc(sizeof(XRectangle) * (size_t)count);
    if (!rects)
        return 0;
    for (int i = 0; i < count; i++)
    {
        rects[i].x = (short)xywh[i * 4 + 0];
        rects[i].y = (short)xywh[i * 4 + 1];
        rects[i].width = (unsigned short)xywh[i * 4 + 2];
        rects[i].height = (unsigned short)xywh[i * 4 + 3];
    }
    XShapeCombineRectangles(g_dpy, g_win, ShapeBounding, 0, 0, rects, count, ShapeSet, Unsorted);
    XShapeCombineRectangles(g_dpy, g_win, ShapeInput, 0, 0, rects, count, ShapeSet, Unsorted);
    free(rects);
    XFlush(g_dpy);
    return 1;
}

unsigned long Island_GetWindow(void)
{
    return (unsigned long)g_win;
}

static int EnsureWin(void)
{
    if (!Dpy())
        return 0;
    if (g_have && g_win)
        return 1;
    if (!g_pid)
        return 0;
    Window w = FindByPid(g_dpy, g_pid);
    if (!w)
        return 0;
    g_win = w;
    g_have = 1;
    return 1;
}

static int RootOrigin(int *ox, int *oy)
{
    Window child;
    return XTranslateCoordinates(g_dpy, g_win, DefaultRootWindow(g_dpy), 0, 0, ox, oy, &child);
}

/* Root-pointer drag. Unity window-local mouse deltas jitter because the
   window moves under the cursor. Track the hotspot in root space instead. */
int Island_BeginDrag(void)
{
    if (!EnsureWin())
        return 0;
    Window root, child;
    int rx, ry, wx, wy;
    unsigned mask;
    if (!XQueryPointer(g_dpy, DefaultRootWindow(g_dpy), &root, &child, &rx, &ry, &wx, &wy, &mask))
        return 0;
    int ox, oy;
    if (!RootOrigin(&ox, &oy))
        return 0;
    g_hot_x = rx - ox;
    g_hot_y = ry - oy;
    g_dragging = 1;
    return 1;
}

int Island_Drag(void)
{
    if (!g_dragging || !EnsureWin())
        return 0;
    Window root, child;
    int rx, ry, wx, wy;
    unsigned mask;
    if (!XQueryPointer(g_dpy, DefaultRootWindow(g_dpy), &root, &child, &rx, &ry, &wx, &wy, &mask))
        return 0;
    XMoveWindow(g_dpy, g_win, rx - g_hot_x, ry - g_hot_y);
    XFlush(g_dpy);
    return 1;
}

int Island_EndDrag(void)
{
    g_dragging = 0;
    return 1;
}

int Island_BeginMove(int root_x, int root_y)
{
    (void)root_x;
    (void)root_y;
    return Island_BeginDrag();
}

typedef struct {
    short x_org, y_org;
    short width, height;
} XineramaScreenInfo;

extern int XineramaIsActive(Display *);
extern XineramaScreenInfo *XineramaQueryScreens(Display *, int *);

int Island_GetScreens(int *xywh, int max)
{
    if (!xywh || max <= 0 || !Dpy())
        return 0;
    int n = 0;
    if (XineramaIsActive(g_dpy))
    {
        XineramaScreenInfo *info = XineramaQueryScreens(g_dpy, &n);
        if (info && n > 0)
        {
            if (n > max)
                n = max;
            for (int i = 0; i < n; i++)
            {
                xywh[i * 4 + 0] = info[i].x_org;
                xywh[i * 4 + 1] = info[i].y_org;
                xywh[i * 4 + 2] = info[i].width;
                xywh[i * 4 + 3] = info[i].height;
            }
            XFree(info);
            return n;
        }
        if (info)
            XFree(info);
    }
    xywh[0] = 0;
    xywh[1] = 0;
    xywh[2] = WidthOfScreen(DefaultScreenOfDisplay(g_dpy));
    xywh[3] = HeightOfScreen(DefaultScreenOfDisplay(g_dpy));
    return 1;
}

int Island_Pointer(int *x, int *y)
{
    if (!x || !y || !Dpy())
        return 0;
    Window root, child;
    int rx, ry, wx, wy;
    unsigned mask;
    if (!XQueryPointer(g_dpy, DefaultRootWindow(g_dpy), &root, &child, &rx, &ry, &wx, &wy, &mask))
        return 0;
    *x = rx;
    *y = ry;
    return 1;
}

static Window g_drop;
static Window g_xdnd_source;
static Window g_fix;
static char g_drop_buf[4096];
static int g_drop_len;
static int g_drag_live;
static int g_xdnd_owner;
static int g_xi_drag;
static int g_need_finished;
static int g_xi_opcode = -1;
static int g_btn1;
static int g_press_x, g_press_y;
static Window g_edge[2];
static int g_fixes_base = -1;
static int g_tgt;
static Window g_sel_req;
static unsigned long g_sel_ts;
static int g_esc_grab;
static int g_want_quit;
#define MAX_EV 8
static int g_evfd[MAX_EV];
static int g_nevf;

#define XK_Escape 0xff1b

static const char *g_targets[] = {
    "text/uri-list",
    "text/plain;charset=utf-8",
    "UTF8_STRING",
    "text/plain",
    "FILE_NAME",
    "STRING",
    NULL
};

static void Dlog(const char *fmt, ...)
{
    FILE *f = fopen("/tmp/island-xdnd.log", "a");
    if (!f)
        return;
    va_list ap;
    va_start(ap, fmt);
    vfprintf(f, fmt, ap);
    va_end(ap);
    fputc('\n', f);
    fclose(f);
}

static void XdndAware(Display *d, Window w)
{
    Atom aware = XInternAtom(d, "XdndAware", False);
    unsigned long ver = 5;
    XChangeProperty(d, w, aware, XA_ATOM, 32, PropModeReplace, (unsigned char *)&ver, 1);
}

/* InputOnly: no pixmap, cannot paint a black bar. */
static Window CreateDrop(Display *d, Window root, int x, int y, int w, int h)
{
    XSetWindowAttributes swa;
    memset(&swa, 0, sizeof(swa));
    swa.override_redirect = True;
    swa.event_mask = StructureNotifyMask | PropertyChangeMask | KeyPressMask;
    Window win = XCreateWindow(d, root, x, y, (unsigned)w, (unsigned)h, 0,
                               0, InputOnly, CopyFromParent,
                               CWOverrideRedirect | CWEventMask, &swa);
    if (!win)
        return 0;
    XStoreName(d, win, "island-drop");
    XdndAware(d, win);
    {
        unsigned long opacity = 0;
        Atom opa = XInternAtom(d, "_NET_WM_WINDOW_OPACITY", False);
        XChangeProperty(d, win, opa, XA_CARDINAL, 32, PropModeReplace,
                        (unsigned char *)&opacity, 1);
    }
    return win;
}

static int KeyBit(const unsigned char *bits, int key)
{
    return (bits[key / 8] >> (key % 8)) & 1;
}

static void OpenKeyboards(void)
{
    DIR *dir;
    struct dirent *de;
    if (g_nevf > 0)
        return;
    dir = opendir("/dev/input");
    if (!dir)
        return;
    while ((de = readdir(dir)) && g_nevf < MAX_EV)
    {
        char path[64];
        unsigned char bits[(KEY_MAX + 7) / 8];
        int fd;
        if (strncmp(de->d_name, "event", 5) != 0)
            continue;
        snprintf(path, sizeof(path), "/dev/input/%s", de->d_name);
        fd = open(path, O_RDONLY | O_NONBLOCK);
        if (fd < 0)
            continue;
        memset(bits, 0, sizeof(bits));
        if (ioctl(fd, EVIOCGBIT(EV_KEY, sizeof(bits)), bits) < 0 ||
            !KeyBit(bits, KEY_ESC) || !KeyBit(bits, KEY_A))
        {
            close(fd);
            continue;
        }
        g_evfd[g_nevf++] = fd;
    }
    closedir(dir);
    Dlog("esc evdev n=%d", g_nevf);
}

static void NoteEsc(void)
{
    g_want_quit = 1;
    Dlog("esc hide");
}

static void PollEsc(void)
{
    struct input_event ev;
    int i;
    OpenKeyboards();
    for (i = 0; i < g_nevf; i++)
    {
        while (read(g_evfd[i], &ev, sizeof(ev)) == (ssize_t)sizeof(ev))
        {
            if (ev.type == EV_KEY && ev.code == KEY_ESC && ev.value == 1)
                NoteEsc();
        }
    }
}

static void *EscThread(void *arg)
{
    struct pollfd pfd[MAX_EV];
    int i, n;
    (void)arg;
    OpenKeyboards();
    n = g_nevf;
    if (n <= 0)
        return NULL;
    for (i = 0; i < n; i++)
    {
        pfd[i].fd = g_evfd[i];
        pfd[i].events = POLLIN;
    }
    Dlog("esc thread n=%d", n);
    for (;;)
    {
        if (poll(pfd, (nfds_t)n, -1) <= 0)
            continue;
        for (i = 0; i < n; i++)
        {
            struct input_event ev;
            if (!(pfd[i].revents & POLLIN))
                continue;
            while (read(g_evfd[i], &ev, sizeof(ev)) == (ssize_t)sizeof(ev))
            {
                if (ev.type == EV_KEY && ev.code == KEY_ESC && ev.value == 1)
                    NoteEsc();
            }
        }
    }
    return NULL;
}

static void StartEscThread(void)
{
    static int started;
    pthread_t th;
    if (started)
        return;
    started = 1;
    if (pthread_create(&th, NULL, EscThread, NULL) == 0)
        pthread_detach(th);
}

static void GrabEsc(Display *d, int on)
{
    KeyCode kc = XKeysymToKeycode(d, XK_Escape);
    if (!kc)
        return;
    Window root = DefaultRootWindow(d);
    if (on && !g_esc_grab)
    {
        XGrabKey(d, kc, AnyModifier, root, True, GrabModeAsync, GrabModeAsync);
        g_esc_grab = 1;
        Dlog("esc grab");
    }
    else if (!on && g_esc_grab)
    {
        XUngrabKey(d, kc, AnyModifier, root);
        g_esc_grab = 0;
        Dlog("esc ungrab");
    }
    XFlush(d);
}

int Island_WantQuit(void)
{
    PollEsc();
    int q = g_want_quit;
    g_want_quit = 0;
    return q;
}

int Island_Overlay(int x, int y, int w, int h)
{
    Display *d = Dpy();
    if (!d)
        return 0;
    if (w <= 0 || h <= 0)
    {
        if (g_drop)
        {
            XUnmapWindow(d, g_drop);
            XDestroyWindow(d, g_drop);
            g_drop = 0;
            XFlush(d);
        }
        return 1;
    }
    Window root = DefaultRootWindow(d);
    if (!g_drop)
    {
        g_drop = CreateDrop(d, root, x, y, w, h);
        if (!g_drop)
            return 0;
        Dlog("overlay create 0x%lx %dx%d+%d+%d", (unsigned long)g_drop, w, h, x, y);
    }
    else
        XMoveResizeWindow(d, g_drop, x, y, (unsigned)w, (unsigned)h);
    XMapRaised(d, g_drop);
    if (g_win)
        XSetInputFocus(d, g_win, RevertToParent, CurrentTime);
    XFlush(d);
    return 1;
}

static void SendXdnd(Display *d, Window dest, const char *name, Window src, long a, long b, long c, long e)
{
    XEvent ev;
    memset(&ev, 0, sizeof(ev));
    ev.xclient.type = ClientMessage;
    ev.xclient.window = dest;
    ev.xclient.message_type = XInternAtom(d, name, False);
    ev.xclient.format = 32;
    ev.xclient.data.l[0] = (long)src;
    ev.xclient.data.l[1] = a;
    ev.xclient.data.l[2] = b;
    ev.xclient.data.l[3] = c;
    ev.xclient.data.l[4] = e;
    XSendEvent(d, dest, False, NoEventMask, &ev);
}

static int ReadUriProp(Display *d, Window w, Atom prop, char *buf, int n)
{
    Atom actual;
    int fmt;
    unsigned long len, more;
    unsigned char *data = NULL;
    if (XGetWindowProperty(d, w, prop, 0, n / 4, True, AnyPropertyType,
                           &actual, &fmt, &len, &more, &data) != Success || !data)
        return 0;
    int copy = (int)len;
    if (fmt == 16)
        copy *= 2;
    if (fmt == 32)
        copy *= 4;
    if (copy >= n)
        copy = n - 1;
    memcpy(buf, data, (size_t)copy);
    buf[copy] = 0;
    XFree(data);
    return copy;
}

static void SyncLive(void)
{
    g_drag_live = g_xdnd_owner || g_xi_drag;
}

static int RootXY(Display *d, int *x, int *y)
{
    Window root, child;
    int rx, ry, wx, wy;
    unsigned mask;
    if (!XQueryPointer(d, DefaultRootWindow(d), &root, &child, &rx, &ry, &wx, &wy, &mask))
        return 0;
    *x = rx;
    *y = ry;
    return 1;
}

static void EnsureXI2(Display *d)
{
    if (g_xi_opcode != -1)
        return;
    int ev = 0, err = 0;
    if (!XQueryExtension(d, "XInputExtension", &g_xi_opcode, &ev, &err))
    {
        g_xi_opcode = -2;
        Dlog("xi2 missing");
        return;
    }
    int major = 2, minor = 0;
    if (XIQueryVersion(d, &major, &minor) != 0)
    {
        g_xi_opcode = -2;
        Dlog("xi2 version");
        return;
    }
    unsigned char mask[XIMaskLen(XI_RawMotion) + 1];
    memset(mask, 0, sizeof(mask));
    XISetMask(mask, XI_RawButtonPress);
    XISetMask(mask, XI_RawButtonRelease);
    XISetMask(mask, XI_RawMotion);
    XIEventMask em;
    em.deviceid = XIAllMasterDevices;
    em.mask_len = (int)sizeof(mask);
    em.mask = mask;
    XISelectEvents(d, DefaultRootWindow(d), &em, 1);
    XFlush(d);
    Dlog("xi2 opcode=%d %d.%d", g_xi_opcode, major, minor);
}

int Island_ArmEdge(int x, int y, int w, int h)
{
    Display *d = Dpy();
    if (!d || w <= 0 || h <= 0)
        return 0;
    int i = (x <= 64) ? 0 : 1;
    Window root = DefaultRootWindow(d);
    if (!g_edge[i])
    {
        g_edge[i] = CreateDrop(d, root, x, y, w, h);
        if (!g_edge[i])
            return 0;
        XStoreName(d, g_edge[i], i ? "island-edge-r" : "island-edge-l");
        Dlog("edge%d 0x%lx %dx%d+%d+%d", i, (unsigned long)g_edge[i], w, h, x, y);
    }
    else
        XMoveResizeWindow(d, g_edge[i], x, y, (unsigned)w, (unsigned)h);
    XMapRaised(d, g_edge[i]);
    XFlush(d);
    return 1;
}

static int IsOurs(Window w)
{
    return w && (w == g_drop || w == g_edge[0] || w == g_edge[1] || w == g_fix);
}

static void EnsureFixes(Display *d)
{
    if (g_fixes_base != -1)
        return;
    int err = 0;
    int base = 0;
    if (!XFixesQueryExtension(d, &base, &err))
    {
        g_fixes_base = -2;
        Dlog("fixes missing");
        return;
    }
    g_fixes_base = base;
    XSetWindowAttributes swa;
    memset(&swa, 0, sizeof(swa));
    swa.override_redirect = True;
    g_fix = XCreateWindow(d, DefaultRootWindow(d), -8, -8, 1, 1, 0,
                          0, InputOnly, CopyFromParent, CWOverrideRedirect, &swa);
    XFixesSelectSelectionInput(
        d, g_fix, XInternAtom(d, "XdndSelection", False),
        XFixesSetSelectionOwnerNotifyMask | XFixesSelectionWindowDestroyNotifyMask |
            XFixesSelectionClientCloseNotifyMask);
    XFlush(d);
    Dlog("fixes base=%d win=0x%lx", g_fixes_base, (unsigned long)g_fix);
}

static void RequestUris(Display *d, Window req, unsigned long ts)
{
    if (!req)
        return;
    if (!ts)
        ts = CurrentTime;
    g_sel_req = req;
    g_sel_ts = ts;
    g_tgt = 0;
    Atom xdndsel = XInternAtom(d, "XdndSelection", False);
    Atom dest = XInternAtom(d, "ISLAND_DROP", False);
    Atom uri = XInternAtom(d, g_targets[0], False);
    XConvertSelection(d, xdndsel, uri, dest, req, (Time)ts);
    XFlush(d);
}

static void RequestNext(Display *d)
{
    g_tgt++;
    if (!g_sel_req || !g_targets[g_tgt])
        return;
    Atom xdndsel = XInternAtom(d, "XdndSelection", False);
    Atom dest = XInternAtom(d, "ISLAND_DROP", False);
    Atom uri = XInternAtom(d, g_targets[g_tgt], False);
    XConvertSelection(d, xdndsel, uri, dest, g_sel_req, (Time)g_sel_ts);
    XFlush(d);
}

static void FinishOk(Display *d, int ok)
{
    if (!g_need_finished || !g_xdnd_source)
        return;
    Atom action = XInternAtom(d, "XdndActionCopy", False);
    SendXdnd(d, g_xdnd_source, "XdndFinished", g_drop ? g_drop : g_fix,
             ok ? 1 : 0, ok ? (long)action : 0, 0, 0);
    XFlush(d);
    g_need_finished = 0;
}

static int TakeReady(char *buf, int n)
{
    if (g_drop_len <= 0)
        return 0;
    int len = g_drop_len;
    if (len >= n)
        len = n - 1;
    memcpy(buf, g_drop_buf, (size_t)len);
    buf[len] = 0;
    g_drop_len = 0;
    return len;
}

int Island_DragLive(void)
{
    return g_drag_live;
}

int Island_XdndPoll(char *buf, int n)
{
    Display *d = Dpy();
    if (!d || !buf || n <= 0)
        return 0;
    Atom enter = XInternAtom(d, "XdndEnter", False);
    Atom pos = XInternAtom(d, "XdndPosition", False);
    Atom drop = XInternAtom(d, "XdndDrop", False);
    Atom action = XInternAtom(d, "XdndActionCopy", False);
    Atom dest = XInternAtom(d, "ISLAND_DROP", False);

    /* XWayland does not deliver XI2-raw for a Wayland Files drag. The
       compositor still mirrors the pointer+buttons into XQueryPointer. */
    {
        int x, y;
        Window root, child;
        int wx, wy;
        unsigned mask = 0;
        if (XQueryPointer(d, DefaultRootWindow(d), &root, &child, &x, &y, &wx, &wy, &mask))
        {
            int b1 = (mask & Button1Mask) != 0;
            if (b1 && !g_btn1)
            {
                g_btn1 = 1;
                g_press_x = x;
                g_press_y = y;
            }
            else if (b1 && g_btn1 && !g_xi_drag)
            {
                int dx = x - g_press_x;
                int dy = y - g_press_y;
                if (dx * dx + dy * dy >= 16 * 16)
                {
                    g_xi_drag = 1;
                    SyncLive();
                    Dlog("ptr drag %d,%d mask=0x%x live=%d", x, y, mask, g_drag_live);
                }
            }
            else if (!b1 && g_btn1)
            {
                g_btn1 = 0;
                g_xi_drag = 0;
                SyncLive();
            }
        }
    }

    while (XPending(d))
    {
        XEvent ev;
        XNextEvent(d, &ev);
        if (ev.type == KeyPress)
        {
            KeySym ks = XLookupKeysym(&ev.xkey, 0);
            if (ks == XK_Escape)
            {
                g_want_quit = 1;
                Dlog("esc");
            }
            continue;
        }
        if (g_fixes_base >= 0 && ev.type == g_fixes_base + XFixesSelectionNotify)
        {
            XFixesSelectionNotifyEvent *fe = (XFixesSelectionNotifyEvent *)&ev;
            g_xdnd_owner = fe->owner != 0;
            SyncLive();
            Dlog("fixes subtype=%d owner=0x%lx ts=%lu live=%d",
                 fe->subtype, (unsigned long)fe->owner,
                 (unsigned long)fe->timestamp, g_drag_live);
            if (g_xdnd_owner)
                RequestUris(d, g_fix, fe->timestamp);
            continue;
        }
        if (ev.type == GenericEvent && g_xi_opcode >= 0 &&
            ev.xcookie.extension == g_xi_opcode)
        {
            if (XGetEventData(d, &ev.xcookie) && ev.xcookie.data)
            {
                XIRawEvent *raw = (XIRawEvent *)ev.xcookie.data;
                if (raw->evtype == XI_RawButtonPress && raw->detail == 1)
                {
                    g_btn1 = 1;
                    RootXY(d, &g_press_x, &g_press_y);
                }
                else if (raw->evtype == XI_RawButtonRelease && raw->detail == 1)
                {
                    g_btn1 = 0;
                    g_xi_drag = 0;
                    SyncLive();
                }
                else if (raw->evtype == XI_RawMotion && g_btn1 && !g_xi_drag)
                {
                    int x, y;
                    if (RootXY(d, &x, &y))
                    {
                        int dx = x - g_press_x;
                        int dy = y - g_press_y;
                        if (dx * dx + dy * dy >= 16 * 16)
                        {
                            g_xi_drag = 1;
                            SyncLive();
                            Dlog("xi drag %d,%d live=%d", x, y, g_drag_live);
                        }
                    }
                }
            }
            XFreeEventData(d, &ev.xcookie);
            continue;
        }
        if (ev.type == SelectionNotify)
        {
            Window req = ev.xselection.requestor;
            if (ev.xselection.property == None)
            {
                Dlog("sel none target=%s", g_targets[g_tgt] ? g_targets[g_tgt] : "?");
                RequestNext(d);
                continue;
            }
            int got = ReadUriProp(d, req, dest, g_drop_buf, (int)sizeof(g_drop_buf));
            Dlog("sel %s n=%d buf=%.80s",
                 g_targets[g_tgt] ? g_targets[g_tgt] : "?", got,
                 got > 0 ? g_drop_buf : "");
            if (got > 0)
            {
                g_drop_len = got;
                FinishOk(d, 1);
            }
            else
                RequestNext(d);
            continue;
        }
        if (ev.type != ClientMessage)
            continue;
        if (ev.xclient.message_type == enter)
        {
            Window hit = ev.xclient.window;
            g_xdnd_source = (Window)ev.xclient.data.l[0];
            g_xdnd_owner = 1;
            SyncLive();
            Dlog("enter src=0x%lx hit=0x%lx flags=0x%lx",
                 (unsigned long)g_xdnd_source, (unsigned long)hit,
                 (unsigned long)ev.xclient.data.l[1]);
            if (IsOurs(hit))
                RequestUris(d, hit, 0);
        }
        else if (ev.xclient.message_type == pos)
        {
            Window hit = ev.xclient.window;
            g_xdnd_source = (Window)ev.xclient.data.l[0];
            g_xdnd_owner = 1;
            SyncLive();
            if (IsOurs(hit))
            {
                SendXdnd(d, g_xdnd_source, "XdndStatus", hit, 1 | 2, 0, 0, (long)action);
                XFlush(d);
            }
        }
        else if (ev.xclient.message_type == drop)
        {
            Window hit = ev.xclient.window;
            g_xdnd_source = (Window)ev.xclient.data.l[0];
            g_need_finished = 1;
            unsigned long ts = (unsigned long)ev.xclient.data.l[2];
            Dlog("drop src=0x%lx hit=0x%lx ts=%lu",
                 (unsigned long)g_xdnd_source, (unsigned long)hit, ts);
            if (IsOurs(hit))
                RequestUris(d, hit, ts);
            else
                FinishOk(d, 0);
        }
    }
    return TakeReady(buf, n);
}
