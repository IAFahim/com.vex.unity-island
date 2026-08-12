/* Linux X11 chrome for the Island player. Linked as libisland.so.
   Declarations for Xext/XShape are local so we do not need -dev packages. */
#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
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

static Display *Dpy(void)
{
    if (!g_dpy)
    {
        g_dpy = XOpenDisplay(NULL);
        if (g_dpy)
            XSetErrorHandler(IgnoreX);
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
    XRaiseWindow(d, win);
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
    if (!g_have || !Dpy() || !xywh || count <= 0)
        return 0;
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
