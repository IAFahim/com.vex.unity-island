using NUnit.Framework;
using Vex.Island;

public class IslandQuietTests
{
    [Test]
    public void Wait_BenchIsLongerThanPill()
    {
        Assert.AreEqual(8f, IslandQuiet.Wait(false));
        Assert.AreEqual(20f, IslandQuiet.Wait(true));
        Assert.Greater(IslandQuiet.Wait(true), IslandQuiet.Wait(false));
    }

    [Test]
    public void Hidden_AndGone_AreZero()
    {
        Assert.AreEqual(IslandQuiet.Phase.Hidden, IslandQuiet.Of(false, 10f, 10f, 8f, false));
        Assert.AreEqual(0f, IslandQuiet.Opacity(IslandQuiet.Phase.Hidden, 10f, 10f, 8f));
        Assert.AreEqual(0f, IslandQuiet.Opacity(IslandQuiet.Phase.Gone, 10f, 10f, 8f));
    }

    [Test]
    public void Appear_ThenAlive_ThenFade_ThenGone()
    {
        const float wait = 8f;
        Assert.AreEqual(IslandQuiet.Phase.Appearing, IslandQuiet.Of(true, 0f, 0f, wait, false));
        Assert.AreEqual(0f, IslandQuiet.Opacity(IslandQuiet.Phase.Appearing, 0f, 0f, wait));
        Assert.AreEqual(0.5f, IslandQuiet.Opacity(IslandQuiet.Phase.Appearing, IslandQuiet.Appear * 0.5f, 0f, wait), 0.001f);

        Assert.AreEqual(IslandQuiet.Phase.Alive, IslandQuiet.Of(true, IslandQuiet.Appear, 0f, wait, false));
        Assert.AreEqual(1f, IslandQuiet.Opacity(IslandQuiet.Phase.Alive, 1f, 0f, wait));

        Assert.AreEqual(IslandQuiet.Phase.Alive, IslandQuiet.Of(true, 2f, wait - 0.01f, wait, false));
        Assert.AreEqual(IslandQuiet.Phase.Fading, IslandQuiet.Of(true, 2f, wait, wait, false));
        Assert.AreEqual(1f, IslandQuiet.Opacity(IslandQuiet.Phase.Fading, 2f, wait, wait));
        Assert.AreEqual(0.5f, IslandQuiet.Opacity(IslandQuiet.Phase.Fading, 2f, wait + IslandQuiet.Fade * 0.5f, wait), 0.001f);

        Assert.AreEqual(IslandQuiet.Phase.Gone, IslandQuiet.Of(true, 2f, wait + IslandQuiet.Fade + 0.01f, wait, false));
        Assert.AreEqual(0f, IslandQuiet.Opacity(IslandQuiet.Phase.Gone, 2f, wait + IslandQuiet.Fade + 0.01f, wait));
    }

    [Test]
    public void Held_NeverFades()
    {
        var phase = IslandQuiet.Of(true, 10f, 100f, 8f, true);
        Assert.AreEqual(IslandQuiet.Phase.Alive, phase);
        Assert.AreEqual(1f, IslandQuiet.Opacity(phase, 10f, 100f, 8f));
    }

    [Test]
    public void Appear_WinsOverHeldAndIdle()
    {
        Assert.AreEqual(IslandQuiet.Phase.Appearing, IslandQuiet.Of(true, 0f, 100f, 8f, true));
    }

    [Test]
    public void SameInputs_SamePhase()
    {
        var a = IslandQuiet.Of(true, 1f, 3f, 8f, false);
        var b = IslandQuiet.Of(true, 1f, 3f, 8f, false);
        Assert.AreEqual(a, b);
        Assert.AreEqual(IslandQuiet.Opacity(a, 1f, 3f, 8f), IslandQuiet.Opacity(b, 1f, 3f, 8f));
    }
}
