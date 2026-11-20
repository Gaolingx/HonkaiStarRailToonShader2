using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Builds a standalone capture player for the RenderDoc comparison (validation only):
    ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt;
    ///     -executeMethod SRCharacter.SRCharacterCaptureBuild.Run
    /// It creates a throw-away URP asset + renderer data with the SRCharacter feature, a scene with
    /// a capsule (character material) and a glass shell (transparent material), then builds
    /// Builds/ShellCapture/ShellCapture.exe. The temporarily assigned pipeline assets are restored
    /// before the editor exits; the generated assets stay under Assets/_SRCharacterVerify/capture.
    /// </summary>
    public static class SRCharacterCaptureBuild
    {
        private const string Root = "Assets/_SRCharacterVerify";
        private const string CaptureDir = Root + "/capture";
        private const string PlayerPath = "Builds/ShellCapture/ShellCapture.exe";

        public static void Run()
        {
            RenderPipelineAsset previousDefault = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset previousQuality = QualitySettings.renderPipeline;
            int exitCode = 1;

            try
            {
                System.IO.Directory.CreateDirectory(CaptureDir);
                AssetDatabase.Refresh();

                UniversalRendererData rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, CaptureDir + "/CaptureRenderer.asset");

                SRCharacterFeature feature = ScriptableObject.CreateInstance<SRCharacterFeature>();
                feature.settings.ResolveShaders();
                feature.settings.characterLayers = ~0;
                feature.settings.enableTransparent = true;
                rendererData.rendererFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.SetDirty();

                UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.msaaSampleCount = 1;
                AssetDatabase.CreateAsset(pipeline, CaptureDir + "/CapturePipeline.asset");

                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = null;

                BuildScene();

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = new[] { CaptureDir + "/ShellCapture.unity" },
                    locationPathName = PlayerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };
                UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
                Debug.Log($"[SRCharacterCaptureBuild] result={report.summary.result} size={report.summary.totalSize} " +
                          $"path={PlayerPath} shaders={feature.settings.transparentShader != null}");
                exitCode = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1;
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[SRCharacterCaptureBuild] threw: " + exception);
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = previousDefault;
                QualitySettings.renderPipeline = previousQuality;
            }

            EditorApplication.Exit(exitCode);
        }

        private static void BuildScene()
        {
            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0.25f, 1f);
            camera.transform.position = new Vector3(0f, 0f, -4f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            GameObject lightGo = new GameObject("Key Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

            Shader characterShader = Shader.Find("SRCharacter/Character");
            Shader transparentShader = Shader.Find(SRCharacterShaderIDs.TransparentShaderName);

            Material bodyMaterial = new Material(characterShader) { name = "CaptureBody" };
            bodyMaterial.SetColor("_BaseColor", new Color(0.9f, 0.7f, 0.7f, 1f));
            bodyMaterial.SetColor("_OutlineColor", Color.black);
            bodyMaterial.SetFloat("_OutlineWidth", 1.5f);
            bodyMaterial.SetFloat(SRCharacterShaderIDs.Area, 1);
            bodyMaterial.SetFloat(SRCharacterShaderIDs.ShadingGroup, 0);
            bodyMaterial.SetFloat(SRCharacterShaderIDs.MaterialID, 16);
            AssetDatabase.CreateAsset(bodyMaterial, CaptureDir + "/CaptureBody.mat");

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Character";
            capsule.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            capsule.AddComponent<SRCharacterRenderer>().Apply();

            // Same shell setup as the verify ablation: below the height line, all five layers on.
            Material shellMaterial = new Material(transparentShader) { name = "CaptureShell" };
            shellMaterial.SetFloat(SRCharacterShaderIDs.Alpha, 1f);
            shellMaterial.SetFloat(SRCharacterShaderIDs.TransparentHeightLine, 1000f);
            AssetDatabase.CreateAsset(shellMaterial, CaptureDir + "/CaptureShell.mat");

            GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "TransparentShell";
            shell.transform.position = new Vector3(0f, 0f, -0.9f);
            shell.transform.localScale = Vector3.one * 0.5f;
            shell.GetComponent<Renderer>().sharedMaterial = shellMaterial;

            EditorSceneManager.SaveScene(scene, CaptureDir + "/ShellCapture.unity");
            AssetDatabase.SaveAssets();
        }
    }
}