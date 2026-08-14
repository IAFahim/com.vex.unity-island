using UnityEditor;
using UnityEngine;

namespace Vex.Island.Editor
{
    [InitializeOnLoad]
    static class IslandEditorPlay
    {
        static IslandEditorPlay()
        {
            EditorApplication.playModeStateChanged += OnPlay;
        }

        static void OnPlay(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
                return;
            Fit();
            EditorApplication.delayCall += Fit;
        }

        static void Fit()
        {
            PlayModeWindow.SetCustomRenderingResolution(
                (uint)IslandMetrics.OpenWidth,
                (uint)IslandMetrics.Height,
                "Island");
        }
    }
}
