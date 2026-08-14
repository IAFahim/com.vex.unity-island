using System;
using System.Collections.Generic;

namespace Vex.Island
{
    /// <summary>
    /// Same shake as ReadAloud: Flips reversals, each after MinPx,
    /// all inside WindowMs. Linux watch sets Hits; App consumes.
    /// </summary>
    public sealed class IslandWiggle
    {
        public static volatile int Hits;

        public static void Note()
        {
            Hits++;
        }

        public static bool Consume()
        {
            if (Hits <= 0)
                return false;
            Hits = 0;
            return true;
        }

        int _dir;
        double _travel;
        long _last = long.MinValue / 4;
        readonly Queue<long> _flips = new Queue<long>();

        public bool Feed(int dx, long nowMs, int flips, int windowMs, int minPx, int coolMs)
        {
            var s = Math.Sign(dx);
            if (s == 0)
                return false;
            if (s == _dir)
            {
                _travel += Math.Abs(dx);
                return false;
            }

            var hit = false;
            if (_dir != 0 && _travel >= minPx)
            {
                _flips.Enqueue(nowMs);
                while (_flips.Count > 0 && nowMs - _flips.Peek() > windowMs)
                    _flips.Dequeue();
                if (_flips.Count >= flips && nowMs - _last > coolMs)
                {
                    _last = nowMs;
                    _flips.Clear();
                    hit = true;
                }
            }

            _dir = s;
            _travel = Math.Abs(dx);
            return hit;
        }

        public static bool SelfCheck()
        {
            const int flips = 5, window = 500, minPx = 25, cool = 1800;
            var d = new IslandWiggle();
            var hit = false;
            var sign = 1;
            for (var i = 0; i < 8; i++, sign = -sign)
            {
                for (var j = 0; j < 4; j++)
                    hit |= d.Feed(sign * 10, i * 60 + j * 10, flips, window, minPx, cool);
            }

            if (!hit)
                return false;

            d = new IslandWiggle();
            hit = false;
            for (var i = 0; i < 100; i++)
                hit |= d.Feed(25, i * 8, flips, window, minPx, cool);
            if (hit)
                return false;

            d = new IslandWiggle();
            hit = false;
            sign = 1;
            for (var i = 0; i < 10; i++, sign = -sign)
                hit |= d.Feed(sign * 40, i * 400, flips, window, minPx, cool);
            return !hit;
        }
    }
}
