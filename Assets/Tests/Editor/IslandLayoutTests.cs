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
        Assert.AreEqual(-88, hidden.Y);
    }

    [Test]
    public void BottomRight_UsesFarEdges()
    {
        var b = IslandLayout.Dock(Dual, 100, 100, IslandEdge.Bottom, IslandSpan.Primary, 420, 88, 10);
        Assert.AreEqual(1080 - 88 - 10, b.Y);
        var r = IslandLayout.Dock(Dual, 100, 100, IslandEdge.Right, IslandSpan.Primary, 420, 88, 10);
        Assert.AreEqual(1920 - 420 - 10, r.X);
    }
}
