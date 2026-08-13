using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vex.Island.Editor
{
    public static class IslandBuilder
    {
        const string Scene = "Assets/Scenes/SampleScene.unity";
        const string LinuxOut = "Builds/Linux/Island";

        [MenuItem("Island/Apply Player Settings")]
        public static void Setup()
        {
            PlayerSettings.companyName = "Vex";
            PlayerSettings.productName = "Island";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = IslandWindow.Width;
            PlayerSettings.defaultScreenHeight = IslandWindow.Height;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.useFlipModelSwapchain = false;
            PlayerSettings.forceSingleInstance = true;
            PlayerSettings.gpuSkinning = false;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64,
                new[]
                {
                    UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                    UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore
                });
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            QualitySettings.vSyncCount = 0;
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Scene, true)
            };

            const string plugin = "Assets/Plugins/Linux/x86_64/libisland.so";
            var importer = AssetImporter.GetAtPath(plugin) as PluginImporter;
            if (importer != null)
            {
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, true);
                importer.SaveAndReimport();
            }

            EnsurePanelSettings();
            EnsureWatcher();
            Debug.Log("Island settings applied");
        }

        static void EnsurePanelSettings()
        {
            const string path = "Assets/Island/Resources/IslandPanel.asset";
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }

            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/Island/Resources/UnityDefaultRuntimeTheme.tss");
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.referenceResolution = new Vector2Int(IslandWindow.Width, IslandWindow.Height);
            settings.clearColor = true;
            settings.colorClearValue = Color.clear;
            settings.clearDepthStencil = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        static void EnsureWatcher()
        {
            const string src = "scripts/island-watch.py";
            const string dst = "Assets/StreamingAssets/island-watch.py";
            if (!File.Exists(src))
                return;
            Directory.CreateDirectory("Assets/StreamingAssets");
            File.Copy(src, dst, true);
        }

        [MenuItem("Island/Build Linux Player")]
        public static void BuildLinux()
        {
            Setup();
            var dir = Path.GetDirectoryName(LinuxOut);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = LinuxOut,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("Island Linux build failed: " + report.summary.result);
#if UNITY_EDITOR
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
#endif
                return;
            }

            var pluginSrc = Path.GetFullPath("Assets/Plugins/Linux/x86_64/libisland.so");
            var pluginDstDir = Path.GetFullPath("Builds/Linux/Island_Data/Plugins/x86_64");
            if (File.Exists(pluginSrc))
            {
                Directory.CreateDirectory(pluginDstDir);
                File.Copy(pluginSrc, Path.Combine(pluginDstDir, "libisland.so"), true);
            }

            Debug.Log("Island Linux build ok -> " + LinuxOut + " bytes=" + report.summary.totalSize);
#if UNITY_EDITOR
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
#endif
        }

        public static void SetupAndBuild()
        {
            BuildLinux();
        }
    }
}
