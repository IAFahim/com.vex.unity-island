using System;
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
        Assert.AreEqual(IslandMode.Files, h.Mode);
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
        h.ShowFiles(new[] { "/tmp/a.png", "/tmp/b.md", "/tmp/c.txt" });
        Assert.AreEqual(3, h.Files.Count);
        Assert.AreEqual("/tmp/a.png", h.Files[0]);
        Assert.AreEqual("/tmp/c.txt", h.Files[2]);
    }

    [Test]
    public void Sense_ImageAndMixed()
    {
        Assert.AreEqual(IslandKind.Image, IslandSense.KindOf("a.PNG"));
        Assert.AreEqual(IslandKind.Text, IslandSense.KindOf("/tmp/note.md"));
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
        h.AddFiles(new[] { "/tmp/a.png", "/tmp/b.md" });
        Assert.AreEqual(2, h.Files.Count);
        Assert.AreEqual("/tmp/b.md", h.Files[1]);
    }

    [Test]
    public void Sense_SheetAndXml()
    {
        Assert.AreEqual(IslandKind.Sheet, IslandSense.KindOf("grid.xlsx"));
        Assert.AreEqual(IslandKind.Sheet, IslandSense.KindOf("/tmp/rows.csv"));
        Assert.AreEqual(IslandKind.Xml, IslandSense.KindOf("doc.xml"));
        Assert.AreEqual(IslandKind.Text, IslandSense.KindOf("note.md"));
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
        Assert.AreEqual("image:1", IslandOffers.Process(new[] { "/tmp/a.png" }));
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
}
