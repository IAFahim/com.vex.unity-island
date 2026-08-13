using System;

namespace Vex.Island
{
    public static class IslandChromeFlags
    {
        public const int Borderless = 1;
        public const int Topmost = 2;
        public const int SkipTaskbar = 4;
        public const int Shape = 8;
    }

    /// <summary>
    /// OS chrome. Core never references a platform assembly;
    /// each platform registers itself at player startup.
    /// </summary>
    public interface IIslandChrome
    {
        string Id { get; }
        bool Apply(int x, int y, int w, int h, int flags);
        void Move(int x, int y);
        void SetVisible(bool visible);
        void SetShape(int[] xywh, int count);
        void BeginDrag();
        void Drag();
        void EndDrag();
        bool TryPointer(out int x, out int y);
        IslandRect[] QueryScreens();
        void Overlay(int x, int y, int w, int h);
        void ArmEdge(int x, int y, int w, int h);
        string[] PollDrop();
        bool DragLive { get; }
        bool WantQuit { get; }
    }

    public static class IslandChrome
    {
        public static IIslandChrome Current { get; private set; } = IslandNullChrome.Instance;

        public static void Register(IIslandChrome chrome)
        {
            if (chrome != null)
                Current = chrome;
        }
    }

    public sealed class IslandNullChrome : IIslandChrome
    {
        public static readonly IslandNullChrome Instance = new IslandNullChrome();

        IslandNullChrome()
        {
        }

        public string Id => "none";
        public bool Apply(int x, int y, int w, int h, int flags) => false;
        public void Move(int x, int y) { }
        public void SetVisible(bool visible) { }
        public void SetShape(int[] xywh, int count) { }
        public void BeginDrag() { }
        public void Drag() { }
        public void EndDrag() { }

        public bool TryPointer(out int x, out int y)
        {
            x = 0;
            y = 0;
            return false;
        }

        public IslandRect[] QueryScreens() => Array.Empty<IslandRect>();
        public void Overlay(int x, int y, int w, int h) { }
        public void ArmEdge(int x, int y, int w, int h) { }
        public string[] PollDrop() => Array.Empty<string>();
        public bool DragLive => false;
        public bool WantQuit => false;
    }
}
