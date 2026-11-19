using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Batch-mode verification entry point.
    ///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod SRCharacter.SRCharacterVerify.Run -logFile &lt;log&gt;
    /// Checks the stencil encoding, forces shader compilation, then renders a capsule through the
    /// full pass chain and asserts the feature is what produced the pixels.
    /// </summary>
    public static class SRCharacterVerify
    {
        private static int s_Errors;

        public static void Run()
        {
            Debug.Log("[SRCharacterVerify] begin");
            VerifyStencilEncoding();
            VerifyShaders();
            RunSmokeTest();
            Debug.Log($"[SRCharacterVerify] finished, errors={s_Errors}");
            EditorApplication.Exit(s_Errors == 0 ? 0 : 1);
        }

        private static void Error(string message)
        {
            s_Errors++;
            Debug.LogError("[SRCharacterVerify] " + message);
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
                Debug.Log("[SRCharacterVerify] ok: " + label);
            else
                Error("failed: " + label);
        }

        private static void VerifyStencilEncoding()
        {
            Check(SRCharacterStencil.MakeRef(0, 16) == 0x10, "MakeRef(0,16) == 0x10");
            Check(SRCharacterStencil.MakeRef(0, 31) == 0x1F, "MakeRef(0,31) == 0x1F");
            Check(SRCharacterStencil.MakeRef(3, 32) == 0xE0, "MakeRef(3,32) == 0xE0");
            Check(SRCharacterStencil.MakePackedMaterialId(1, 16) == 0x50, "packed(area=body,id=16) == 0x50");
            Check(SRCharacterStencil.MakePackedMaterialId(2, 31) == 0x9F, "packed(area=face,id=31) == 0x9F");
            Check(SRCharacterStencil.GroupFromRef(0xE0) == 3, "GroupFromRef(0xE0) == 3");
            Check(SRCharacterStencil.EyeReadMask == 0x81, "eye stencil read mask == 0x81");
            Check((SRCharacterStencil.FlagEyeOccluded & SRCharacterStencil.GroupMask) == 0, "bit0 flag does not overlap the group bits");
        }

        private static void VerifyShaders()
        {
            Shader characterShader = Shader.Find("SRCharacter/Character");
            Shader shadingShader = Shader.Find("SRCharacter/Shading");
            Shader transparentShader = Shader.Find(SRCharacterShaderIDs.TransparentShaderName);

            if (characterShader == null)
            {
                Error("Shader.Find(\"SRCharacter/Character\") failed (is the Shaders folder imported?)");
                return;
            }

            if (shadingShader == null)
            {
                Error("Shader.Find(\"SRCharacter/Shading\") failed (is the Shaders folder imported?)");
                return;
            }

            if (transparentShader == null)
            {
                Error("Shader.Find(\"SRCharacter/Transparent\") failed (is the Shaders folder imported?)");
                return;
            }

            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(characterShader), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(shadingShader), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(transparentShader), ImportAssetOptions.ForceUpdate);

            CheckShader(characterShader);
            CheckShader(shadingShader);
            CheckShader(transparentShader);

            SRCharacterFeature feature = ScriptableObject.CreateInstance<SRCharacterFeature>();
            feature.settings.ResolveShaders();
            feature.Create();
            Check(feature.settings.characterShader != null, "feature resolves the character shader");
            Check(feature.settings.shadingShader != null, "feature resolves the shading shader");
            Check(feature.settings.transparentShader != null, "feature resolves the transparent shader");
            Object.DestroyImmediate(feature);
        }

        private static void CheckShader(Shader shader)
        {
            Check(shader.isSupported, $"{shader.name} isSupported");

            // ShaderCompilerMessageSeverity is internal in 2022.3; ShaderHasError is the public probe.
            Check(!ShaderUtil.ShaderHasError(shader), $"{shader.name} has no compile errors");

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            Debug.Log($"[SRCharacterVerify] {shader.name}: {messages.Length} compiler message(s)");

            foreach (ShaderMessage message in messages)
                Debug.LogWarning($"[SRCharacterVerify] {shader.name}:{message.line} {message.message}");
        }

        private static void RunSmokeTest()
        {
            Shader characterShader = Shader.Find("SRCharacter/Character");
            Shader shadingShader = Shader.Find("SRCharacter/Shading");
            if (characterShader == null || shadingShader == null)
            {
                Error("smoke test skipped: character/shading shader not found");
                return;
            }

            UniversalRenderPipelineAsset urpAsset =
                (GraphicsSettings.defaultRenderPipeline ?? QualitySettings.renderPipeline) as UniversalRenderPipelineAsset;
            ScriptableRendererData[] rendererDataList = urpAsset != null ? GetRendererDataList(urpAsset) : null;
            if (rendererDataList == null || rendererDataList.Length == 0 || rendererDataList[0] == null)
            {
                Error("smoke test skipped: no UniversalRendererData in the active URP asset");
                return;
            }

            ScriptableRendererData originalRendererData = rendererDataList[0];
            int previousMsaa = urpAsset.msaaSampleCount;

            RenderTexture rt = null;
            Material material = null;
            Material baselineMaterial = null;
            SRCharacterFeature feature = null;
            ScriptableRendererData rendererData = null;

            try
            {
                // Swap a cloned renderer data (with our feature) into the active URP asset for the
                // duration of the test; the project asset is restored in finally and never saved.
                urpAsset.msaaSampleCount = 1;
                // Create the scene first: entering a new scene unloads unused assets, which would
                // destroy the runtime-instantiated renderer data we are about to inject.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                rendererData = Object.Instantiate(originalRendererData);
                rendererData.name = "SRCharacterVerifyRenderer";
                rendererData.hideFlags = HideFlags.DontSave | HideFlags.DontUnloadUnusedAsset;

                feature = ScriptableObject.CreateInstance<SRCharacterFeature>();
                feature.settings.ResolveShaders();
                feature.Create();
                rendererData.rendererFeatures.Add(feature);
                rendererData.SetDirty();

                rendererDataList[0] = rendererData;
                RebuildRenderers(urpAsset);
                PreparePipeline(urpAsset);
                Debug.Log($"[SRCharacterVerify] swapped renderer data; active renderer features = {GetRendererFeatureCount(urpAsset)}, " +
                          $"currentPipeline={DescribePipeline()}");

                GameObject cameraGo = new GameObject("SRCharacterVerifyCamera");
                Camera camera = cameraGo.AddComponent<Camera>();
                cameraGo.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0.25f, 1f);
                camera.transform.position = new Vector3(0f, 0f, -4f);
                camera.transform.rotation = Quaternion.identity;
                camera.enabled = false;
                UniversalAdditionalCameraData cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderPostProcessing = false;

                GameObject lightGo = new GameObject("SRCharacterVerifyLight");
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1f;
                lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

                GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                material = new Material(characterShader);
                material.SetColor("_BaseColor", new Color(0.9f, 0.7f, 0.7f, 1f));
                material.SetColor("_OutlineColor", Color.black);
                material.SetFloat("_OutlineWidth", 1.5f);
                material.SetFloat("_SRSpecIntensity", 1f);
                material.SetFloat(SRCharacterShaderIDs.StencilRef, SRCharacterStencil.MakeRef(0, 16));
                material.SetFloat(SRCharacterShaderIDs.Area, 1);
                material.SetFloat(SRCharacterShaderIDs.ShadingGroup, 0);
                material.SetFloat(SRCharacterShaderIDs.MaterialID, 16);
                capsule.GetComponent<Renderer>().sharedMaterial = material;
                capsule.AddComponent<SRCharacterRenderer>().Apply();

                // A stock URP lit cube proves the pipeline itself draws.
                baselineMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                baselineMaterial.color = Color.green;
                GameObject baseline = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseline.transform.position = new Vector3(1.4f, 0f, 0f);
                baseline.transform.localScale = Vector3.one * 0.6f;
                baseline.GetComponent<Renderer>().sharedMaterial = baselineMaterial;

                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { name = "SRCharacterVerifyRT" };
                rt.Create();
                camera.targetTexture = rt;

                // The first render request instantiates the pipeline; measure the second one.
                RenderCamera(camera, rt);
                Debug.Log($"[SRCharacterVerify] warm-up: currentPipeline={DescribePipeline()}, " +
                          $"rendererFeatures={rendererData.rendererFeatures.Count}");

                int characterPixels = RenderAndCount(camera, rt, out Color32 background, out int baselinePixels, out Color32 capsuleCenter);
                Debug.Log($"[SRCharacterVerify] pipeline={DescribePipeline()}, background={background}, " +
                          $"baseline green pixels={baselinePixels}, character pixels={characterPixels}, capsule center={capsuleCenter}");

                SaveRenderTexture(rt, "smoke_render.png");

                Check(baselinePixels > 50, "stock URP lit object rendered (pipeline works)");
                Check(characterPixels > 200, "character pixels rendered by the SRCharacter feature");

                // The fullscreen stencil shading must carry the character on its own; the additive
                // overlay only contributes specular/rim on top of it.
                feature.settings.enableOverlay = false;
                int diffuseOnlyPixels = RenderAndCount(camera, rt, out _, out _, out _);
                Debug.Log($"[SRCharacterVerify] character pixels without overlay = {diffuseOnlyPixels}");
                Check(diffuseOnlyPixels > 200, "fullscreen stencil shading renders without the additive overlay");
                feature.settings.enableOverlay = true;

                // Depth correctness: an opaque URP object drawn in the opaque pass must occlude the
                // character, which also proves the feature runs after opaques and depth-tests against
                // the scene depth attachment instead of overdrawing it.
                Material occluderMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                occluderMaterial.color = Color.red;
                GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                occluder.transform.position = new Vector3(0f, 0f, -1.5f);
                occluder.transform.localScale = new Vector3(1.2f, 1.2f, 0.2f);
                occluder.GetComponent<Renderer>().sharedMaterial = occluderMaterial;

                int occludedCharacterPixels = RenderAndCount(camera, rt, out _, out _, out Color32 center);
                Debug.Log($"[SRCharacterVerify] occluded center={center}, character pixels with occluder={occludedCharacterPixels}");
                Check(center.r > 96 && center.g < 96, "occluder wins the depth test at the screen center");
                Check(occludedCharacterPixels < characterPixels, "character pixels are reduced behind an opaque occluder");

                Object.DestroyImmediate(occluder);
                Object.DestroyImmediate(occluderMaterial);

                // --- Semi-transparent bangs (capture P35 eids 5349..5595): the mesh is never in the
                // G-buffer; it alpha-blends over the already shaded character with a stencil guard.
                Material transparentMaterial = new Material(Shader.Find(SRCharacterShaderIDs.TransparentShaderName));
                transparentMaterial.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 1f));
                transparentMaterial.SetFloat(SRCharacterShaderIDs.Alpha, 0.5f);
                transparentMaterial.SetFloat(SRCharacterShaderIDs.StencilRef, SRCharacterStencil.MakeRef(0, 24));
                transparentMaterial.SetFloat(SRCharacterShaderIDs.Area, 1);
                transparentMaterial.SetFloat(SRCharacterShaderIDs.ShadingGroup, 0);
                transparentMaterial.SetFloat(SRCharacterShaderIDs.MaterialID, 24);

                GameObject bangs = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bangs.name = "SRCharacterVerifyBangs";
                bangs.transform.position = new Vector3(0f, 0f, -0.8f);
                bangs.transform.localScale = new Vector3(1.4f, 2.2f, 1f);
                bangs.GetComponent<Renderer>().sharedMaterial = transparentMaterial;

                Color32[] transparentPixels = RenderAndGetPixels(camera, rt);
                Color32 transparentCenter = transparentPixels[(rt.height / 2) * rt.width + rt.width / 2];
                Debug.Log($"[SRCharacterVerify] transparent bangs center={transparentCenter} (capsule center was {capsuleCenter})");
                Check(transparentCenter.r > capsuleCenter.r + 12 && transparentCenter.g < capsuleCenter.g - 12,
                    "semi-transparent bangs alpha-blend over the shaded character");
                SaveRenderTexture(rt, "smoke_transparent.png");

                Object.DestroyImmediate(bangs);
                Object.DestroyImmediate(transparentMaterial);

                // --- Eye overlay (capture eids 4968/4989): the proxy is redrawn against the G-buffer
                // depth; where it is covered (the green bangs bar) the ZFail marks stencil bit 0 and
                // the additive highlight is skipped. Visible eye pixels must get brighter.
                Material eyeMaterial = new Material(characterShader);
                eyeMaterial.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.35f, 1f));
                eyeMaterial.SetColor("_SREyeColor", Color.white);
                eyeMaterial.SetFloat("_SREyeSpecIntensity", 1f);
                eyeMaterial.SetFloat("_SREyeShininess", 8f);
                eyeMaterial.SetFloat("_SREyeRimIntensity", 1f);

                GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "SRCharacterVerifyEyeProxy";
                eye.transform.position = new Vector3(0f, 0f, -0.62f);
                eye.transform.localScale = Vector3.one * 0.4f;
                eye.GetComponent<Renderer>().sharedMaterial = eyeMaterial;
                SRCharacterRenderer eyeSettings = eye.AddComponent<SRCharacterRenderer>();
                eyeSettings.area = SRCharacterRenderer.Area.Face;
                eyeSettings.shadingGroup = 2;
                eyeSettings.materialId = 16;
                eyeSettings.eyeProxy = true;
                eyeSettings.Apply();

                Material eyeOccluderMaterial = new Material(characterShader);
                eyeOccluderMaterial.SetColor("_BaseColor", new Color(0.2f, 0.6f, 0.2f, 1f));
                GameObject eyeOccluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                eyeOccluder.name = "SRCharacterVerifyBangsShadow";
                eyeOccluder.transform.position = new Vector3(-0.11f, 0f, -0.92f);
                eyeOccluder.transform.localScale = new Vector3(0.18f, 0.4f, 0.02f);
                eyeOccluder.GetComponent<Renderer>().sharedMaterial = eyeOccluderMaterial;
                SRCharacterRenderer eyeOccluderSettings = eyeOccluder.AddComponent<SRCharacterRenderer>();
                eyeOccluderSettings.area = SRCharacterRenderer.Area.Face;
                eyeOccluderSettings.shadingGroup = 2;
                eyeOccluderSettings.materialId = 17;
                eyeOccluderSettings.Apply();

                feature.settings.enableEye = false;
                Color32[] eyeOff = RenderAndGetPixels(camera, rt);
                feature.settings.enableEye = true;
                Color32[] eyeOn = RenderAndGetPixels(camera, rt);

                int visibleIndex = (rt.height / 2) * rt.width + rt.width / 2 + 8;
                int coveredIndex = (rt.height / 2) * rt.width + rt.width / 2 - 8;
                int visibleDelta = Brightness(eyeOn[visibleIndex]) - Brightness(eyeOff[visibleIndex]);
                int coveredDelta = Brightness(eyeOn[coveredIndex]) - Brightness(eyeOff[coveredIndex]);
                Debug.Log($"[SRCharacterVerify] eye overlay: visible delta={visibleDelta} " +
                          $"(off={eyeOff[visibleIndex]}, on={eyeOn[visibleIndex]}), " +
                          $"covered delta={coveredDelta} (off={eyeOff[coveredIndex]}, on={eyeOn[coveredIndex]})");
                Check(visibleDelta > 8, "eye overlay adds a highlight on visible eye pixels");
                Check(coveredDelta <= 3 && coveredDelta >= -3, "eye overlay is skipped where stencil bit0 marks bangs occlusion");
                SaveRenderTexture(rt, "smoke_eye.png");

                Object.DestroyImmediate(eyeOccluder);
                Object.DestroyImmediate(eyeOccluderMaterial);
                Object.DestroyImmediate(eye);
                Object.DestroyImmediate(eyeMaterial);

                feature.SetActive(false);
                int disabledPixels = RenderAndCount(camera, rt, out _, out _, out _);
                Debug.Log($"[SRCharacterVerify] character pixels with feature disabled = {disabledPixels}");
                Check(disabledPixels == 0, "no character pixels without the feature");
                feature.SetActive(true);
            }
            catch (System.Exception exception)
            {
                Error("smoke test threw: " + exception);
            }
            finally
            {
                rendererDataList[0] = originalRendererData;
                urpAsset.msaaSampleCount = previousMsaa;
                RebuildRenderers(urpAsset);

                if (rt != null)
                    Object.DestroyImmediate(rt);
                if (material != null)
                    Object.DestroyImmediate(material);
                if (baselineMaterial != null)
                    Object.DestroyImmediate(baselineMaterial);
                if (rendererData != null)
                    Object.DestroyImmediate(rendererData);
                if (feature != null)
                    Object.DestroyImmediate(feature);
            }
        }

        // Camera.Render() bypasses SRP; render requests are the supported SRP path.
        private static void RenderCamera(Camera camera, RenderTexture rt)
        {
            System.Type requestType = typeof(RenderPipeline).GetNestedType("StandardRequest", BindingFlags.Public);
            object request = requestType != null ? System.Activator.CreateInstance(requestType) : null;

            FieldInfo destination = requestType?.GetField("destination", BindingFlags.Instance | BindingFlags.Public);
            destination?.SetValue(request, rt);

            MethodInfo submit = typeof(RenderPipeline)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "SubmitRenderRequest" && m.GetParameters().Length == 2);

            if (submit != null && request != null)
            {
                if (submit.IsGenericMethod)
                    submit.MakeGenericMethod(requestType).Invoke(null, new[] { (object)camera, request });
                else
                    submit.Invoke(null, new[] { (object)camera, request });
                return;
            }

            camera.Render();
        }

        private static string DescribePipeline()
        {
            RenderPipeline pipeline = RenderPipelineManager.currentPipeline;
            return pipeline != null ? pipeline.GetType().Name : "null";
        }

        private static ScriptableRendererData[] GetRendererDataList(UniversalRenderPipelineAsset urpAsset)
        {
            FieldInfo field = typeof(UniversalRenderPipelineAsset).GetField(
                "m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(urpAsset) as ScriptableRendererData[];
        }

        // SRP instances are created lazily by the render loop; batch mode needs it created explicitly.
        private static void PreparePipeline(RenderPipelineAsset asset)
        {
            try
            {
                typeof(RenderPipelineManager)
                    .GetMethod("PrepareRenderPipeline", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, new object[] { asset });
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[SRCharacterVerify] PrepareRenderPipeline failed: " + exception.Message);
            }
        }

        // The asset caches renderer instances, so drop them after swapping the renderer data.
        private static void RebuildRenderers(UniversalRenderPipelineAsset urpAsset)
        {
            try
            {
                typeof(UniversalRenderPipelineAsset)
                    .GetMethod("DestroyRenderers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(urpAsset, null);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[SRCharacterVerify] DestroyRenderers failed: " + exception.Message);
            }
        }

        private static int GetRendererFeatureCount(UniversalRenderPipelineAsset urpAsset)
        {
            ScriptableRenderer renderer = urpAsset.scriptableRenderer;
            PropertyInfo property = typeof(ScriptableRenderer).GetProperty(
                "rendererFeatures", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(renderer) is System.Collections.IList features ? features.Count : -1;
        }

        private static void SaveRenderTexture(RenderTexture rt, string fileName)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            string directory = System.IO.Path.Combine(Application.dataPath, "_SRCharacterVerify", "validation");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, fileName), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            Debug.Log("[SRCharacterVerify] wrote " + fileName);
        }

        private static Color32[] RenderAndGetPixels(Camera camera, RenderTexture rt)
        {
            RenderCamera(camera, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }

        private static int RenderAndCount(Camera camera, RenderTexture rt, out Color32 background, out int baselinePixels, out Color32 center)
        {
            Color32[] pixels = RenderAndGetPixels(camera, rt);
            background = pixels[0];
            center = pixels[(rt.height / 2) * rt.width + rt.width / 2];

            baselinePixels = pixels.Count(p => p.g > 96 && p.r < 64 && p.b < 64);
            return pixels.Count(p => p.r > 96 && p.g > 96 && p.b > 96);
        }

        private static int Brightness(Color32 pixel)
        {
            return pixel.r + pixel.g + pixel.b;
        }
    }
}