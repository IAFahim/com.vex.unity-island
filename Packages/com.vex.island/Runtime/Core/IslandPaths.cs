using System;

namespace Vex.Island
{
    public static class IslandPaths
    {
        public static string DecodeFileToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                return "";
            line = line.Replace("\\ ", " ");
            if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return new Uri(line).LocalPath;
                }
                catch
                {
                    line = Uri.UnescapeDataString(line.Substring(7));
                    if (line.StartsWith("localhost/", StringComparison.OrdinalIgnoreCase))
                        line = line.Substring(9);
                }
            }
            else
                line = Uri.UnescapeDataString(line);
            return line;
        }

        public static string[] ParseUriList(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();
            var list = new System.Collections.Generic.List<string>();
            var lines = text.Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = DecodeFileToken(lines[i]);
                if (line.Length > 0)
                    list.Add(line);
            }

            return list.ToArray();
        }

        public static string Ext(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            var i = path.LastIndexOf('.');
            if (i < 0 || i == path.Length - 1)
                return "";
            var slash = path.LastIndexOfAny(new[] { '/', '\\' });
            if (slash > i)
                return "";
            return path.Substring(i + 1).ToLowerInvariant();
        }
    }
}
