using System;
using System.IO;
using NUnit.Framework;
using Vex.Island;

public class IslandLayoutTests
{
    static readonly IslandRect[] Dual =
    {
        new IslandRect(0, 0, 1920, 1080),
        new IslandRect(1920, 0, 1920, 1080)
    };

    [Test]
    public void VirtualTop_CentersOnCombinedWidth()
    {
        var p = IslandLayout.Dock(Dual, 100, 100, IslandEdge.Top, IslandSpan.VirtualDesktop, 420, 88, 14);
        Assert.AreEqual(0, p.Bound.X);
        Assert.AreEqual(3840, p.Bound.W);
        Assert.AreEqual((3840 - 420) / 2, p.X);
        Assert.AreEqual(14, p.Y);
    }

    [Test]
    public void ActiveMonitor_UsesPointerScreen()
    {
        var p = IslandLayout.Dock(Dual, 2500, 40, IslandEdge.Top, IslandSpan.ActiveMonitor, 420, 88, 14);
        Assert.AreEqual(1920, p.Bound.X);
        Assert.AreEqual(1920 + (1920 - 420) / 2, p.X);
    }

    [Test]
    public void HiddenTop_SitsAboveBound()
    {
        var shown = IslandLayout.Dock(Dual, 100, 100, IslandEdge.Top, IslandSpan.Primary, 420, 88, 14);
        var hidden = shown.Hidden(88);
        Assert.Less(hidden.Y, shown.Bound.Y);
    }

    [Test]
    public void Primary_PicksLargest()
    {
        var screens = new[]
        {
            new IslandRect(0, 640, 1080, 1920),
            new IslandRect(1080, 0, 1440, 2560),
        };
        var p = IslandLayout.Dock(screens, 100, 700, IslandEdge.Top, IslandSpan.Primary, 420, 88, 14);
        Assert.AreEqual(1080, p.Bound.X);
        Assert.AreEqual(1080 + (1440 - 420) / 2, p.X);
        Assert.AreEqual(14, p.Y);
    }

    [Test]
    public void VirtualLeft_UsesLeftmostScreen_Centered()
    {
        var desk = new[]
        {
            new IslandRect(0, 640, 1080, 1920),
            new IslandRect(1080, 0, 1440, 2560),
            new IslandRect(3600, 640, 1080, 1920),
        };
        var p = IslandLayout.Dock(desk, 200, 1000, IslandEdge.Left, IslandSpan.VirtualDesktop, 88, 420, 10);
        Assert.AreEqual(0, p.Bound.X);
        Assert.AreEqual(10, p.X);
        Assert.AreEqual(640 + (1920 - 420) / 2, p.Y);
        var r = IslandLayout.Dock(desk, 4000, 1000, IslandEdge.Right, IslandSpan.VirtualDesktop, 88, 420, 10);
        Assert.AreEqual(3600, r.Bound.X);
        Assert.AreEqual(3600 + 1080 - 88 - 10, r.X);
        Assert.AreEqual(IslandEdge.Left, IslandLayout.NearerOuter(desk, 200));
        Assert.AreEqual(IslandEdge.Right, IslandLayout.NearerOuter(desk, 4000));
    }

    [Test]
    public void Inside_IsWorldMinusBound()
    {
        var b = new IslandRect(1080, 0, 1440, 2560);
        int lx, ly;
        IslandLayout.Inside(b, 1090, 400, out lx, out ly);
        Assert.AreEqual(10, lx);
        Assert.AreEqual(400, ly);
        IslandLayout.Inside(b, 1080 + 1440 - 390, 10, out lx, out ly);
        Assert.AreEqual(1440 - 390, lx);
    }

    [Test]
    public void Along_FollowsGrab_NotMonitorCenter()
    {
        var desk = new[] { new IslandRect(0, 0, 1920, 1080) };
        var centered = IslandLayout.Dock(desk, 20, 800, IslandEdge.Left, IslandSpan.VirtualDesktop, 380, 420, 10);
        Assert.AreEqual((1080 - 420) / 2, centered.Y);
        var along = IslandLayout.Along(desk, 20, 400, 40, IslandEdge.Left, IslandSpan.VirtualDesktop, 380, 420, 10);
        Assert.AreEqual(10, along.X);
        Assert.AreEqual(360, along.Y);
        var hi = IslandLayout.Along(desk, 20, 2000, 0, IslandEdge.Left, IslandSpan.VirtualDesktop, 380, 420, 10);
        Assert.AreEqual(1080 - 420 - 10, hi.Y);
        var lo = IslandLayout.Along(desk, 20, 0, 0, IslandEdge.Left, IslandSpan.VirtualDesktop, 380, 420, 10);
        Assert.AreEqual(10, lo.Y);
    }

    [Test]
    public void Host_SlideY_SurvivesShownPlacement()
    {
        var desk = new[] { new IslandRect(0, 0, 1920, 1080) };
        var h = new IslandHost();
        h.Edge = IslandEdge.Left;
        h.SlideY = 500;
        var p = h.ShownPlacement(desk, 20, 100, 380);
        Assert.AreEqual(10, p.X);
        Assert.AreEqual(500, p.Y);
        h.SlideY = 0;
        var c = h.ShownPlacement(desk, 20, 100, 380);
        Assert.AreEqual(10, c.Y);
        h.SlideY = 900;
        var hi = h.ShownPlacement(desk, 20, 100, 380);
        Assert.AreEqual(1080 - 420 - 10, hi.Y);
    }

    [Test]
    public void BottomRight_UsesFarEdges()
    {
        var b = IslandLayout.Dock(Dual, 100, 100, IslandEdge.Bottom, IslandSpan.Primary, 420, 88, 10);
        Assert.AreEqual(1080 - 88 - 10, b.Y);
        var r = IslandLayout.Dock(Dual, 100, 100, IslandEdge.Right, IslandSpan.Primary, 420, 88, 10);
        Assert.AreEqual(1920 - 420 - 10, r.X);
    }

    [Test]
    public void DecodeFileToken_UriAndPlain()
    {
        Assert.AreEqual("/tmp/a.png", IslandPaths.DecodeFileToken("file:///tmp/a.png"));
        Assert.AreEqual("/tmp/a b.png", IslandPaths.DecodeFileToken("file:///tmp/a%20b.png"));
        Assert.AreEqual("notes.md", IslandPaths.DecodeFileToken("notes.md"));
        Assert.AreEqual("", IslandPaths.DecodeFileToken("# comment"));
        Assert.AreEqual("/tmp/a.png", IslandPaths.ParseUriList("file:///tmp/a.png\n# x\n")[0]);
    }

    [Test]
    public void EditorChrome_IsNull()
    {
        Assert.AreSame(IslandNullChrome.Instance, IslandChrome.Current);
        Assert.AreEqual("none", IslandChrome.Current.Id);
        Assert.IsFalse(IslandChrome.Current.DragLive);
        Assert.AreEqual(0, IslandChrome.Current.QueryScreens().Length);
    }

    [Test]
    public void Dismiss_HidesAndClearsFiles()
    {
        var h = new IslandHost();
        h.ShowFiles(new[] { "/tmp/a.png" });
        Assert.IsTrue(h.Visible);
        Assert.AreEqual(IslandMode.Photo, h.Mode);
        Assert.AreEqual(1, h.Files.Count);
        h.Dismiss();
        Assert.IsFalse(h.Visible);
        Assert.AreEqual(IslandMode.Idle, h.Mode);
        Assert.AreEqual(0, h.Files.Count);
    }

    [Test]
    public void ShowFiles_KeepsEveryPath()
    {
        var h = new IslandHost();
        h.ShowFiles(new[] { "/tmp/a.md", "/tmp/b.md", "/tmp/c.txt" });
        Assert.AreEqual(3, h.Files.Count);
        Assert.AreEqual("/tmp/a.md", h.Files[0]);
        Assert.AreEqual("/tmp/c.txt", h.Files[2]);
    }

    [Test]
    public void Sense_ImageAndMixed()
    {
        Assert.AreEqual(IslandKind.Image, IslandSense.KindOf("a.PNG"));
        Assert.AreEqual(IslandKind.Speak, IslandSense.KindOf("/tmp/note.md"));
        Assert.AreEqual(IslandKind.Text, IslandSense.KindOf("/tmp/note.cs"));
        var one = IslandSense.FromFiles(new[] { "/tmp/pic.webp" });
        Assert.AreEqual(IslandKind.Image, one.Kind);
        Assert.AreEqual("image", one.Detail);
        var mix = IslandSense.FromFiles(new[] { "/tmp/a.png", "/tmp/b.md" });
        Assert.AreEqual(IslandKind.Mixed, mix.Kind);
        Assert.AreEqual(2, mix.Count);
        Assert.IsTrue(mix.HasWork);
    }

    [Test]
    public void AddFiles_SkipsDuplicates()
    {
        var h = new IslandHost();
        h.ShowFiles(new[] { "/tmp/a.png" });
        h.AddFiles(new[] { "/tmp/a.png", "/tmp/b.webp" });
        Assert.AreEqual(2, h.Files.Count);
        Assert.AreEqual("/tmp/b.webp", h.Files[1]);
    }

    [Test]
    public void Sense_SheetAndXml()
    {
        Assert.AreEqual(IslandKind.Sheet, IslandSense.KindOf("grid.xlsx"));
        Assert.AreEqual(IslandKind.Sheet, IslandSense.KindOf("/tmp/rows.csv"));
        Assert.AreEqual(IslandKind.Xml, IslandSense.KindOf("doc.xml"));
        Assert.AreEqual(IslandKind.Speak, IslandSense.KindOf("note.md"));
        Assert.AreEqual(IslandKind.Text, IslandSense.KindOf("note.cs"));
        var sheet = IslandSense.FromFiles(new[] { "/tmp/a.xlsx", "/tmp/b.csv" });
        Assert.AreEqual(IslandKind.Sheet, sheet.Kind);
        Assert.AreEqual("2 sheets", sheet.Detail);
        var xml = IslandSense.FromFiles(new[] { "/tmp/a.xml" });
        Assert.AreEqual(IslandKind.Xml, xml.Kind);
        Assert.AreEqual("xml", xml.Detail);
    }

    [Test]
    public void Offers_Process_ByKind()
    {
        Assert.AreEqual("image", IslandOffers.Resolve(new[] { "/tmp/a.PNG" }).Id);
        Assert.AreEqual("sheet", IslandOffers.Resolve(new[] { "/tmp/a.xlsx" }).Id);
        Assert.AreEqual("xml", IslandOffers.Resolve(new[] { "/tmp/a.xml" }).Id);
        Assert.IsNull(IslandOffers.Resolve(new[] { "/tmp/a.png", "/tmp/b.xml" }));
        Assert.AreEqual("photo:0", IslandOffers.Process(new[] { "/tmp/a.png" }));
        Assert.AreEqual("sheet:2", IslandOffers.Process(new[] { "/tmp/a.xlsx", "/tmp/b.csv" }));
        Assert.AreEqual("mixed:2", IslandOffers.Process(new[] { "/tmp/a.png", "/tmp/b.xml" }));
        Assert.AreEqual("idle", IslandOffers.Process(Array.Empty<string>()));
    }

    [Test]
    public void Host_ProcessFiles_SetsNote()
    {
        var h = new IslandHost();
        h.ShowFiles(new[] { "/tmp/a.xml" });
        Assert.AreEqual(IslandKind.Xml, h.Context.Kind);
        Assert.AreEqual("", h.LastNote);
        Assert.AreEqual("xml:1", h.ProcessFiles());
        Assert.AreEqual("xml:1", h.LastNote);
        h.ShowFiles(new[] { "/tmp/a.png" });
        Assert.AreEqual("", h.LastNote);
        Assert.AreEqual(IslandKind.Image, h.Context.Kind);
    }

    [Test]
    public void Speak_CleanAndPreview()
    {
        Assert.AreEqual("hello world", IslandSpeak.Clean("hello   **world**"));
        Assert.AreEqual("see docs", IslandSpeak.Clean("see [docs](https://x.test)"));
        Assert.AreEqual("code block.", IslandSpeak.Clean("```cs\nvoid x(){}\n```"));
        Assert.AreEqual("short", IslandSpeak.Preview("short"));
        Assert.AreEqual("this is a very lo…", IslandSpeak.Preview("this is a very long line of text", 18));
        Assert.AreEqual("empty", new IslandHost().SpeakNow("   "));
        Assert.AreEqual("speak", IslandOffers.Resolve(new[] { "/tmp/note.md" }).Id);
        Assert.AreEqual("speak", IslandOffers.Resolve(new[] { "/tmp/message(2).txt" }).Id);
        Assert.AreEqual("text", IslandOffers.Resolve(new[] { "/tmp/note.cs" }).Id);
        var empty = Path.Combine(Path.GetTempPath(), "island-empty-speak.txt");
        File.WriteAllText(empty, "   ");
        var h = new IslandHost();
        Assert.AreEqual("empty", h.TakeDrop(new[] { empty }));
        Assert.AreEqual(IslandMode.Speak, h.Mode);
        Assert.AreEqual(1, h.Files.Count);
    }

    [Test]
    public void Speak_StatusEmptyWhenNotLive()
    {
        Assert.IsFalse(IslandSpeak.IsLive);
        Assert.AreEqual("", IslandSpeak.Status());
    }

    [Test]
    public void Wiggle_SelfCheck()
    {
        Assert.IsTrue(IslandWiggle.SelfCheck());
    }

    [Test]
    public void Voice_FeelPresets()
    {
        var v = new IslandVoice();
        v.ApplyFeel("stubborn");
        Assert.AreEqual(8, v.WiggleFlips);
        Assert.AreEqual("stubborn", v.WiggleFeel);
        v.ApplyFeel("normal");
        Assert.AreEqual(5, v.WiggleFlips);
        var loaded = IslandVoice.Load();
        Assert.Greater(loaded.Speed, 0.4);
        Assert.Less(loaded.Speed, 3.1);
    }
}
