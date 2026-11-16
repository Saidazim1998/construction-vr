using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NBM500U.EditorTools
{
    /// <summary>
    /// Builds the first scene of the trainer, the engineers' room: the room with its furniture, the desk
    /// with the safety journal (stage 1 of the scenario) and the stand with the protective equipment the
    /// trainee puts on (stage 2). The journal desk and the stand share one corner so that both are within
    /// reach of a player who cannot walk far. Everything is rebuilt from the models, so run it again after
    /// changing a model. Shared helpers come from <see cref="FactorySceneSetup"/>.
    /// </summary>
    public static class EngineerRoomSceneSetup
    {
        public const string AssetRoot = "Assets/EngineerRoom";
        public const string RoomFbx = AssetRoot + "/Models/EngineerRoom.fbx";
        public const string ScenePath = "Assets/Scenes/EngineerRoom.unity";
        public const string LightingSettingsPath = AssetRoot + "/EngineerRoomLighting.lighting";

        // model file, anchor in the room model the item is placed at
        private static readonly (string model, string anchor)[] Equipment =
        {
            ("PPE_Helmet", "Anchor_Helmet"), ("PPE_Jacket", "Anchor_Jacket"),
            ("PPE_Glove_L", "Anchor_Glove_L"), ("PPE_Glove_R", "Anchor_Glove_R"),
            ("PPE_Boot_L", "Anchor_Boot_L"), ("PPE_Boot_R", "Anchor_Boot_R"),
        };

        // Ceiling lamps (x, z) and their height: keep in sync with LAMPS in build_room.py.
        private static readonly Vector2[] Lamps =
        {
            new Vector2(-2.9f, -1.7f), new Vector2(0f, -1.7f), new Vector2(2.9f, -1.7f),
            new Vector2(-2.9f, 1.3f), new Vector2(0f, 1.3f), new Vector2(2.9f, 1.3f),
        };

        private const float LampHeight = 3.17f;
        private const float LampIntensity = 42f;
        private const float ContactShadowCore = 0.62f;

        [MenuItem("Tools/Engineer Room/Build Scene")]
        private static void BuildSceneMenu() => Debug.Log(BuildScene());

        [MenuItem("Tools/Engineer Room/Bake Lighting (final quality)")]
        private static void BakeMenu() => Debug.Log(StartBake(false));

        public static string BuildScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException(
                        "An open scene has unsaved changes. Save or discard them, then run this again.");
                }
            }

            FactorySceneSetup.UseAssetRoot(AssetRoot);
            try
            {
                return Build();
            }
            finally
            {
                FactorySceneSetup.UseAssetRoot("Assets/Factory");
            }
        }

        private static string Build()
        {
            var log = new StringBuilder("[EngineerRoom] Build scene\n");
            FactorySceneSetup.ConfigureModel(RoomFbx, log);
            foreach ((string model, string _) in Equipment)
            {
                FactorySceneSetup.ConfigureModel($"{AssetRoot}/Models/{model}.fbx", log);
            }

            ConfigureTextures();
            ConfigureMaterials();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- room and furniture
            GameObject room = FactorySceneSetup.Instantiate(RoomFbx, scene);
            foreach (MeshRenderer r in room.GetComponentsInChildren<MeshRenderer>())
            {
                string n = r.name;
                if (n.StartsWith("Journal_") || n == "Mirror_Surface")
                {
                    continue;                               // the journal and its pen stay dynamic
                }

                GameObjectUtility.SetStaticEditorFlags(r.gameObject, FactorySceneSetup.StaticFlags);
                bool flat = n == "Room_Windows" || n == "Room_Lights";
                r.shadowCastingMode = flat ? ShadowCastingMode.Off : ShadowCastingMode.On;
                switch (n)
                {
                    case "Room_Floor": FactorySceneSetup.Lightmapped(r, 1f); break;
                    case "Room_Walls": FactorySceneSetup.Lightmapped(r, 0.6f); break;
                    case "Room_WallDado": FactorySceneSetup.Lightmapped(r, 0.6f); break;
                    case "Room_Ceiling": FactorySceneSetup.Lightmapped(r, 0.4f); break;
                    default: r.receiveGI = ReceiveGI.LightProbes; break;
                }
            }

            Transform floorObject = FactorySceneSetup.Find(room, "Room_Floor");
            Bounds floor = FactorySceneSetup.WorldBounds(floorObject);
            var floorCollider = floorObject.gameObject.AddComponent<BoxCollider>();
            Bounds localFloor = floorObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 thin = floorObject.InverseTransformVector(Vector3.up * 0.2f);
            floorCollider.center = localFloor.center - thin * 0.5f;
            floorCollider.size = localFloor.size + new Vector3(Mathf.Abs(thin.x), Mathf.Abs(thin.y), Mathf.Abs(thin.z));
            log.AppendLine($"  room {floor.size.x:0.0} x {floor.size.z:0.0} m");

            // ---- protective equipment on the stand: every item can be picked up and goes back when let go
            int items = 0;
            foreach ((string model, string anchorName) in Equipment)
            {
                Transform anchor = FactorySceneSetup.Find(room, anchorName);
                GameObject item = FactorySceneSetup.Instantiate($"{AssetRoot}/Models/{model}.fbx", scene);
                item.name = model;
                item.transform.position = anchor.position;      // keep the rotation the importer gave the model
                MakeGrabbable(item, 0f);
                ConstructionVR.EditorTools.PlayerBodySetup.MakeWearable(item, PartOf(model));
                items++;
            }

            MakeGrabbable(FactorySceneSetup.Find(room, "Journal_Pen").gameObject, 0.035f);
            log.AppendLine($"  {items} pieces of protective equipment and the journal pen can be grabbed");

            // ---- player: between the journal desk (in front) and the equipment stand (to the left)
            Vector3 player = FactorySceneSetup.Find(room, "Anchor_Player").position;
            Vector3 journal = FactorySceneSetup.Find(room, "Journal_Open").position;
            Vector3 towardsDesk = journal - player;
            towardsDesk.y = 0f;
            Quaternion facing = Quaternion.LookRotation(Mathf.Abs(towardsDesk.x) > Mathf.Abs(towardsDesk.z)
                ? new Vector3(Mathf.Sign(towardsDesk.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(towardsDesk.z)));
            GameObject rig = NBM500UVRSetup.AddPlayerRig(scene, new Vector3(player.x, 0f, player.z), facing, log);
            ConstructionVR.EditorTools.PlayerBodySetup.AddToRig(rig, log);
            log.AppendLine($"  player at {player}, facing {facing.eulerAngles.y:0} deg, journal at {journal}");

            // ---- lighting: baked lamps and daylight, probes for everything that is not lightmapped
            var lighting = new GameObject("Lighting");
            Quaternion down = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            foreach (Vector2 lamp in Lamps)
            {
                var go = new GameObject($"Lamp_{lamp.x:0.0}_{lamp.y:0.0}");
                go.transform.SetParent(lighting.transform, false);
                go.transform.SetPositionAndRotation(new Vector3(lamp.x, LampHeight, lamp.y), down);
                var light = go.AddComponent<Light>();
                light.type = LightType.Rectangle;
                light.areaSize = new Vector2(1.25f, 0.22f);
                light.color = new Color(1f, 0.97f, 0.92f);
                light.intensity = LampIntensity;
                light.range = 15f;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.shadows = LightShadows.Soft;
            }

            var probes = new List<Vector3>();
            float[] heights = { 0.3f, 1.3f, 2.3f, 3.1f };
            for (float x = -4f; x <= 4.01f; x += 2f)
                for (float z = -3f; z <= 3.01f; z += 2f)
                    foreach (float h in heights)
                        probes.Add(new Vector3(x, h, z));
            for (float x = -4.1f; x <= -1.69f; x += 0.8f)              // denser where the player works
                for (float z = -3.2f; z <= -0.79f; z += 0.8f)
                    foreach (float h in new[] { 0.5f, 1.2f, 1.9f })
                        probes.Add(new Vector3(x, h, z));
            var probeObject = new GameObject("Light Probes");
            probeObject.transform.SetParent(lighting.transform, false);
            probeObject.AddComponent<LightProbeGroup>().probePositions = probes.ToArray();

            var reflectionObject = new GameObject("Reflection Probe");
            reflectionObject.transform.SetParent(lighting.transform, false);
            reflectionObject.transform.position = new Vector3(0f, 1.6f, 0f);
            var reflection = reflectionObject.AddComponent<ReflectionProbe>();
            reflection.mode = ReflectionProbeMode.Baked;
            reflection.resolution = 128;
            reflection.boxProjection = true;
            reflection.size = new Vector3(floor.size.x, 3.4f, floor.size.z);
            reflection.center = new Vector3(0f, 0.1f, 0f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.03f, 0.03f, 0.035f);
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            log.AppendLine($"  {Lamps.Length} baked lamps, {probes.Count} light probes, 1 reflection probe");

            ConstructionVR.EditorTools.PlayerBodySetup.AddMirror(
                FactorySceneSetup.Find(room, "Mirror_Surface").GetComponent<MeshRenderer>(), log);
            AddContactShadows(log);
            FactorySceneSetup.ConfigureQuestQuality(log);
            FactorySceneSetup.AddPostProcessing(rig, log);

            Lightmapping.lightingSettings = FactorySceneSetup.LoadLightingSettings(true, LightingSettingsPath, 30f);
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

        /// <summary>Starts an asynchronous bake of the open room scene. Poll FactorySceneSetup.BakeStatus().</summary>
        public static string StartBake(bool draft)
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                throw new InvalidOperationException("Open " + ScenePath + " first.");
            }

            if (Lightmapping.isRunning)
            {
                return "bake already running: " + FactorySceneSetup.BakeStatus();
            }

            Lightmapping.lightingSettings = FactorySceneSetup.LoadLightingSettings(draft, LightingSettingsPath, 30f);
            return Lightmapping.BakeAsync() ? "bake started (" + (draft ? "draft" : "final") + ")" : "bake did not start";
        }

        private static ConstructionVR.Player.OutfitPart PartOf(string model)
        {
            switch (model)
            {
                case "PPE_Helmet": return ConstructionVR.Player.OutfitPart.Helmet;
                case "PPE_Jacket": return ConstructionVR.Player.OutfitPart.Jacket;
                case "PPE_Glove_L": return ConstructionVR.Player.OutfitPart.GloveLeft;
                case "PPE_Glove_R": return ConstructionVR.Player.OutfitPart.GloveRight;
                case "PPE_Boot_L": return ConstructionVR.Player.OutfitPart.BootLeft;
                case "PPE_Boot_R": return ConstructionVR.Player.OutfitPart.BootRight;
                default: throw new ArgumentException("Not a piece of protective equipment: " + model);
            }
        }

        // ------------------------------------------------------------------ interaction

        // A kinematic body with a box collider that can be grabbed with a controller or a tracked hand and
        // drifts back to where it was when it is let go. Wearing the equipment is the job of the scenario
        // logic, which is not written yet.
        private static void MakeGrabbable(GameObject go, float minColliderSize)
        {
            MeshFilter filter = go.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
            {
                throw new InvalidOperationException($"'{go.name}' has no mesh (did the model import lose it?)");
            }

            Bounds b = filter.sharedMesh.bounds;
            var box = go.AddComponent<BoxCollider>();
            Vector3 centre = go.transform.InverseTransformPoint(filter.transform.TransformPoint(b.center));
            Vector3 size = go.transform.InverseTransformVector(filter.transform.TransformVector(b.size));
            Vector3 scale = go.transform.lossyScale;                // the minimum is in metres, the collider in local units
            box.center = centre;
            box.size = new Vector3(
                Mathf.Max(Mathf.Abs(size.x), minColliderSize / Mathf.Abs(scale.x)),
                Mathf.Max(Mathf.Abs(size.y), minColliderSize / Mathf.Abs(scale.y)),
                Mathf.Max(Mathf.Abs(size.z), minColliderSize / Mathf.Abs(scale.z)));

            Rigidbody body = NBM500UVRSetup.AddKinematicBody(go);
            var free = go.AddComponent<GrabFreeTransformer>();
            Grabbable grabbable = NBM500UVRSetup.AddGrabbable(go, body, free, free);
            NBM500UVRSetup.AddGrabInteractables(go, body, grabbable, null);

            var rest = go.AddComponent<NBMReturnToRest>();
            var so = new SerializedObject(rest);
            so.FindProperty("_grabbable").objectReferenceValue = grabbable;
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
            {
                r.receiveGI = ReceiveGI.LightProbes;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            }
        }

        // ------------------------------------------------------------------ assets

        private static void ConfigureTextures()
        {
            FactorySceneSetup.ImportTexture("T_Room_Floor", false, 2048, 8);
            FactorySceneSetup.ImportTexture("T_Room_Floor_N", true, 2048, 8);
            FactorySceneSetup.ImportTexture("T_Room_Wall", false, 1024, 2);
            FactorySceneSetup.ImportTexture("T_Room_Wall_N", true, 1024, 2);
            FactorySceneSetup.ImportTexture("T_Room_DeskTop", false, 1024, 4);
            FactorySceneSetup.ImportTexture("T_Room_DeskTop_N", true, 1024, 4);
            FactorySceneSetup.ImportTexture("T_Room_Graphics", false, 2048, 4);
            FactorySceneSetup.ImportTexture("T_PPE_Helmet", false, 1024, 2);
            FactorySceneSetup.ImportTexture("T_PPE_Jacket", false, 2048, 2);
            FactorySceneSetup.ImportTexture("T_PPE_Glove", false, 1024, 2);
            FactorySceneSetup.ImportTexture("T_PPE_Boot", false, 1024, 2);
        }

        private static void ConfigureMaterials()
        {
            // The models carry world-scale UVs (1 UV = 1 m), so tiling = 1 / tile size in metres.
            FactorySceneSetup.Maps("M_Room_Floor", Color.white, 0f, 1f, "T_Room_Floor", "T_Room_Floor_N", 0.4f, smoothnessFromAlpha: true);
            FactorySceneSetup.Maps("M_Room_Wall", new Color(0.98f, 0.97f, 0.95f), 0f, 0.1f, "T_Room_Wall", "T_Room_Wall_N", 0.5f);
            FactorySceneSetup.Maps("M_Room_Dado", new Color(0.62f, 0.74f, 0.80f), 0f, 0.25f, "T_Room_Wall", "T_Room_Wall_N", 0.5f);
            FactorySceneSetup.Maps("M_Room_Ceiling", new Color(0.97f, 0.97f, 0.96f), 0f, 0.1f, "T_Hall_Concrete", "T_Hall_Concrete_N", 0.5f);
            FactorySceneSetup.Maps("M_Room_DeskTop", Color.white, 0f, 1f, "T_Room_DeskTop", "T_Room_DeskTop_N", 0.8f, smoothnessFromAlpha: true);
            FactorySceneSetup.Maps("M_Room_Graphics", Color.white, 0f, 0.15f, "T_Room_Graphics", null, 1f);
            FactorySceneSetup.Maps("M_Room_Duct", new Color(0.92f, 0.93f, 0.94f), 0.5f, 0.5f, "T_Prop_Galvanized", null, 0.7f);

            FactorySceneSetup.Paint("M_Room_MetalGrey", new Color(0.66f, 0.68f, 0.70f), 0.1f);
            FactorySceneSetup.Paint("M_Room_MetalBlue", new Color(0.13f, 0.30f, 0.55f), 0.1f);
            FactorySceneSetup.Paint("M_Room_MetalRed", new Color(0.74f, 0.10f, 0.08f), 0.1f);
            FactorySceneSetup.Paint("M_Room_MetalGreen", new Color(0.13f, 0.42f, 0.24f), 0.1f);
            FactorySceneSetup.Paint("M_Room_Yellow", new Color(0.95f, 0.72f, 0.06f), 0f);

            FactorySceneSetup.Surface("M_Room_Trim", new Color(0.24f, 0.25f, 0.27f), 0f, 0.3f);
            FactorySceneSetup.Surface("M_Room_Steel", new Color(0.36f, 0.37f, 0.39f), 0.6f, 0.55f);
            FactorySceneSetup.Surface("M_Room_Black", new Color(0.05f, 0.05f, 0.05f), 0f, 0.35f);
            FactorySceneSetup.Surface("M_Room_White", new Color(0.93f, 0.93f, 0.91f), 0f, 0.35f);
            FactorySceneSetup.Surface("M_Room_Fabric", new Color(0.12f, 0.14f, 0.18f), 0f, 0.05f);
            FactorySceneSetup.Surface("M_Room_Cardboard", new Color(0.62f, 0.48f, 0.32f), 0f, 0.1f);

            // Each window is 2 x 2 panes of the glazing texture; the lamps only look bright, their light
            // comes from the area lights under them.
            FactorySceneSetup.Emissive("M_Room_Window", new Color(0.35f, 0.37f, 0.40f), Color.white, 2.4f,
                MaterialGlobalIlluminationFlags.BakedEmissive, "T_Hall_Window", Vector2.one, Vector2.zero);
            FactorySceneSetup.Emissive("M_Room_LightPanel", new Color(1f, 0.97f, 0.92f), new Color(1f, 0.97f, 0.92f), 4f,
                MaterialGlobalIlluminationFlags.BakedEmissive, null, Vector2.one, Vector2.zero);

            FactorySceneSetup.Textured("M_PPE_Helmet", "T_PPE_Helmet", 0f, 0.45f);
            FactorySceneSetup.Textured("M_PPE_Jacket", "T_PPE_Jacket", 0f, 0.12f);
            FactorySceneSetup.Textured("M_PPE_Glove", "T_PPE_Glove", 0f, 0.15f);
            FactorySceneSetup.Textured("M_PPE_Boot", "T_PPE_Boot", 0f, 0.35f);
            AssetDatabase.SaveAssets();
        }

        // Soft dark quads on the floor under the furniture (footprints in room coordinates, metres).
        private static void AddContactShadows(StringBuilder log)
        {
            var parent = new GameObject("Contact Shadows");
            Material strong = FactorySceneSetup.ContactShadowMaterial("M_Room_ContactShadow", 0.55f);
            Material soft = FactorySceneSetup.ContactShadowMaterial("M_Room_ContactShadowSoft", 0.32f);
            var footprints = new (string name, float x, float z, float sizeX, float sizeZ, Material material)[]
            {
                ("Desk", -4.1f, -1.9f, 0.70f, 1.50f, soft), ("Stand", -2.5f, -3.22f, 1.64f, 0.50f, soft),
                ("Bench", -1.25f, -2.55f, 0.36f, 1.10f, soft), ("Lockers", 1.15f, -3.24f, 2.42f, 0.50f, strong),
                ("Table", 1.3f, -0.3f, 1.80f, 0.90f, soft), ("Workbench1", -2.2f, 2.88f, 2.0f, 0.75f, soft),
                ("Workbench2", 1.0f, 2.88f, 2.0f, 0.75f, soft), ("ToolCabinet", 3.9f, 3.05f, 0.70f, 0.46f, strong),
                ("Bookcase", -4.3f, 0.55f, 0.38f, 1.0f, strong), ("BinShelving", 4.22f, 1.75f, 0.5f, 1.94f, soft),
                ("Chair", -3.25f, -0.75f, 0.55f, 0.55f, soft),
            };
            foreach (var f in footprints)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.name = "Shadow_" + f.name;
                quad.transform.SetParent(parent.transform, false);
                quad.transform.SetPositionAndRotation(new Vector3(f.x, 0.008f, f.z), Quaternion.Euler(90f, 0f, 0f));
                quad.transform.localScale = new Vector3(f.sizeX / ContactShadowCore, f.sizeZ / ContactShadowCore, 1f);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = f.material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(quad, StaticEditorFlags.BatchingStatic);
            }

            log.AppendLine($"  {footprints.Length} contact shadows");
        }
    }
}
