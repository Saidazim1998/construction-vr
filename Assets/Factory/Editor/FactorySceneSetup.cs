using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using URP = UnityEngine.Rendering.Universal;

namespace NBM500U.EditorTools
{
    /// <summary>
    /// Builds the second scene of the trainer, the train repair hall where the welding is done:
    /// hall shell, gondola wagon on the track, welding stand, NBM-500U machine, CO2 cylinder, the
    /// player rig, baked lights, light probes and a reflection probe. Everything is rebuilt from the
    /// models, so run it again after changing a model.
    /// </summary>
    public static class FactorySceneSetup
    {
        public const string HallFbx = "Assets/Factory/Models/FactoryHall.fbx";
        public const string StandFbx = "Assets/Factory/Models/WeldingStand.fbx";
        public const string GondolaFbx = "Assets/Factory/Models/Gondola.fbx";
        public const string PropsFbx = "Assets/Factory/Models/FactoryProps.fbx";
        public const string BogieFbx = "Assets/Factory/Models/Bogie.fbx";             // spare bogie, reduced
        public const string GondolaLodFbx = "Assets/Factory/Models/GondolaLOD.fbx";   // far wagon, reduced
        // Asset folders the shared helpers below read from and write to. The factory is the default; the
        // other scene builders point them at their own folder while they run (see UseAssetRoot).
        private static string s_assetRoot = "Assets/Factory";
        public static string MaterialFolder => s_assetRoot + "/Materials";
        public static string TextureFolder => s_assetRoot + "/Textures";
        internal static void UseAssetRoot(string root) => s_assetRoot = root;
        public const string ScenePath = "Assets/Scenes/Factory.unity";
        public const string LightingSettingsPath = "Assets/Factory/FactoryLighting.lighting";

        // Underside of the light fixtures and rail top, metres above the floor (see build_hall.py).
        private const float FixtureHeight = 10.70f;
        private const float RailTop = 0.024f;

        private const float FixtureIntensity = 100f;

        // Exposure correction (stops) applied after tonemapping.
        private const float PostExposure = 0.2f;

        // Part of a contact-shadow quad that the object's footprint covers (see make_textures.py).
        private const float ContactShadowCore = 0.62f;
        private static readonly Color FixtureColor = new Color(1f, 0.97f, 0.9f);

        internal const StaticEditorFlags StaticFlags =
            StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;

        [MenuItem("Tools/Factory/Build Factory Scene")]
        private static void BuildSceneMenu() => Debug.Log(BuildScene());

        [MenuItem("Tools/Factory/Bake Lighting (final quality)")]
        private static void BakeMenu() => Debug.Log(StartBake(false));

        // ------------------------------------------------------------------- scene

        public static string BuildScene()
        {
            var log = new StringBuilder("[Factory] Build factory scene\n");
            UseAssetRoot("Assets/Factory");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException(
                        "An open scene has unsaved changes. Save or discard them, then run this again.");
                }
            }

            ConfigureModel(HallFbx, log);
            ConfigureModel(StandFbx, log);
            ConfigureModel(GondolaFbx, log);
            ConfigureModel(PropsFbx, log);
            ConfigureModel(BogieFbx, log);
            ConfigureModel(GondolaLodFbx, log);
            ConfigureTextures();
            ConfigureMaterials(log);

            var machinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NBM500UVRSetup.PrefabPath);
            var cylinderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GasCylinderVRSetup.PrefabPath);
            if (machinePrefab == null || cylinderPrefab == null)
            {
                throw new InvalidOperationException(
                    "NBM-500U or CO2 cylinder prefab is missing. Build them first (Tools/NBM500U).");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- hall shell
            GameObject hall = Instantiate(HallFbx, scene);
            foreach (MeshRenderer r in hall.GetComponentsInChildren<MeshRenderer>())
            {
                string n = r.name;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticFlags);
                bool flat = n.StartsWith("Hall_FloorLines") || n == "Hall_Windows" || n == "Hall_Lights";
                r.shadowCastingMode = flat ? ShadowCastingMode.Off : ShadowCastingMode.On;
                switch (n)
                {
                    case "Hall_Floor": Lightmapped(r, 1.7f); break;      // sharper contact shadows under the equipment
                    case "Hall_Walls": Lightmapped(r, 0.5f); break;
                    case "Hall_WallDado": Lightmapped(r, 0.5f); break;
                    case "Hall_Columns": Lightmapped(r, 0.7f); break;
                    case "Hall_ColumnBases": Lightmapped(r, 0.7f); break;
                    case "Hall_RoofDeck": Lightmapped(r, 0.25f); break;
                    case "Hall_CraneRunway": Lightmapped(r, 0.3f); break;
                    case "Crane_Bridge": Lightmapped(r, 0.5f); break;
                    default: r.receiveGI = ReceiveGI.LightProbes; break;
                }
            }

            Bounds floor = WorldBounds(Find(hall, "Hall_Floor"));
            Bounds track = WorldBounds(Find(hall, "Hall_Track"));
            bool lengthIsZ = floor.size.z >= floor.size.x;
            Vector3 along = lengthIsZ ? Vector3.forward : Vector3.right;
            Vector3 across = lengthIsZ ? Vector3.right : Vector3.forward;
            Vector3 centre = new Vector3(floor.center.x, 0f, floor.center.z);
            float trackOffset = Vector3.Dot(track.center - centre, across);
            Vector3 towardsTrack = across * Mathf.Sign(trackOffset);
            log.AppendLine($"  hall {floor.size.x:0.0} x {floor.size.z:0.0} m, track {trackOffset:0.00} m off the axis");

            var floorCollider = Find(hall, "Hall_Floor").gameObject.AddComponent<BoxCollider>();
            Bounds localFloor = Find(hall, "Hall_Floor").GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 thin = Find(hall, "Hall_Floor").InverseTransformVector(Vector3.up * 0.2f);
            floorCollider.center = localFloor.center - thin * 0.5f;
            floorCollider.size = localFloor.size + new Vector3(Mathf.Abs(thin.x), Mathf.Abs(thin.y), Mathf.Abs(thin.z));

            // ---- gondola wagon standing on the track
            GameObject gondola = Instantiate(GondolaFbx, scene);
            gondola.transform.position = centre + across * trackOffset + along * 8f + Vector3.up * RailTop;
            foreach (MeshRenderer r in gondola.GetComponentsInChildren<MeshRenderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticFlags);
                if (r.name == "Gondola_Body") Lightmapped(r, 0.4f); else r.receiveGI = ReceiveGI.LightProbes;
            }

            // A second wagon further down the track and two spare bogies standing on the shop floor.
            GameObject gondola2 = Instantiate(GondolaLodFbx, scene);
            gondola2.name = "Gondola (2)";
            gondola2.transform.position = centre + across * trackOffset - along * 14f + Vector3.up * RailTop;
            ProbeLitStatic(gondola2);
            for (int i = 0; i < 2; i++)
            {
                GameObject bogie = Instantiate(BogieFbx, scene);
                bogie.name = $"Bogie ({i + 1})";
                bogie.transform.SetPositionAndRotation(
                    centre - towardsTrack * 6.5f - along * (13f + 3.6f * i), Quaternion.Euler(0f, i == 0 ? 4f : -3f, 0f));
                ProbeLitStatic(bogie);
            }

            // ---- workshop furniture, pipes, ducts and signs (authored in hall coordinates)
            GameObject props = Instantiate(PropsFbx, scene);
            ProbeLitStatic(props);
            Find(props, "Prop_Signs").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---- work cell: welding stand, machine, cylinder, player
            Vector3 cell = centre - towardsTrack * 2f;
            GameObject stand = Instantiate(StandFbx, scene);
            stand.transform.position = cell;
            foreach (MeshRenderer r in stand.GetComponentsInChildren<MeshRenderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticFlags);
                if (r.name == "Stand_Frame") Lightmapped(r, 1f); else r.receiveGI = ReceiveGI.LightProbes;
            }

            // The player stands beside the stand, the machine one metre ahead with its panel towards them.
            Quaternion facing = Quaternion.LookRotation(along);
            Vector3 player = cell - towardsTrack * 2.3f + along * 0.2f;
            Vector3 machinePosition = player + along * 1.0f;

            var machine = (GameObject)PrefabUtility.InstantiatePrefab(machinePrefab, scene);
            machine.transform.SetPositionAndRotation(machinePosition, facing);
            var cylinder = (GameObject)PrefabUtility.InstantiatePrefab(cylinderPrefab, scene);
            cylinder.transform.SetPositionAndRotation(machinePosition + facing * GasCylinderVRSetup.ScenePosition, facing);
            int casters = BakedShadowCasters(machine) + BakedShadowCasters(cylinder);
            log.AppendLine($"  {casters} fixed parts of the machine and the cylinder cast baked shadows");
            GameObject rig = NBM500UVRSetup.AddPlayerRig(scene, player, facing, log);
            ConstructionVR.EditorTools.PlayerBodySetup.AddToRig(rig, log);
            log.AppendLine($"  stand at {cell}, machine at {machinePosition}, player at {player}");

            // ---- soft contact shadows. Under 36 soft lights the baked shadows are almost invisible, so
            // the equipment gets a dark, soft-edged quad on the floor (cheap, no realtime shadows needed).
            var shadows = new GameObject("Contact Shadows");
            Material strong = ContactShadowMaterial("M_Hall_ContactShadow", 0.62f);
            Material soft = ContactShadowMaterial("M_Hall_ContactShadowSoft", 0.36f);
            int shadowCount = 0;
            void Shadow(string label, Vector3 position, float sizeX, float sizeZ, Material material)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.name = "Shadow_" + label;
                quad.transform.SetParent(shadows.transform, false);
                quad.transform.SetPositionAndRotation(
                    new Vector3(position.x, 0.009f, position.z), Quaternion.Euler(90f, 0f, 0f));
                quad.transform.localScale = new Vector3(sizeX / ContactShadowCore, sizeZ / ContactShadowCore, 1f);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(quad, StaticEditorFlags.BatchingStatic);
                shadowCount++;
            }

            void ShadowUnder(string label, Bounds b, Material material) =>
                Shadow(label, b.center, b.size.x, b.size.z, material);

            // footprint given in hall coordinates: metres across the hall / along it from the centre
            void ShadowAt(string label, float acrossOffset, float alongOffset, float sizeAcross, float sizeAlong, Material material)
            {
                Vector3 p = centre + across * acrossOffset * Mathf.Sign(trackOffset) + along * alongOffset;
                Shadow(label, p, lengthIsZ ? sizeAcross : sizeAlong, lengthIsZ ? sizeAlong : sizeAcross, material);
            }

            Bounds machineBase = WorldBounds(Find(machine, "PS_Housing"));
            machineBase.Encapsulate(WorldBounds(Find(machine, "Cooler_Housing")));
            machineBase.Encapsulate(WorldBounds(Find(machine, "WF_Chassis")));
            ShadowUnder("Machine", machineBase, strong);
            ShadowUnder("Cylinder", WorldBounds(Find(cylinder, "Gas_Cylinder_Body")), strong);
            ShadowUnder("Stand", WorldBounds(stand.transform), soft);
            ShadowUnder("Gondola", WorldBounds(gondola.transform), soft);
            ShadowUnder("Gondola2", WorldBounds(gondola2.transform), soft);
            ShadowAt("ToolCabinet", -5.6f, 1.75f, 0.70f, 0.46f, strong);
            ShadowAt("Workbench", -2.0f, -3.9f, 2.0f, 0.8f, soft);
            ShadowAt("FumeExtractor", -0.2f, -2.4f, 0.6f, 0.7f, strong);
            ShadowAt("ScrapBin", -7.6f, -4.6f, 1.4f, 0.9f, strong);
            ShadowAt("Pallet1", 1.6f, -3.6f, 1.2f, 0.8f, strong);
            ShadowAt("Pallet2", 1.8f, 4.0f, 1.25f, 2.4f, strong);
            ShadowAt("Shelving", -11.6f, -3.0f, 0.6f, 4.0f, soft);
            ShadowAt("CylinderCage", -11.55f, 3.0f, 0.6f, 1.5f, soft);
            ShadowAt("SandBox", -11.53f, 7.3f, 0.8f, 0.6f, strong);
            ShadowAt("Bogie1", -6.5f, -13.0f, 2.5f, 2.6f, soft);
            ShadowAt("Bogie2", -6.5f, -16.6f, 2.5f, 2.6f, soft);
            log.AppendLine($"  {shadowCount} contact shadows");

            // ---- lighting
            var lighting = new GameObject("Lighting");

            // The roof steel, crane trolley and fixtures are lit by light probes. Their own centres lie
            // above the probe grid, so they sample the probes from a point inside it instead.
            var roofAnchor = new GameObject("Probe Anchor (roof)");
            roofAnchor.transform.SetParent(lighting.transform, false);
            roofAnchor.transform.position = centre + Vector3.up * 8.5f;
            foreach (MeshRenderer r in hall.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.receiveGI == ReceiveGI.LightProbes && r.bounds.center.y > 6f)
                {
                    r.probeAnchor = roofAnchor.transform;
                }
            }

            int fixtures = 0;
            float[] rows = { -8f, -2.7f, 2.7f, 8f };
            Quaternion down = Quaternion.LookRotation(Vector3.down, along);
            for (int i = 0; i < 9; i++)
            {
                foreach (float row in rows)
                {
                    var go = new GameObject($"Fixture_{i}_{fixtures % rows.Length}");
                    go.transform.SetParent(lighting.transform, false);
                    go.transform.SetPositionAndRotation(
                        centre + across * row + along * (-24f + 6f * i) + Vector3.up * FixtureHeight, down);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Rectangle;
                    light.areaSize = new Vector2(0.28f, 1.5f);
                    light.color = FixtureColor;
                    light.intensity = FixtureIntensity;
                    light.range = 40f;
                    light.lightmapBakeType = LightmapBakeType.Baked;
                    light.shadows = LightShadows.Soft;
                    fixtures++;
                }
            }

            var probeObject = new GameObject("Light Probes");
            probeObject.transform.SetParent(lighting.transform, false);
            var positions = new List<Vector3>();
            float[] heights = { 0.4f, 1.8f, 4.5f, 9.5f };
            for (float a = -11f; a <= 11.01f; a += 5.5f)
                for (float l = -26f; l <= 26.01f; l += 6.5f)
                    foreach (float h in heights)
                        positions.Add(centre + across * a + along * l + Vector3.up * h);
            float[] cellHeights = { 0.3f, 1.1f, 1.9f };
            for (float a = -3.75f; a <= 3.76f; a += 1.5f)
                for (float l = -3.75f; l <= 3.76f; l += 1.5f)
                    foreach (float h in cellHeights)
                        positions.Add(cell - towardsTrack * 1.2f + across * a + along * l + Vector3.up * h);
            probeObject.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();

            var reflectionObject = new GameObject("Reflection Probe");
            reflectionObject.transform.SetParent(lighting.transform, false);
            reflectionObject.transform.position = cell + Vector3.up * 1.7f;
            var reflection = reflectionObject.AddComponent<ReflectionProbe>();
            reflection.mode = ReflectionProbeMode.Baked;
            reflection.resolution = 256;
            reflection.boxProjection = true;
            // The box covers the whole hall (13.5 m high), whatever the probe's own position is.
            reflection.size = new Vector3(floor.size.x, 13.5f, floor.size.z);
            reflection.center = new Vector3(centre.x - cell.x, 6.75f - 1.7f, centre.z - cell.z);

            // Indoors: no sky. Everything the player sees is lit by the bake.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.03f, 0.03f, 0.035f);
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            log.AppendLine($"  {fixtures} baked area lights, {positions.Count} light probes, 1 reflection probe");

            ConfigureQuestQuality(log);
            AddPostProcessing(rig, log);

            Lightmapping.lightingSettings = LoadLightingSettings(true);
            log.Append(ConstructionVR.EditorTools.TrainingSceneSetup.InstallInOpenScene());       // scenario logic and interface
            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine("  saved " + ScenePath);

            List<EditorBuildSettingsScene> buildScenes = EditorBuildSettings.scenes
                .Where(s => s.path != ScenePath).ToList();
            buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            log.AppendLine("  build scenes: " + string.Join(", ", buildScenes.Select(s => s.path)));
            return log.ToString();
        }

        // ------------------------------------------------- post-processing and Quest quality

        public const string PostProfilePath = "Assets/Factory/FactoryPostProcess.asset";
        public const string MobileRpAssetPath = "Assets/Settings/Mobile_RPAsset.asset";

        [MenuItem("Tools/Factory/Toggle Post Processing")]
        private static void TogglePostProcessingMenu() => Debug.Log(SetPostProcessing(null));

        /// <summary>
        /// Switches the post-processing of the open factory scene on or off (null = toggle). It costs GPU
        /// time on the headset, so this is the switch to flip if the frame rate needs it.
        /// </summary>
        public static string SetPostProcessing(bool? on)
        {
            var volume = UnityEngine.Object.FindAnyObjectByType<Volume>(FindObjectsInactive.Include);
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (volume == null || rig == null)
            {
                return "[Factory] no post-processing volume or camera rig in the open scene";
            }

            bool enable = on ?? !volume.gameObject.activeSelf;
            volume.gameObject.SetActive(enable);
            foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
            {
                URP.UniversalAdditionalCameraData data = URP.CameraExtensions.GetUniversalAdditionalCameraData(camera);
                data.renderPostProcessing = enable;
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            }

            EditorSceneManager.MarkSceneDirty(volume.gameObject.scene);
            return "[Factory] post-processing " + (enable ? "on" : "off") + " (save the scene to keep it)";
        }

        // A global volume: neutral tonemapping (the lamps and windows no longer clip to flat white),
        // a little contrast and saturation, and a soft glow round the bright lamps. Nothing that is
        // uncomfortable in a headset (no vignette, motion blur, depth of field, grain or distortion).
        internal static void AddPostProcessing(GameObject rig, StringBuilder log)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostProfilePath);
            }

            PostComponent<URP.Tonemapping>(profile).mode.Override(URP.TonemappingMode.Neutral);

            var colour = PostComponent<URP.ColorAdjustments>(profile);
            colour.postExposure.Override(PostExposure);
            colour.contrast.Override(12f);
            colour.saturation.Override(8f);

            PostComponent<URP.WhiteBalance>(profile).temperature.Override(4f);

            var bloom = PostComponent<URP.Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.3f);
            bloom.scatter.Override(0.55f);
            bloom.highQualityFiltering.Override(false);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);

            var go = new GameObject("Post Processing");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;

            int cameras = 0;
            foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
            {
                URP.UniversalAdditionalCameraData data = URP.CameraExtensions.GetUniversalAdditionalCameraData(camera);
                data.renderPostProcessing = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                cameras++;
            }

            log.AppendLine($"  post-processing volume (tonemapping, colour, bloom) on {cameras} rig camera(s)");
        }

        private static T PostComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(false);
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            component.active = true;
            EditorUtility.SetDirty(component);
            return component;
        }

        // The headset build uses the "Mobile" quality level. 4x MSAA is what Meta recommends for Quest,
        // and the template's 0.8 render scale (meant for phones) makes a headset image soft. The Editor is
        // switched to the same quality level so that Play mode and screenshots show what the build shows.
        internal static void ConfigureQuestQuality(StringBuilder log)
        {
            var asset = AssetDatabase.LoadAssetAtPath<URP.UniversalRenderPipelineAsset>(MobileRpAssetPath);
            if (asset == null)
            {
                log.AppendLine("  WARNING " + MobileRpAssetPath + " not found, quality settings left alone");
                return;
            }

            asset.msaaSampleCount = 4;
            asset.renderScale = 1f;
            asset.supportsHDR = true;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            int mobile = Array.IndexOf(QualitySettings.names, "Mobile");
            if (mobile >= 0 && QualitySettings.GetQualityLevel() != mobile)
            {
                QualitySettings.SetQualityLevel(mobile, true);
            }

            log.AppendLine($"  Mobile render pipeline: MSAA 4x, render scale 1.0, HDR on; Editor quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}'");
        }

        // ------------------------------------------------------------------ baking

        /// <summary>Starts an asynchronous bake of the open scene. Poll <see cref="BakeStatus"/>.</summary>
        public static string StartBake(bool draft)
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                throw new InvalidOperationException("Open " + ScenePath + " first.");
            }

            if (Lightmapping.isRunning)
            {
                return "bake already running: " + BakeStatus();
            }

            Lightmapping.lightingSettings = LoadLightingSettings(draft);
            bool started = Lightmapping.BakeAsync();
            return started ? "bake started (" + (draft ? "draft" : "final") + ")" : "bake did not start";
        }

        public static string BakeStatus()
        {
            LightmapData[] maps = LightmapSettings.lightmaps;
            string sizes = string.Join(", ", maps.Where(m => m.lightmapColor != null)
                .Select(m => m.lightmapColor.width + "x" + m.lightmapColor.height));
            return $"running={Lightmapping.isRunning} progress={Lightmapping.buildProgress:0.00} " +
                   $"lightmaps={maps.Length} [{sizes}] probes={LightmapSettings.lightProbes?.count ?? 0}";
        }

        internal static LightingSettings LoadLightingSettings(bool draft, string path = LightingSettingsPath, float resolution = 12f)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings { name = "FactoryLighting" };
                AssetDatabase.CreateAsset(settings, path);
            }

            settings.autoGenerate = false;
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.Subtractive;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.lightmapResolution = draft ? resolution * 0.4f : resolution;
            settings.lightmapMaxSize = 2048;
            settings.lightmapPadding = 4;
            settings.lightmapCompression = LightmapCompression.HighQuality;
            settings.directSampleCount = draft ? 16 : 64;
            settings.indirectSampleCount = draft ? 96 : 512;
            settings.environmentSampleCount = 64;
            settings.maxBounces = 3;
            // Ambient occlusion also on direct light: with 36 soft lights it is what grounds the objects.
            settings.ao = true;
            settings.aoMaxDistance = 0.7f;
            settings.aoExponentIndirect = 2.0f;
            settings.aoExponentDirect = 1.6f;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }

        // ------------------------------------------------------------------ assets

        internal static void ConfigureModel(string path, StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Model not found: " + path);
            }

            if (!importer.bakeAxisConversion || !importer.generateSecondaryUV || importer.importCameras ||
                importer.importLights || importer.animationType != ModelImporterAnimationType.None)
            {
                importer.bakeAxisConversion = true;
                importer.generateSecondaryUV = true;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importBlendShapes = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }

            // Materials still embedded in the model: extract the new ones, and point the ones another
            // model already extracted (same name) at that shared material.
            NBM500UVRSetup.EnsureFolder(MaterialFolder);
            int extracted = 0, shared = 0;
            foreach (Material m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                string target = $"{MaterialFolder}/{m.name}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(target);
                if (existing != null)
                {
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), existing);
                    shared++;
                }
                else if (string.IsNullOrEmpty(AssetDatabase.ExtractAsset(m, target)))
                {
                    extracted++;
                }
            }

            if (extracted > 0 || shared > 0)
            {
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            log.AppendLine($"  {System.IO.Path.GetFileName(path)}: {extracted} materials extracted, {shared} shared");
        }

        private static void ConfigureTextures()
        {
            ImportTexture("T_Hall_Floor", false, 2048, 8);
            ImportTexture("T_Hall_Floor_N", true, 2048, 8);
            ImportTexture("T_Hall_Concrete", false, 1024, 2);
            ImportTexture("T_Hall_Concrete_N", true, 1024, 2);
            ImportTexture("T_Hall_WallPanel", false, 1024, 4);
            ImportTexture("T_Hall_WallPanel_N", true, 1024, 4);
            ImportTexture("T_Hall_RoofDeck", false, 1024, 4);
            ImportTexture("T_Hall_RoofDeck_N", true, 1024, 4);
            ImportTexture("T_Hall_Paint", false, 1024, 2);
            ImportTexture("T_Hall_Paint_N", true, 1024, 2);
            ImportTexture("T_Hall_Hazard", false, 512, 2);
            ImportTexture("T_Hall_Window", false, 1024, 2);
            ImportTexture("T_Hall_Signs", false, 2048, 4);
            ImportTexture("T_Prop_Wood", false, 1024, 2);
            ImportTexture("T_Prop_Wood_N", true, 1024, 2);
            ImportTexture("T_Prop_Steel", false, 1024, 2);
            ImportTexture("T_Prop_Steel_N", true, 1024, 2);
            ImportTexture("T_Prop_Galvanized", false, 1024, 2);

            var shadow = AssetImporter.GetAtPath($"{TextureFolder}/T_Hall_ContactShadow.png") as TextureImporter;
            if (shadow != null && (shadow.wrapMode != TextureWrapMode.Clamp || !shadow.alphaIsTransparency))
            {
                shadow.wrapMode = TextureWrapMode.Clamp;
                shadow.alphaIsTransparency = true;
                shadow.maxTextureSize = 256;
                shadow.SaveAndReimport();
            }
        }

        internal static void ImportTexture(string name, bool normalMap, int maxSize, int aniso)
        {
            string path = $"{TextureFolder}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[Factory] texture not found: " + path);
                return;
            }

            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType == type && importer.maxTextureSize == maxSize && importer.anisoLevel == aniso &&
                importer.wrapMode == TextureWrapMode.Repeat && !importer.alphaIsTransparency)
            {
                return;
            }

            importer.textureType = type;
            importer.maxTextureSize = maxSize;
            importer.anisoLevel = aniso;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        private static void ConfigureMaterials(StringBuilder log)
        {
            // The hall and the props carry world-scale UVs (1 UV = 1 m), so tiling = 1 / tile size in metres.
            // Floor: epoxy on concrete, 4 m tile, gloss taken from the alpha channel of the colour map.
            Maps("M_Hall_Floor", new Color(0.80f, 0.82f, 0.80f), 0f, 1f, "T_Hall_Floor", "T_Hall_Floor_N", 1f / 3f, smoothnessFromAlpha: true);
            Maps("M_Hall_ConcreteWhite", new Color(0.97f, 0.97f, 0.96f), 0f, 0.15f, "T_Hall_Concrete", "T_Hall_Concrete_N", 0.5f);
            Maps("M_Hall_Dado", new Color(0.42f, 0.52f, 0.56f), 0f, 0.3f, "T_Hall_Concrete", "T_Hall_Concrete_N", 0.5f);
            Maps("M_Hall_WallPanel", new Color(0.96f, 0.97f, 0.98f), 0.1f, 0.35f, "T_Hall_WallPanel", "T_Hall_WallPanel_N", 1f);
            Maps("M_Hall_RoofDeck", new Color(0.86f, 0.87f, 0.88f), 0.2f, 0.35f, "T_Hall_RoofDeck", "T_Hall_RoofDeck_N", 1f);
            Maps("M_Hall_Hazard", Color.white, 0f, 0.35f, "T_Hall_Hazard", null, 1f);
            Maps("M_Hall_Signs", Color.white, 0f, 0.2f, "T_Hall_Signs", null, 1f);

            // Painted steel: one worn-paint map tinted by the material colour.
            Paint("M_Hall_CraneYellow", new Color(0.95f, 0.60f, 0.05f), 0.1f);
            Paint("M_Hall_SteelDark", new Color(0.24f, 0.26f, 0.30f), 0.4f);
            Paint("M_Hall_SafetyYellow", new Color(0.93f, 0.68f, 0.05f), 0f);
            Paint("M_Hall_Gate", new Color(0.55f, 0.57f, 0.60f), 0.3f);
            Paint("M_Prop_Red", new Color(0.72f, 0.09f, 0.07f), 0.1f);
            Paint("M_Prop_Blue", new Color(0.12f, 0.28f, 0.52f), 0.1f);
            Paint("M_Prop_Grey", new Color(0.60f, 0.62f, 0.63f), 0.1f);
            Paint("M_Prop_Green", new Color(0.12f, 0.33f, 0.20f), 0.1f);

            Surface("M_Hall_LineYellow", new Color(0.85f, 0.62f, 0.05f), 0f, 0.45f);
            Surface("M_Hall_LineGreen", new Color(0.10f, 0.45f, 0.24f), 0f, 0.45f);
            Surface("M_Hall_SteelWhite", new Color(0.84f, 0.85f, 0.86f), 0.2f, 0.4f);
            Surface("M_Hall_Rail", new Color(0.46f, 0.45f, 0.44f), 0.35f, 0.55f);
            Paint("M_Stand_Paint", new Color(0.17f, 0.32f, 0.50f), 0.2f);
            Surface("M_Stand_Steel", new Color(0.55f, 0.56f, 0.58f), 0.9f, 0.6f);
            Maps("M_Prop_DarkSteel", Color.white, 0.5f, 1f, "T_Prop_Steel", "T_Prop_Steel_N", 0.8f, smoothnessFromAlpha: true);
            Maps("M_Prop_Wood", Color.white, 0f, 0.15f, "T_Prop_Wood", "T_Prop_Wood_N", 1f);
            Surface("M_Prop_Black", new Color(0.05f, 0.05f, 0.05f), 0f, 0.35f);
            Surface("M_Prop_Screen", new Color(0.86f, 0.33f, 0.04f), 0f, 0.4f);
            Maps("M_Prop_Duct", new Color(0.92f, 0.93f, 0.94f), 0.5f, 0.5f, "T_Prop_Galvanized", null, 0.7f);
            Surface("M_Prop_Cardboard", new Color(0.62f, 0.48f, 0.32f), 0f, 0.1f);

            // Daylight windows light the hall in the bake; the fixtures only look bright (their light
            // comes from the area lights placed under them).
            // (Both are BakedEmissive: with any other GI flag URP switched the emission keyword off again.)
            // Window panes are 1 m wide and 0.8 m high; the offset puts a glazing bar on the window sill.
            Emissive("M_Hall_WindowGlass", new Color(0.35f, 0.37f, 0.40f), Color.white, 2.2f,
                MaterialGlobalIlluminationFlags.BakedEmissive, "T_Hall_Window", new Vector2(1f, 1.25f), new Vector2(0f, -0.125f));
            Emissive("M_Hall_LightPanel", new Color(1f, 0.97f, 0.9f), new Color(1f, 0.97f, 0.9f), 4f,
                MaterialGlobalIlluminationFlags.BakedEmissive, null, Vector2.one, Vector2.zero);

            Textured("M_Gondola_Body", "T_Gondola_Body", 0.2f, 0.35f);
            Textured("M_Gondola_Coupler", "T_Gondola_Coupler", 0.4f, 0.4f);
            Textured("M_Gondola_BogieA", "T_Gondola_BogieA", 0.1f, 0.35f);
            Textured("M_Gondola_BogieB", "T_Gondola_BogieB", 0.1f, 0.35f);
            Textured("M_Gondola_Wheel", "T_Gondola_Wheel", 0.15f, 0.45f);
            AssetDatabase.SaveAssets();
            log.AppendLine("  materials configured");
        }

        internal static Material LoadMaterial(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
            if (m == null)
            {
                Debug.LogWarning("[Factory] material not found: " + name);
            }

            return m;
        }

        internal static void Surface(string name, Color color, float metallic, float smoothness)
        {
            Material m = LoadMaterial(name);
            if (m == null) return;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
        }

        internal static void Emissive(string name, Color color, Color glow, float intensity,
            MaterialGlobalIlluminationFlags gi, string texture, Vector2 tiling, Vector2 offset)
        {
            Material m = LoadMaterial(name);
            if (m == null) return;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", glow.linear * intensity);
            m.globalIlluminationFlags = gi;
            if (texture != null)
            {
                Texture2D t = LoadTexture(texture);
                m.SetTexture("_BaseMap", t);
                m.SetTexture("_EmissionMap", t);
                m.SetTextureScale("_BaseMap", tiling);
                m.SetTextureOffset("_BaseMap", offset);
            }

            EditorUtility.SetDirty(m);
        }

        // Unlit, alpha-blended black quad: T_Hall_ContactShadow fades out towards the edge of the quad.
        internal static Material ContactShadowMaterial(string name, float strength)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }

            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", LoadTexture("T_Hall_ContactShadow"));
            m.SetColor("_BaseColor", new Color(0f, 0f, 0f, strength));
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Texture2D LoadTexture(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{name}.png");
            if (t == null) t = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Factory/Textures/{name}.png");   // maps shared by all scenes
            if (t == null)
            {
                Debug.LogWarning("[Factory] texture not found: " + name);
            }

            return t;
        }

        /// <summary>Colour map (tinted by <paramref name="tint"/>) and optional normal map, square tiling.</summary>
        internal static void Maps(string name, Color tint, float metallic, float smoothness, string colourMap,
            string normalMap, float tiling, bool smoothnessFromAlpha = false)
        {
            Material m = LoadMaterial(name);
            if (m == null) return;
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetTexture("_BaseMap", LoadTexture(colourMap));
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            if (normalMap != null)
            {
                m.SetTexture("_BumpMap", LoadTexture(normalMap));
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }

            m.SetFloat("_SmoothnessTextureChannel", smoothnessFromAlpha ? 1f : 0f);
            if (smoothnessFromAlpha) m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            else m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(m);
        }

        internal static void Paint(string name, Color tint, float metallic)
        {
            Maps(name, tint, metallic, 0.9f, "T_Hall_Paint", "T_Hall_Paint_N", 0.6f, smoothnessFromAlpha: true);
        }

        internal static void Textured(string name, string texture, float metallic, float smoothness)
        {
            Material m = LoadMaterial(name);
            if (m == null) return;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{texture}.png");
            if (t == null)
            {
                Debug.LogWarning("[Factory] texture not found: " + texture);
                return;
            }

            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
        }

        // ----------------------------------------------------------------- helpers

        internal static GameObject Instantiate(string modelPath, Scene scene)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null)
            {
                throw new InvalidOperationException("Model not found: " + modelPath);
            }

            return (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        }

        internal static Transform Find(GameObject root, string name)
        {
            Transform t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == name);
            if (t == null)
            {
                throw new InvalidOperationException($"'{name}' not found under '{root.name}'");
            }

            return t;
        }

        internal static Bounds WorldBounds(Transform t)
        {
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                b.Encapsulate(r.bounds);
            }

            return b;
        }

        /// <summary>Static for the bake (casts shadows, bounces light) but lit by light probes itself.</summary>
        internal static void ProbeLitStatic(GameObject root)
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticFlags);
                r.receiveGI = ReceiveGI.LightProbes;
            }
        }

        /// <summary>
        /// Interactive prefabs stay dynamic, but their parts that never move (no Rigidbody above them)
        /// take part in the bake so that they throw a shadow on the floor. Returns the number of parts.
        /// </summary>
        internal static int BakedShadowCasters(GameObject root)
        {
            int count = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.GetComponentInParent<Rigidbody>() != null)
                {
                    continue;
                }

                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI);
                r.receiveGI = ReceiveGI.LightProbes;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                count++;
            }

            return count;
        }

        internal static void Lightmapped(MeshRenderer r, float scale)
        {
            r.receiveGI = ReceiveGI.Lightmaps;
            r.scaleInLightmap = scale;
        }
    }
}
