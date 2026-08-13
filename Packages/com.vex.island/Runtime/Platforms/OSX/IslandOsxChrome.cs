using System;
using UnityEngine;

namespace Vex.Island
{
    /// <summary>
    /// macOS chrome is not written yet. Registers so the player
    /// pipeline has a slot; Apply stays false until NSWindow work lands.
    /// </summary>
    public sealed class IslandOsxChrome : IIslandChrome
    {
        public string Id => "osx";
        public bool DragLive => false;
        public bool WantQuit => false;

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
    }

    static class IslandOsxBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            IslandChrome.Register(new IslandOsxChrome());
#endif
        }
    }
}
