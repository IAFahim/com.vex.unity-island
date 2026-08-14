using System;
using System.Collections.Generic;

namespace Vex.Island
{
    public readonly struct IslandFrame : IEquatable<IslandFrame>
    {
        public readonly string[] Files;
        public readonly IslandContext Context;
        public readonly string OfferId;
        public readonly bool Holds;
        public readonly bool OpensBench;
        public readonly bool ActsOnDrop;
        public readonly IslandEdge Edge;
        public readonly int SlideY;
        public readonly IslandSpan Span;
        public readonly bool Visible;
        public readonly bool Bench;
        public readonly string Line;
        public readonly string Note;

        public IslandFrame(
            string[] files,
            IslandContext context,
            string offerId,
            bool holds,
            bool opensBench,
            bool actsOnDrop,
            IslandEdge edge,
            int slideY,
            IslandSpan span,
            bool visible,
            bool bench,
            string line,
            string note)
        {
            Files = files ?? Array.Empty<string>();
            Context = context;
            OfferId = offerId ?? "";
            Holds = holds;
            OpensBench = opensBench;
            ActsOnDrop = actsOnDrop;
            Edge = edge;
            SlideY = slideY;
            Span = span;
            Visible = visible;
            Bench = bench;
            Line = line ?? "";
            Note = note ?? "";
        }

        public int Count => Files.Length;

        public bool Shows => Visible && (Bench || Files.Length > 0);

        public IslandMode Mode
        {
            get
            {
                if (!Visible)
                    return IslandMode.Idle;
                if (Context.Kind == IslandKind.Image)
                    return IslandMode.Photo;
                if (Context.Kind == IslandKind.Speak)
                    return IslandMode.Speak;
                return IslandMode.Files;
            }
        }

        public bool Equals(IslandFrame o)
        {
            if (Edge != o.Edge || SlideY != o.SlideY || Span != o.Span)
                return false;
            if (Visible != o.Visible || Bench != o.Bench)
                return false;
            if (Holds != o.Holds || OpensBench != o.OpensBench || ActsOnDrop != o.ActsOnDrop)
                return false;
            if (Context.Kind != o.Context.Kind || Context.Count != o.Context.Count)
                return false;
            if (OfferId != o.OfferId || Line != o.Line || Note != o.Note)
                return false;
            if (Files.Length != o.Files.Length)
                return false;
            for (var i = 0; i < Files.Length; i++)
            {
                if (Files[i] != o.Files[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is IslandFrame f && Equals(f);
        }

        public override int GetHashCode()
        {
            return (Visible ? 1 : 0) ^ (Files.Length * 397) ^ (int)Edge ^ SlideY;
        }
    }

    public static class IslandKernel
    {
        public static IslandFrame Idle(IslandEdge edge, int slideY, IslandSpan span)
        {
            return new IslandFrame(
                Array.Empty<string>(),
                new IslandContext(IslandKind.Idle, "", "idle", 0),
                "",
                false,
                false,
                false,
                edge,
                slideY,
                span,
                false,
                false,
                "",
                "");
        }

        public static IslandFrame Hold(IslandFrame prev, IReadOnlyList<string> paths)
        {
            var files = Normalize(paths);
            if (files.Length == 0)
                return Idle(prev.Edge, prev.SlideY, prev.Span);

            var photos = Filter(files, IslandPhoto.Accepts);
            if (photos.Length > 0)
                files = photos;

            var ctx = IslandSense.FromFiles(files);
            var offer = IslandOffers.Resolve(files);
            var holds = offer != null && offer.Holds;
            var opens = offer != null && offer.OpensBench;
            var acts = offer != null && offer.ActsOnDrop;
            return new IslandFrame(
                files,
                ctx,
                offer != null ? offer.Id : "",
                holds,
                opens,
                acts,
                prev.Edge,
                prev.SlideY,
                prev.Span,
                true,
                prev.Bench || opens,
                "",
                "");
        }

        public static IslandFrame Dismiss(IslandFrame prev)
        {
            return Idle(prev.Edge, prev.SlideY, prev.Span);
        }

        public static IslandFrame Pose(IslandFrame prev, IslandEdge edge, int slideY)
        {
            return new IslandFrame(
                prev.Files, prev.Context, prev.OfferId,
                prev.Holds, prev.OpensBench, prev.ActsOnDrop,
                edge, slideY, prev.Span,
                prev.Visible, prev.Bench, prev.Line, prev.Note);
        }

        public static IslandFrame Bench(IslandFrame prev, bool open)
        {
            return new IslandFrame(
                prev.Files, prev.Context, prev.OfferId,
                prev.Holds, prev.OpensBench, prev.ActsOnDrop,
                prev.Edge, prev.SlideY, prev.Span,
                prev.Visible, open, prev.Line, prev.Note);
        }

        public static IslandFrame Speak(IslandFrame prev, string line, string note)
        {
            return new IslandFrame(
                prev.Files,
                new IslandContext(IslandKind.Speak, line ?? "", note ?? "", 1),
                "speak",
                true,
                false,
                true,
                prev.Edge,
                prev.SlideY,
                prev.Span,
                true,
                prev.Bench,
                line ?? "",
                note ?? "");
        }

        public static IslandFrame Noted(IslandFrame prev, string note, string line)
        {
            return new IslandFrame(
                prev.Files, prev.Context, prev.OfferId,
                prev.Holds, prev.OpensBench, prev.ActsOnDrop,
                prev.Edge, prev.SlideY, prev.Span,
                prev.Visible, prev.Bench,
                line ?? prev.Line,
                note ?? "");
        }

        public static IslandFrame WithSpan(IslandFrame prev, IslandSpan span)
        {
            return new IslandFrame(
                prev.Files, prev.Context, prev.OfferId,
                prev.Holds, prev.OpensBench, prev.ActsOnDrop,
                prev.Edge, prev.SlideY, span,
                prev.Visible, prev.Bench, prev.Line, prev.Note);
        }

        public static IslandFrame Reveal(IslandFrame prev, bool visible)
        {
            return new IslandFrame(
                prev.Files, prev.Context, prev.OfferId,
                prev.Holds, prev.OpensBench, prev.ActsOnDrop,
                prev.Edge, prev.SlideY, prev.Span,
                visible, prev.Bench, prev.Line, prev.Note);
        }

        public static string[] Normalize(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return Array.Empty<string>();
            var list = new List<string>(paths.Count);
            for (var i = 0; i < paths.Count; i++)
            {
                var p = paths[i];
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                p = p.Trim();
                if (!list.Contains(p))
                    list.Add(p);
            }

            return list.ToArray();
        }

        public static string[] Filter(string[] have, System.Func<string, bool> keep)
        {
            if (have == null || have.Length == 0 || keep == null)
                return Array.Empty<string>();
            var list = new List<string>(have.Length);
            for (var i = 0; i < have.Length; i++)
            {
                if (keep(have[i]))
                    list.Add(have[i]);
            }

            return list.ToArray();
        }

        public static string[] Append(string[] have, IReadOnlyList<string> extra)
        {
            var list = new List<string>(have ?? Array.Empty<string>());
            if (extra == null)
                return list.ToArray();
            for (var i = 0; i < extra.Count; i++)
            {
                var p = extra[i];
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                p = p.Trim();
                if (!list.Contains(p))
                    list.Add(p);
            }

            return list.ToArray();
        }
    }
}
