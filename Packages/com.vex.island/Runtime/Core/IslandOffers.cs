using System.Collections.Generic;
using System.IO;

namespace Vex.Island
{
    public interface IIslandOffer
    {
        string Id { get; }
        IslandKind Kind { get; }
        bool Holds { get; }
        bool OpensBench { get; }
        bool ActsOnDrop { get; }
        bool Accepts(string path);
        string Process(IReadOnlyList<string> files);
    }

    public static class IslandOffers
    {
        static readonly List<IIslandOffer> All = new List<IIslandOffer>();
        static List<string> _classes;

        static IslandOffers()
        {
            Register(new PhotoOffer());
            Register(new ExtOffer("image", IslandKind.Image, "svg", "exr"));
            Register(new ExtOffer("sheet", IslandKind.Sheet,
                "xlsx", "xls", "xlsm", "csv", "tsv", "ods"));
            Register(new ExtOffer("xml", IslandKind.Xml,
                "xml", "xsd", "xsl", "xslt"));
            Register(new SpeakOffer());
            Register(new ExtOffer("text", IslandKind.Text,
                "cs", "json", "html", "py", "rs", "c", "h"));
            Register(new ExtOffer("audio", IslandKind.Audio,
                "mp3", "wav", "flac", "ogg"));
            Register(new ExtOffer("video", IslandKind.Video,
                "mp4", "webm", "mkv", "mov"));
        }

        public static void Register(IIslandOffer offer)
        {
            if (offer == null)
                return;
            All.Add(offer);
            _classes = null;
        }

        public static IReadOnlyList<string> ClassNames()
        {
            if (_classes != null)
                return _classes;
            var list = new List<string>(All.Count + 1) { "files" };
            for (var i = 0; i < All.Count; i++)
            {
                if (!list.Contains(All[i].Id))
                    list.Add(All[i].Id);
            }

            _classes = list;
            return list;
        }

        public static IIslandOffer Resolve(IReadOnlyList<string> files)
        {
            if (files == null || files.Count == 0)
                return null;
            for (var i = 0; i < All.Count; i++)
            {
                var ok = true;
                for (var f = 0; f < files.Count; f++)
                {
                    if (!All[i].Accepts(files[f]))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    return All[i];
            }

            return null;
        }

        public static IslandContext Read(IReadOnlyList<string> files)
        {
            return IslandSense.FromFiles(files);
        }

        public static string Process(IReadOnlyList<string> files)
        {
            if (files == null || files.Count == 0)
                return "idle";
            var o = Resolve(files);
            if (o != null)
                return o.Process(files);
            return "mixed:" + files.Count;
        }

        sealed class PhotoOffer : IIslandOffer
        {
            public string Id => "image";
            public IslandKind Kind => IslandKind.Image;
            public bool Holds => true;
            public bool OpensBench => true;
            public bool ActsOnDrop => false;

            public bool Accepts(string path)
            {
                return IslandPhoto.Accepts(path);
            }

            public string Process(IReadOnlyList<string> files)
            {
                if (!IslandPhoto.Current.SameFiles(files))
                    IslandPhoto.Current.Bind(files);
                return IslandPhoto.Current.ExportAll();
            }
        }

        sealed class SpeakOffer : IIslandOffer
        {
            public string Id => "speak";
            public IslandKind Kind => IslandKind.Speak;
            public bool Holds => true;
            public bool OpensBench => false;
            public bool ActsOnDrop => true;

            public bool Accepts(string path)
            {
                var e = IslandPaths.Ext(path);
                return e == "txt" || e == "md" || e == "markdown" || e == "log" || e == "rst" || e == "text";
            }

            public string Process(IReadOnlyList<string> files)
            {
                try
                {
                    var text = File.ReadAllText(files[0]);
                    var note = IslandSpeak.Speak(text);
                    return note == "empty" ? "speak:0" : note;
                }
                catch
                {
                    return "speak:0";
                }
            }
        }

        sealed class ExtOffer : IIslandOffer
        {
            readonly HashSet<string> _ext;

            public string Id { get; }
            public IslandKind Kind { get; }
            public bool Holds => false;
            public bool OpensBench => false;
            public bool ActsOnDrop => false;

            public ExtOffer(string id, IslandKind kind, params string[] ext)
            {
                Id = id;
                Kind = kind;
                _ext = new HashSet<string>(ext);
            }

            public bool Accepts(string path)
            {
                return _ext.Contains(IslandPaths.Ext(path));
            }

            public string Process(IReadOnlyList<string> files)
            {
                return Id + ":" + files.Count;
            }
        }
    }
}
