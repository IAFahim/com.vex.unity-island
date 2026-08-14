using System;

namespace Vex.Island
{
    public enum IslandEdge
    {
        Top,
        Bottom,
        Left,
        Right
    }

    public enum IslandSpan
    {
        ActiveMonitor,
        Primary,
        VirtualDesktop
    }

    public readonly struct IslandRect
    {
        public readonly int X, Y, W, H;

        public IslandRect(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public int CenterX => X + W / 2;
        public int CenterY => Y + H / 2;

        public bool Contains(int px, int py)
        {
            return px >= X && py >= Y && px < X + W && py < Y + H;
        }

        public static IslandRect Union(IslandRect a, IslandRect b)
        {
            var x0 = Math.Min(a.X, b.X);
            var y0 = Math.Min(a.Y, b.Y);
            var x1 = Math.Max(a.X + a.W, b.X + b.W);
            var y1 = Math.Max(a.Y + a.H, b.Y + b.H);
            return new IslandRect(x0, y0, x1 - x0, y1 - y0);
        }
    }

    public readonly struct IslandPlacement
    {
        public readonly int X, Y;
        public readonly IslandEdge Edge;
        public readonly IslandRect Bound;

        public IslandPlacement(int x, int y, IslandEdge edge, IslandRect bound)
        {
            X = x;
            Y = y;
            Edge = edge;
            Bound = bound;
        }

        public IslandPlacement Hidden(int size)
        {
            var park = Math.Max(size, 4000);
            switch (Edge)
            {
                case IslandEdge.Top:
                    return new IslandPlacement(X, Bound.Y - park, Edge, Bound);
                case IslandEdge.Bottom:
                    return new IslandPlacement(X, Bound.Y + Bound.H + park - size, Edge, Bound);
                case IslandEdge.Left:
                    return new IslandPlacement(Bound.X - park, Y, Edge, Bound);
                default:
                    return new IslandPlacement(Bound.X + Bound.W + park - size, Y, Edge, Bound);
            }
        }
    }

    /// <summary>
    /// Where the pill sits. Pure math so tests do not need a player.
    /// </summary>
    public static class IslandLayout
    {
        public static IslandPlacement Dock(
            IslandRect[] screens,
            int pointerX,
            int pointerY,
            IslandEdge edge,
            IslandSpan span,
            int width,
            int height,
            int margin)
        {
            if (screens == null || screens.Length == 0)
                screens = new[] { new IslandRect(0, 0, 1920, 1080) };

            IslandRect bound;
            switch (span)
            {
                case IslandSpan.VirtualDesktop:
                    if (edge == IslandEdge.Left)
                        bound = Leftmost(screens);
                    else if (edge == IslandEdge.Right)
                        bound = Rightmost(screens);
                    else
                    {
                        bound = screens[0];
                        for (var i = 1; i < screens.Length; i++)
                            bound = IslandRect.Union(bound, screens[i]);
                    }
                    break;
                case IslandSpan.Primary:
                    bound = PrimaryOf(screens);
                    break;
                default:
                    bound = ScreenAt(screens, pointerX, pointerY);
                    break;
            }

            int x, y;
            switch (edge)
            {
                case IslandEdge.Bottom:
                    x = bound.X + Math.Max(0, (bound.W - width) / 2);
                    y = bound.Y + bound.H - height - margin;
                    break;
                case IslandEdge.Left:
                    x = bound.X + margin;
                    y = bound.Y + Math.Max(0, (bound.H - height) / 2);
                    break;
                case IslandEdge.Right:
                    x = bound.X + bound.W - width - margin;
                    y = bound.Y + Math.Max(0, (bound.H - height) / 2);
                    break;
                default:
                    x = bound.X + Math.Max(0, (bound.W - width) / 2);
                    y = bound.Y + margin;
                    break;
            }

            return new IslandPlacement(x, y, edge, bound);
        }

        public static int ClampY(IslandRect bound, int y, int height, int margin)
        {
            var lo = bound.Y + margin;
            var hi = bound.Y + bound.H - height - margin;
            if (hi < lo)
                return lo;
            if (y < lo)
                return lo;
            if (y > hi)
                return hi;
            return y;
        }

        /// <summary>
        /// Same X dock as <see cref="Dock"/>, but Y follows the grab
        /// (pointerY - hotY) instead of recentering on the monitor.
        /// </summary>
        public static IslandPlacement Along(
            IslandRect[] screens,
            int pointerX,
            int pointerY,
            int hotY,
            IslandEdge edge,
            IslandSpan span,
            int width,
            int height,
            int margin)
        {
            var dock = Dock(screens, pointerX, pointerY, edge, span, width, height, margin);
            var y = ClampY(dock.Bound, pointerY - hotY, height, margin);
            return new IslandPlacement(dock.X, y, dock.Edge, dock.Bound);
        }

        public static void Inside(IslandRect bound, int worldX, int worldY, out int localX, out int localY)
        {
            localX = worldX - bound.X;
            localY = worldY - bound.Y;
        }

        public static IslandRect Leftmost(IslandRect[] screens)
        {
            var best = screens[0];
            for (var i = 1; i < screens.Length; i++)
            {
                if (screens[i].X < best.X)
                    best = screens[i];
            }

            return best;
        }

        public static IslandRect Rightmost(IslandRect[] screens)
        {
            var best = screens[0];
            for (var i = 1; i < screens.Length; i++)
            {
                if (screens[i].X + screens[i].W > best.X + best.W)
                    best = screens[i];
            }

            return best;
        }

        public static IslandEdge NearerOuter(IslandRect[] screens, int pointerX)
        {
            var left = Leftmost(screens);
            var right = Rightmost(screens);
            var dl = pointerX - left.X;
            var dr = right.X + right.W - pointerX;
            return dl <= dr ? IslandEdge.Left : IslandEdge.Right;
        }

        public static IslandRect PrimaryOf(IslandRect[] screens)
        {
            var best = screens[0];
            for (var i = 1; i < screens.Length; i++)
            {
                if (screens[i].W * (long)screens[i].H > best.W * (long)best.H)
                    best = screens[i];
            }

            return best;
        }

        public static IslandRect ScreenAt(IslandRect[] screens, int px, int py)
        {
            for (var i = 0; i < screens.Length; i++)
            {
                if (screens[i].Contains(px, py))
                    return screens[i];
            }

            return screens[0];
        }
    }
}
