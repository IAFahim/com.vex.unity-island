using System;
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
        const string LinuxPlugin = "Packages/com.vex.island/Runtime/Platforms/Linux/Plugins/x86_64/libisland.so";
        const string PanelPath = "Packages/com.vex.island/Runtime/Unity/Resources/IslandPanel.asset";
        const string ThemePath = "Packages/com.vex.island/Runtime/Unity/Resources/UnityDefaultRuntimeTheme.tss";

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
            PlayerSettings.forceSingleInstance = false;
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

            var importer = AssetImporter.GetAtPath(LinuxPlugin) as PluginImporter;
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
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelPath);
            }

            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
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
            Build(BuildTarget.StandaloneLinux64, "Builds/Linux/Island");
        }

        [MenuItem("Island/Build Windows Player")]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Island.exe");
        }

        [MenuItem("Island/Build macOS Player")]
        public static void BuildOsx()
        {
            Build(BuildTarget.StandaloneOSX, "Builds/OSX/Island.app");
        }

        public static void SetupAndBuild()
        {
            var t = Environment.GetEnvironmentVariable("ISLAND_BUILD_TARGET");
            if (t == "win" || t == "windows")
                BuildWindows();
            else if (t == "osx" || t == "mac" || t == "macos")
                BuildOsx();
            else
                BuildLinux();
        }

        static void Build(BuildTarget target, string output)
        {
            Setup();
            var dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = output,
                target = target,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("Island " + target + " build failed: " + report.summary.result);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                return;
            }

            if (target == BuildTarget.StandaloneLinux64)
            {
                var pluginSrc = Path.GetFullPath(LinuxPlugin);
                var pluginDstDir = Path.GetFullPath("Builds/Linux/Island_Data/Plugins/x86_64");
                if (File.Exists(pluginSrc))
                {
                    Directory.CreateDirectory(pluginDstDir);
                    File.Copy(pluginSrc, Path.Combine(pluginDstDir, "libisland.so"), true);
                }
            }

            Debug.Log("Island " + target + " build ok -> " + output + " bytes=" + report.summary.totalSize);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
    }
}
