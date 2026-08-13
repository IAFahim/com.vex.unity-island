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
        Assert.AreEqual("/tmp/a.png", IslandWindow.DecodeFileToken("file:///tmp/a.png"));
        Assert.AreEqual("/tmp/a b.png", IslandWindow.DecodeFileToken("file:///tmp/a%20b.png"));
        Assert.AreEqual("notes.md", IslandWindow.DecodeFileToken("notes.md"));
        Assert.AreEqual("", IslandWindow.DecodeFileToken("# comment"));
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
}
