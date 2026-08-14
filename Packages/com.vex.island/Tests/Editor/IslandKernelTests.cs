using System;
using NUnit.Framework;
using Vex.Island;

public class IslandKernelTests
{
    static readonly IslandFrame Origin =
        IslandKernel.Idle(IslandEdge.Left, 400, IslandSpan.VirtualDesktop);

    [Test]
    public void Hold_Empty_IsIdle_KeepsPose()
    {
        var held = IslandKernel.Hold(Origin, Array.Empty<string>());
        Assert.AreEqual(IslandMode.Idle, held.Mode);
        Assert.IsFalse(held.Visible);
        Assert.AreEqual(IslandEdge.Left, held.Edge);
        Assert.AreEqual(400, held.SlideY);
        Assert.IsTrue(held.Equals(IslandKernel.Hold(Origin, null)));
    }

    [Test]
    public void Hold_Png_OpensPhotoBench_Holds()
    {
        var held = IslandKernel.Hold(Origin, new[] { "/tmp/a.png" });
        Assert.AreEqual(IslandMode.Photo, held.Mode);
        Assert.AreEqual("image", held.OfferId);
        Assert.IsTrue(held.Holds);
        Assert.IsTrue(held.OpensBench);
        Assert.IsFalse(held.ActsOnDrop);
        Assert.IsTrue(held.Bench);
        Assert.IsTrue(held.Shows);
        Assert.AreEqual(400, held.SlideY);
    }

    [Test]
    public void Hold_Txt_Holds_ActsOnDrop_NoBench()
    {
        var held = IslandKernel.Hold(Origin, new[] { "/tmp/note.md" });
        Assert.AreEqual(IslandMode.Speak, held.Mode);
        Assert.AreEqual("speak", held.OfferId);
        Assert.IsTrue(held.Holds);
        Assert.IsFalse(held.OpensBench);
        Assert.IsTrue(held.ActsOnDrop);
        Assert.IsFalse(held.Bench);
    }

    [Test]
    public void Dismiss_DropsWork_KeepsPose()
    {
        var held = IslandKernel.Hold(Origin, new[] { "/tmp/a.png" });
        held = IslandKernel.Pose(held, IslandEdge.Right, 220);
        var gone = IslandKernel.Dismiss(held);
        Assert.AreEqual(IslandMode.Idle, gone.Mode);
        Assert.AreEqual(0, gone.Count);
        Assert.AreEqual(IslandEdge.Right, gone.Edge);
        Assert.AreEqual(220, gone.SlideY);
        Assert.IsFalse(gone.Shows);
    }

    [Test]
    public void Hold_MixedWithPhoto_KeepsImagesOnly()
    {
        var held = IslandKernel.Hold(Origin, new[] { "/tmp/a.png", "/tmp/note.md" });
        Assert.AreEqual(IslandMode.Photo, held.Mode);
        Assert.AreEqual(1, held.Count);
        Assert.AreEqual("/tmp/a.png", held.Files[0]);
        var mix = IslandKernel.Hold(Origin, new[] { "/tmp/a.xml", "/tmp/b.cs" });
        Assert.AreEqual(IslandMode.Files, mix.Mode);
        Assert.AreEqual(2, mix.Count);
        Assert.IsFalse(mix.OpensBench);
    }

    [Test]
    public void Hold_SamePaths_Equal()
    {
        var a = IslandKernel.Hold(Origin, new[] { "/tmp/a.png", "/tmp/b.png" });
        var b = IslandKernel.Hold(Origin, new[] { "/tmp/a.png", "/tmp/b.png" });
        Assert.IsTrue(a.Equals(b));
        Assert.AreEqual(3, IslandKernel.Append(a.Files, new[] { "/tmp/a.png", "/tmp/c.png" }).Length);
    }

    [Test]
    public void Host_TakeDrop_Image_UsesKernel()
    {
        var h = new IslandHost();
        h.SlideY = 500;
        h.TakeDrop(new[] { "/tmp/a.png" });
        Assert.AreEqual(IslandMode.Photo, h.Mode);
        Assert.IsTrue(h.Frame.Holds);
        Assert.IsTrue(h.Frame.Bench);
        Assert.AreEqual(500, h.SlideY);
        h.Dismiss();
        Assert.AreEqual(IslandMode.Idle, h.Mode);
        Assert.AreEqual(500, h.SlideY);
        Assert.AreEqual(0, h.Files.Count);
    }
}
