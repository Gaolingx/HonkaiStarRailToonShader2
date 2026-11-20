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
                SRCharacterRenderer capsuleSettings = capsule.AddComponent<SRCharacterRenderer>();
                capsuleSettings.Apply();

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

                // --- Semi-transparent ornament shell (capture P35 eids 5349/5361/5404/5585/5595).
                // Five global layers draw the same mesh in capture order. The bubble sits in front of
                // the capsule: the flat layer's NOT_EQUAL(ref 0) guard needs a nonzero stencil value
                // behind it, which is exactly how the capture uses it (shell over the character).
                Material shellMaterial = new Material(Shader.Find(SRCharacterShaderIDs.TransparentShaderName));
                shellMaterial.SetFloat(SRCharacterShaderIDs.Alpha, 1f);
                shellMaterial.SetFloat(SRCharacterShaderIDs.TransparentHeightLine, 1000f); // stay below the fade line
                shellMaterial.SetFloat("_OutlineWidth", 12f);                              // widen the silhouette band

                GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shell.name = "SRCharacterVerifyShell";
                shell.transform.position = new Vector3(0f, 0f, -0.9f);
                shell.transform.localScale = Vector3.one * 0.5f;
                shell.GetComponent<Renderer>().sharedMaterial = shellMaterial;

                int shellCenter = (rt.height / 2) * rt.width + rt.width / 2;
                int shellEdge = shellCenter + 17;   // grazing band inside the silhouette

                Color32[] shellAll = RenderAndGetPixels(camera, rt);
                SaveRenderTexture(rt, "smoke_transparent.png");
                Color32 shellCenterPixel = shellAll[shellCenter];
                Debug.Log($"[SRCharacterVerify] transparent shell center={shellCenterPixel} (capsule center was {capsuleCenter})");
                Check(Distance(shellCenterPixel, capsuleCenter) > 12,
                    "transparent shell composites over the shaded character");

                // Ablation: each capture layer must leave its own signature.
                feature.settings.enableTransparentFlat = false;
                Color32[] shellNoFlat = RenderAndGetPixels(camera, rt);
                feature.settings.enableTransparentFlat = true;
                // The flat layer is palette * 1.59 * (0.46, 0.67, 1.0): it shifts the composite
                // towards blue (its brightness is then consumed by the near-opaque main layer).
                int flatBlueDelta = shellAll[shellCenter].b - shellNoFlat[shellCenter].b;
                int flatRedDelta = shellNoFlat[shellCenter].r - shellAll[shellCenter].r;
                Debug.Log($"[SRCharacterVerify] flat layer ablation: blue delta={flatBlueDelta}, red delta={flatRedDelta} " +
                          $"(all={shellAll[shellCenter]}, off={shellNoFlat[shellCenter]})");
                Check(flatBlueDelta > 15, "flat layer (5349) tints the shell centre blue");

                feature.settings.enableTransparentShade = false;
                Color32[] shellNoShade = RenderAndGetPixels(camera, rt);
                feature.settings.enableTransparentShade = true;
                int shadeDelta = Brightness(shellNoShade[shellCenter]) - Brightness(shellAll[shellCenter]);
                Debug.Log($"[SRCharacterVerify] main layer ablation: center brightness delta={shadeDelta} (off={shellNoShade[shellCenter]})");
                Check(shadeDelta > 30, "main layer (5361) darkens the shell centre below the fade line");

                feature.settings.enableTransparentRim = false;
                Color32[] shellNoRim = RenderAndGetPixels(camera, rt);
                feature.settings.enableTransparentRim = true;
                int rimEdgeDelta = Brightness(shellAll[shellEdge]) - Brightness(shellNoRim[shellEdge]);
                int rimCenterDelta = Brightness(shellAll[shellCenter]) - Brightness(shellNoRim[shellCenter]);
                Debug.Log($"[SRCharacterVerify] fresnel layer ablation: edge delta={rimEdgeDelta}, centre delta={rimCenterDelta} " +
                          $"(edge={shellAll[shellEdge]}, off={shellNoRim[shellEdge]})");
                Check(rimEdgeDelta > 8, "fresnel layer (5585) lights the grazing silhouette");
                Check(rimCenterDelta >= -3 && rimCenterDelta <= 3, "fresnel layer fades out when facing the camera");

                // The depth-only layer must not change a single colour pixel (ColorMask 0).
                feature.settings.enableTransparentDepthShell = false;
                Color32[] shellNoDepth = RenderAndGetPixels(camera, rt);
                feature.settings.enableTransparentDepthShell = true;
                int depthDelta = Distance(shellAll[shellCenter], shellNoDepth[shellCenter]);
                Debug.Log($"[SRCharacterVerify] depth shell ablation: max channel delta={depthDelta}");
                Check(depthDelta <= 2, "depth shell (5404) writes depth only");

                // The outline layer's colour writes are masked in the capture; the band left by the
                // fresnel layer's stencil ref is what a visible outline would occupy.
                feature.settings.enableTransparentOutline = false;
                Color32[] shellNoOutline = RenderAndGetPixels(camera, rt);
                feature.settings.enableTransparentOutline = true;
                int outlineDefaultDelta = Distance(shellAll[shellCenter], shellNoOutline[shellCenter]);
                int outlineBand = shellCenter + 22;
                int outlineBandDefaultDelta = Distance(shellAll[outlineBand], shellNoOutline[outlineBand]);
                Debug.Log($"[SRCharacterVerify] outline layer masked: centre delta={outlineDefaultDelta}, band delta={outlineBandDefaultDelta}");
                Check(outlineDefaultDelta <= 2 && outlineBandDefaultDelta <= 2,
                    "outline layer (5595) writes no colour by default (capture mask)");

                shellMaterial.SetFloat(SRCharacterShaderIDs.TransparentOutlineVisible, 1f);
                Color32[] shellOutlineVisible = RenderAndGetPixels(camera, rt);
                shellMaterial.SetFloat(SRCharacterShaderIDs.TransparentOutlineVisible, 0f);
                int outlineVisibleDelta = Brightness(shellNoOutline[outlineBand]) - Brightness(shellOutlineVisible[outlineBand]);
                Debug.Log($"[SRCharacterVerify] outline colour toggle: band brightness drop={outlineVisibleDelta} " +
                          $"(masked={shellNoOutline[outlineBand]}, visible={shellOutlineVisible[outlineBand]})");
                Check(outlineVisibleDelta > 30, "outline colour toggle paints the silhouette band");

                // Per-layer stencil dedup: a duplicate overlapping shell must not composite twice.
                GameObject shellTwin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shellTwin.name = "SRCharacterVerifyShellTwin";
                shellTwin.transform.position = shell.transform.position;
                shellTwin.transform.localScale = shell.transform.localScale;
                shellTwin.GetComponent<Renderer>().sharedMaterial = shellMaterial;

                Color32[] shellTwice = RenderAndGetPixels(camera, rt);
                int dedupCenterDelta = Distance(shellAll[shellCenter], shellTwice[shellCenter]);
                Debug.Log($"[SRCharacterVerify] duplicate shell: centre delta={dedupCenterDelta} " +
                          $"(single={shellAll[shellCenter]}, twice={shellTwice[shellCenter]})");
                Check(dedupCenterDelta <= 2, "per-layer stencil guard dedupes overlapping shells");

                Object.DestroyImmediate(shellTwin);
                Object.DestroyImmediate(shell);
                Object.DestroyImmediate(shellMaterial);

                // --- Front hair shadow (reference-based, not in capture frame 13063): a small
                // bangs quad is rasterized into the hair depth prepass; the face shading samples
                // it with an offset along the light direction. With a +X key light the shadow must
                // appear to the right of the caster, and a larger _SRHairShadowDistance must push
                // the darkened region further right.
                // The shell colour layers are switched off here: their near-opaque main layer would
                // sit on top of the shadow region and hide the measured darkening.
                feature.settings.enableTransparentFlat = false;
                feature.settings.enableTransparentShade = false;
                feature.settings.enableTransparentDepthShell = false;
                feature.settings.enableTransparentRim = false;
                feature.settings.enableTransparentOutline = false;

                Quaternion previousLightRotation = light.transform.rotation;
                light.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

                // The face SDF is lit on the capsule, so the toon term only shows a difference
                // when the ramp is not white. A uniform dark ramp makes `lit` visible.
                Texture2D testRamp = new Texture2D(256, 16, TextureFormat.RGBA32, false, true)
                {
                    name = "SRCharacterVerifyRamp"
                };
                Color[] rampPixels = new Color[256 * 16];
                for (int i = 0; i < rampPixels.Length; i++)
                    rampPixels[i] = new Color(0.2f, 0.2f, 0.22f, 1f);
                testRamp.SetPixels(rampPixels);
                testRamp.Apply();
                feature.settings.rampCool = testRamp;
                feature.settings.rampWarm = testRamp;
                feature.settings.hairRampCool = testRamp;
                feature.settings.hairRampWarm = testRamp;

                capsuleSettings.area = SRCharacterRenderer.Area.Face;
                capsuleSettings.Apply();

                Material hairCasterMaterial = new Material(Shader.Find(SRCharacterShaderIDs.TransparentShaderName));
                hairCasterMaterial.SetFloat(SRCharacterShaderIDs.Alpha, 0.5f);
                hairCasterMaterial.SetFloat(SRCharacterShaderIDs.Area, 3);

                GameObject hairCaster = GameObject.CreatePrimitive(PrimitiveType.Quad);
                hairCaster.name = "SRCharacterVerifyHairCaster";
                hairCaster.transform.position = new Vector3(-0.3f, 0.3f, -1.2f);
                hairCaster.transform.localScale = new Vector3(0.36f, 0.5f, 1f);
                hairCaster.GetComponent<Renderer>().sharedMaterial = hairCasterMaterial;

                Vector3 casterScreen = camera.WorldToScreenPoint(hairCaster.transform.position);

                feature.settings.enableFrontHairShadow = false;
                Color32[] hairShadowOff = RenderAndGetPixels(camera, rt);

                feature.settings.enableFrontHairShadow = true;
                feature.settings.hairShadowDistance = 3f;
                Color32[] hairShadowNear = RenderAndGetPixels(camera, rt);

                feature.settings.hairShadowDistance = 6f;
                Color32[] hairShadowFar = RenderAndGetPixels(camera, rt);

                MeasureDarkening(hairShadowOff, hairShadowNear, rt.width, 5,
                    out int nearCount, out float nearCentroidX, out float nearMaxX);
                MeasureDarkening(hairShadowOff, hairShadowFar, rt.width, 5,
                    out int farCount, out float farCentroidX, out float farMaxX);
                Debug.Log($"[SRCharacterVerify] front hair shadow: caster.x={casterScreen.x:F1}, " +
                          $"near(d=3) count={nearCount} centroidX={nearCentroidX:F1} maxX={nearMaxX:F0}, " +
                          $"far(d=6) count={farCount} centroidX={farCentroidX:F1} maxX={farMaxX:F0}");
                Check(nearCount > 20, "front hair shadow darkens face pixels");
                Check(nearCentroidX > casterScreen.x + 1.5f && nearMaxX > casterScreen.x + 2f,
                    "front hair shadow lands on the side opposite the light");
                Check(farCount > 20 && farCentroidX > nearCentroidX + 1f && farMaxX > nearMaxX + 1f,
                    "a larger hair shadow distance pushes the shadow further from the caster");

                SaveRenderTexture(rt, "smoke_hairshadow.png");

                feature.settings.hairShadowDistance = 0.2f;
                feature.settings.rampCool = null;
                feature.settings.rampWarm = null;
                feature.settings.hairRampCool = null;
                feature.settings.hairRampWarm = null;
                feature.settings.enableTransparentFlat = true;
                feature.settings.enableTransparentShade = true;
                feature.settings.enableTransparentDepthShell = true;
                feature.settings.enableTransparentRim = true;
                feature.settings.enableTransparentOutline = true;
                capsuleSettings.area = SRCharacterRenderer.Area.Body;
                capsuleSettings.Apply();
                light.transform.rotation = previousLightRotation;
                Object.DestroyImmediate(hairCaster);
                Object.DestroyImmediate(hairCasterMaterial);
                Object.DestroyImmediate(testRamp);

                // --- Eye overlay (capture eids 4968/4989): the proxy is redrawn against the G-buffer
                // depth; where it is covered (the green bangs bar) the ZFail marks stencil bit 0 and
                // the additive highlight is skipped. Visible eye pixels must get brighter.
                // PS 9584 constants decoded from frame 13063 / eid 5015 (see README 2.5):
                // iris mask 0.624, base tint 0.04, spec radius 1, distance fade 5.
                Material eyeMaterial = new Material(characterShader);
                eyeMaterial.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.35f, 1f));
                eyeMaterial.SetColor("_SREyeColor", Color.white);
                eyeMaterial.SetFloat("_SREyeSpecIntensity", 1f);
                eyeMaterial.SetFloat("_SREyeIrisMask", 0.624f);
                eyeMaterial.SetFloat("_SREyeIrisBase", 0.04f);
                eyeMaterial.SetFloat("_SREyeSpecRadius", 1f);
                eyeMaterial.SetFloat("_SREyeFadeDistance", 5f);

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

        private static int Distance(Color32 a, Color32 b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
        }

        // Pixel array comes from GetPixels32 (row 0 = bottom); centroid X shares that space
        // with Camera.WorldToScreenPoint, so the comparison is direct.
        private static void MeasureDarkening(Color32[] reference, Color32[] shaded, int width, int threshold,
            out int count, out float centroidX, out float maxX)
        {
            count = 0;
            maxX = 0f;
            double sumX = 0;
            for (int i = 0; i < reference.Length; i++)
            {
                if (Brightness(reference[i]) - Brightness(shaded[i]) <= threshold)
                    continue;

                float x = i % width;
                count++;
                sumX += x;
                if (x > maxX)
                    maxX = x;
            }

            centroidX = count > 0 ? (float)(sumX / count) : 0f;
        }
    }
}