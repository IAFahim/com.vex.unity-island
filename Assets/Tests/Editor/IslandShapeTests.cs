using NUnit.Framework;
using Vex.Island;

public class IslandShapeTests
{
    [Test]
    public void RoundedRect_CoversCenter_NotSharpCorners()
    {
        const int w = 100;
        const int h = 40;
        const int r = 12;
        var xywh = new int[h * 4];
        var n = IslandShape.BuildRoundedRect(w, h, r, xywh);
        Assert.Greater(n, 2);
        Assert.IsTrue(IslandShape.Contains(xywh, n, w / 2, h / 2));
        Assert.IsFalse(IslandShape.Contains(xywh, n, 0, 0));
        Assert.IsFalse(IslandShape.Contains(xywh, n, w - 1, 0));
        Assert.IsFalse(IslandShape.Contains(xywh, n, 0, h - 1));
        Assert.IsFalse(IslandShape.Contains(xywh, n, w - 1, h - 1));
        Assert.IsTrue(IslandShape.Contains(xywh, n, r, h / 2));
        Assert.IsTrue(IslandShape.Contains(xywh, n, w / 2, 0));
    }

    [Test]
    public void RoundedRect_ZeroRadius_IsFullRectangle()
    {
        var xywh = new int[16];
        var n = IslandShape.BuildRoundedRect(20, 10, 0, xywh);
        Assert.AreEqual(1, n);
        Assert.AreEqual(0, xywh[0]);
        Assert.AreEqual(0, xywh[1]);
        Assert.AreEqual(20, xywh[2]);
        Assert.AreEqual(10, xywh[3]);
    }
}
