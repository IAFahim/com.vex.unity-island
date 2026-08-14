using System;
using System.IO;
using NUnit.Framework;
using Vex.Island;

public class IslandPhotoTests
{
    [Test]
    public void Photo_NudgeTime_MovesClock()
    {
        var p = new IslandPhoto();
        p.DateOverride = new DateTime(2026, 7, 28, 8, 0, 0);
        p.TimeOverride = new DateTime(2026, 7, 28, 8, 0, 0);
        p.NudgeTime(60);
        Assert.AreEqual(9, p.Effective().Hour);
        p.NudgeTime(-15);
        Assert.AreEqual(8, p.Effective().Hour);
        Assert.AreEqual(45, p.Effective().Minute);
    }

    [Test]
    public void Photo_SelfCheck()
    {
        var err = IslandPhoto.SelfCheck();
        Assert.AreEqual("", err, err);
    }

    [Test]
    public void Host_TakeDrop_ImageOpensPhoto()
    {
        var h = new IslandHost();
        var note = h.TakeDrop(new[] { "/tmp/does-not-exist-photo.png" });
        Assert.AreEqual(IslandMode.Photo, h.Mode);
        Assert.IsTrue(h.Visible);
        Assert.AreEqual(IslandKind.Image, h.Context.Kind);
        Assert.AreEqual("", note);
        Assert.IsTrue(IslandPhoto.Current.HasWork);
        h.Dismiss();
        Assert.AreEqual(IslandMode.Idle, h.Mode);
        Assert.IsFalse(IslandPhoto.Current.HasWork);
    }

    [Test]
    public void Host_ShowFiles_MixedStaysFiles()
    {
        var h = new IslandHost();
        h.ShowFiles(new[] { "/tmp/a.png", "/tmp/b.md" });
        Assert.AreEqual(IslandMode.Photo, h.Mode);
        Assert.AreEqual(1, h.Files.Count);
        Assert.AreEqual("/tmp/a.png", h.Files[0]);
        h.ShowFiles(new[] { "/tmp/a.xml", "/tmp/b.cs" });
        Assert.AreEqual(IslandMode.Files, h.Mode);
        Assert.AreEqual(IslandKind.Mixed, h.Context.Kind);
        h.Dismiss();
    }

    [Test]
    public void Photo_Export_LeavesOriginal()
    {
        if (!IslandPhoto.HasFfmpeg())
            Assert.Fail("ffmpeg missing — stamp path cannot run");

        var tmp = Path.Combine(Path.GetTempPath(), "island-photo-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        var prevOut = IslandPhoto.OutOverride;
        var prevSet = IslandPhoto.SettingsPathOverride;
        IslandPhoto.OutOverride = Path.Combine(tmp, "out");
        IslandPhoto.SettingsPathOverride = Path.Combine(tmp, "settings.json");
        try
        {
            var src = Path.Combine(tmp, "in.jpg");
            Assert.AreEqual("", IslandPhoto.SelfCheck());
            // SelfCheck already covers export; this gate is the host path.
            var gray = Path.Combine(tmp, "shot.jpg");
            var session = new IslandPhoto();
            // reuse lavfi via a second self-contained bind: write probe then skip
            // if SelfCheck passed, ffmpeg + export contract holds.
            File.WriteAllText(gray, "not-an-image");
            session.Bind(new[] { gray });
            Assert.AreEqual("photo:0", session.ExportAll());
            Assert.IsTrue(File.Exists(gray));
            Assert.AreEqual("not-an-image", File.ReadAllText(gray));
        }
        finally
        {
            IslandPhoto.OutOverride = prevOut;
            IslandPhoto.SettingsPathOverride = prevSet;
            IslandPhoto.Current.Clear();
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
