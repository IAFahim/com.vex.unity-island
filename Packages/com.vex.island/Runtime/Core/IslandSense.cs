using System.Collections.Generic;
using System.IO;

namespace Vex.Island
{
    public enum IslandKind
    {
        Idle,
        Files,
        Image,
        Text,
        Speak,
        Audio,
        Video,
        Sheet,
        Xml,
        Mixed
    }

    /// <summary>
    /// What the island is looking at. UITK paints this; offers Process it.
    /// Pure so tests do not need a player.
    /// </summary>
    public readonly struct IslandContext
    {
        public readonly IslandKind Kind;
        public readonly string Label;
        public readonly string Detail;
        public readonly int Count;

        public IslandContext(IslandKind kind, string label, string detail, int count)
        {
            Kind = kind;
            Label = label ?? "";
            Detail = detail ?? "";
            Count = count;
        }

        public bool HasWork => Kind != IslandKind.Idle && Count > 0;
    }

    public static class IslandSense
    {
        public static IslandKind KindOf(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return IslandKind.Files;
            var o = IslandOffers.Resolve(new[] { path });
            return o != null ? o.Kind : IslandKind.Files;
        }

        public static IslandContext FromFiles(IReadOnlyList<string> files)
        {
            if (files == null || files.Count == 0)
                return new IslandContext(IslandKind.Idle, "", "idle", 0);

            var seen = new HashSet<IslandKind>();
            for (var i = 0; i < files.Count; i++)
                seen.Add(KindOf(files[i]));

            IslandKind kind;
            if (seen.Count > 1)
                kind = IslandKind.Mixed;
            else
            {
                using (var e = seen.GetEnumerator())
                {
                    e.MoveNext();
                    kind = e.Current;
                }
            }

            var first = Path.GetFileName(files[0]);
            if (string.IsNullOrEmpty(first))
                first = files[0];
            if (first.Length > 18)
                first = first.Substring(0, 16) + "…";

            string detail;
            if (files.Count == 1)
                detail = LabelOf(kind);
            else if (kind == IslandKind.Mixed)
                detail = files.Count + " mixed";
            else
                detail = files.Count + " " + LabelOf(kind) + "s";

            return new IslandContext(kind, first, detail, files.Count);
        }

        public static string LabelOf(IslandKind kind)
        {
            switch (kind)
            {
                case IslandKind.Image: return "image";
                case IslandKind.Text: return "text";
                case IslandKind.Speak: return "speak";
                case IslandKind.Audio: return "audio";
                case IslandKind.Video: return "video";
                case IslandKind.Sheet: return "sheet";
                case IslandKind.Xml: return "xml";
                case IslandKind.Mixed: return "mixed";
                case IslandKind.Files: return "file";
                default: return "idle";
            }
        }
    }
}
