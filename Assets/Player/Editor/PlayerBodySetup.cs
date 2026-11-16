using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConstructionVR.Player;
using Meta.XR.Movement.Editor;
using Meta.XR.Movement.Retargeting;
using Meta.XR.Movement.Retargeting.Editor;
using NBM500U;
using NBM500U.EditorTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ConstructionVR.EditorTools
{
    /// <summary>
    /// Builds the full-body player: the modular body model (see D:\Blender\Player\build_player.py) becomes a
    /// prefab that is driven by Meta's body tracking through the Movement SDK retargeter and dressed by
    /// <see cref="PlayerOutfit"/>. The scene builders call <see cref="AddToRig"/>, <see cref="MakeWearable"/>
    /// and <see cref="AddMirror"/>.
    /// </summary>
    public static class PlayerBodySetup
    {
        public const string AssetRoot = "Assets/Player";
        public const string ModelPath = AssetRoot + "/Models/PlayerBody.fbx";
        public const string PrefabPath = AssetRoot + "/PlayerBody.prefab";
        public const string MirrorMaterialPath = AssetRoot + "/Materials/M_PlanarMirror.mat";
        public const string HelmetModelPath = "Assets/EngineerRoom/Models/PPE_Helmet.fbx";

        /// <summary>The head (and the worn helmet) are on this layer: the player's own eyes do not render it.</summary>
        public const string HeadLayerName = "PlayerHead";

        private const int WaterLayer = 4;        // built-in layer, used for mirror surfaces (not reflected)
        private const int SetupVersion = 9;   // bump to check that new code is loaded

        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        // piece name of the body model -> (part, worn or bare)
        private static readonly (OutfitPart part, string[] worn, string[] bare)[] Pieces =
        {
            (OutfitPart.Helmet, new[] { "Worn_Helmet" }, new string[0]),
            // The bare forearm under a sleeve and the bare wrist under a gauntlet cuff go off with the clothing
            // that covers them: hidden skin would poke through when the wrist bends.
            (OutfitPart.Jacket, new[] { "Body_Torso_Jacket" }, new[] { "Body_Torso_Base", "Body_Wrist_L_Upper", "Body_Wrist_R_Upper" }),
            (OutfitPart.GloveLeft, new[] { "Body_Hand_L_Glove" }, new[] { "Body_Hand_L_Bare", "Body_Wrist_L_Lower" }),
            (OutfitPart.GloveRight, new[] { "Body_Hand_R_Glove" }, new[] { "Body_Hand_R_Bare", "Body_Wrist_R_Lower" }),
            (OutfitPart.BootLeft, new[] { "Body_Foot_L_Boot" }, new[] { "Body_Foot_L_Bare" }),
            (OutfitPart.BootRight, new[] { "Body_Foot_R_Boot" }, new[] { "Body_Foot_R_Bare" }),
        };

        [MenuItem("Tools/Player/Build Player Body Prefab")]
        private static void BuildPrefabMenu() => Debug.Log(BuildPrefab());

        // ----------------------------------------------------------------- prefab

        public static string BuildPrefab()
        {
            var log = new StringBuilder("[Player] Build player body prefab\n");
            int headLayer = EnsureLayer(HeadLayerName, 12);
            log.AppendLine($"  layer '{HeadLayerName}' = {headLayer}");

            ConfigureModel(log);
            FactorySceneSetup.UseAssetRoot(AssetRoot);
            try
            {
                FactorySceneSetup.ImportTexture("T_Player_Base", false, 2048, 2);
                FactorySceneSetup.ImportTexture("T_Player_Dressed", false, 2048, 2);
                FactorySceneSetup.Textured("M_Player_Base", "T_Player_Base", 0f, 0.15f);
                FactorySceneSetup.Textured("M_Player_Dressed", "T_Player_Dressed", 0f, 0.2f);
                foreach (string name in new[] { "M_Player_Base", "M_Player_Dressed" })
                {
                    Material m = FactorySceneSetup.LoadMaterial(name);
                    if (m != null)
                    {
                        m.SetFloat("_Cull", (float)CullMode.Off);      // sleeves, cuffs and hems are open: show both sides
                        m.doubleSidedGI = true;
                        EditorUtility.SetDirty(m);
                    }
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                FactorySceneSetup.UseAssetRoot("Assets/Factory");
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            int humanFingers = ((ModelImporter)AssetImporter.GetAtPath(ModelPath)).humanDescription.human
                .Count(h => FingerNames.Any(f => h.boneName.Contains("Hand" + f)));
            log.AppendLine($"  avatar: {(avatar == null ? "none" : $"valid={avatar.isValid} human={avatar.isHuman}")}, {humanFingers}/30 finger bones in the human map");

            // Movement SDK: retargeting configuration (body tracking skeleton -> this rig). A configuration made
            // for an older model without finger bones is thrown away: the setup would keep its joint list.
            string oldConfig = Path.ChangeExtension(ModelPath, ".json");
            if (File.Exists(oldConfig) && !File.ReadAllText(oldConfig).Contains("\"LeftHandIndex1\""))
            {
                AssetDatabase.DeleteAsset(oldConfig);
                AssetDatabase.DeleteAsset(Path.ChangeExtension(ModelPath, null) + "-metadata.asset");
                log.AppendLine("  old retargeting config (no fingers) removed");
            }

            MSDKUtilityEditorMetadata metadata = MSDKUtilityEditor.RunDefaultRetargetingSetup(model);
            if (metadata == null || metadata.ConfigJson == null)
            {
                throw new InvalidOperationException("Movement SDK did not produce a retargeting config for " + ModelPath);
            }

            log.AppendLine("  retargeting config: " + AssetDatabase.GetAssetPath(metadata.ConfigJson));
            FixFingerMapping(AssetDatabase.GetAssetPath(metadata.ConfigJson), log);

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                body.name = "PlayerBody";

                var retargeter = body.AddComponent<CharacterRetargeter>();
                retargeter.ConfigAsset = metadata.ConfigJson;
                using (var so = new SerializedObject(retargeter))
                {
                    CharacterRetargeterConfigEditor.LoadConfig(so, retargeter);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                var provider = body.AddComponent<MetaSourceDataProvider>();
                provider.ProvidedSkeletonType = OVRPlugin.BodyJointSet.FullBody;

                foreach (SkinnedMeshRenderer r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    r.updateWhenOffscreen = true;           // the body is around the camera: never cull it by its rest bounds
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                    if (r.name.StartsWith("Body_Hand_", StringComparison.Ordinal))
                    {
                        // A finger joint next to the wrist blends hand, forearm and two finger bones; the
                        // quality level used on the headset would keep only two of them.
                        r.quality = SkinQuality.Bone4;
                    }
                }

                // Fingers: 3 bones each, mapped by the retargeting config (the source skeleton has the hand joints).
                int fingerBones = 0;
                string config = File.ReadAllText(AssetDatabase.GetAssetPath(metadata.ConfigJson));
                int mapped = 0;
                foreach (string side in new[] { "Left", "Right" })
                {
                    foreach (string finger in FingerNames)
                    {
                        for (int i = 1; i <= 3; i++)
                        {
                            string bone = $"{side}Hand{finger}{i}";
                            if (FindChild(body, bone) != null) fingerBones++;
                            if (config.Contains("\"" + bone + "\"")) mapped++;
                        }
                    }
                }

                log.AppendLine($"  finger bones: {fingerBones}/30 in the model, {mapped}/30 named in the retargeting config");
                if (fingerBones != 30)
                {
                    throw new InvalidOperationException("The body model has no finger bones: rebuild it with D:\\Blender\\Player\\build_player.py");
                }

                Transform head = Bone(body, "Head");
                SetLayer(Bone(body, "Body_Head").gameObject, headLayer);

                // The helmet the player wears: the stand's helmet model, fixed on the head bone.
                var helmetModel = AssetDatabase.LoadAssetAtPath<GameObject>(HelmetModelPath);
                if (helmetModel != null)
                {
                    var helmet = (GameObject)PrefabUtility.InstantiatePrefab(helmetModel, preview);
                    helmet.name = "Worn_Helmet";
                    helmet.transform.SetParent(head, true);
                    helmet.transform.position = head.position + new Vector3(0f, -0.03f, 0.025f);
                    SetLayer(helmet, headLayer);
                    foreach (MeshRenderer r in helmet.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveGI = ReceiveGI.LightProbes;
                    }

                    helmet.SetActive(false);
                }
                else
                {
                    log.AppendLine("  WARNING helmet model not found: " + HelmetModelPath);
                }

                var outfit = body.AddComponent<PlayerOutfit>();
                using (var so = new SerializedObject(outfit))
                {
                    SerializedProperty pieces = so.FindProperty("_pieces");
                    pieces.arraySize = Pieces.Length;
                    for (int i = 0; i < Pieces.Length; i++)
                    {
                        SerializedProperty piece = pieces.GetArrayElementAtIndex(i);
                        piece.FindPropertyRelative("part").intValue = (int)Pieces[i].part;
                        SetObjects(piece.FindPropertyRelative("worn"), Pieces[i].worn.Select(n => FindChild(body, n)));
                        SetObjects(piece.FindPropertyRelative("bare"), Pieces[i].bare.Select(n => FindChild(body, n)));
                    }

                    so.FindProperty("_head").objectReferenceValue = head;
                    so.FindProperty("_chest").objectReferenceValue = Bone(body, "UpperChest");
                    so.FindProperty("_handLeft").objectReferenceValue = Bone(body, "LeftHand");
                    so.FindProperty("_handRight").objectReferenceValue = Bone(body, "RightHand");
                    so.FindProperty("_kneeLeft").objectReferenceValue = Bone(body, "LeftLowerLeg");
                    so.FindProperty("_kneeRight").objectReferenceValue = Bone(body, "RightLowerLeg");
                    so.FindProperty("_footLeft").objectReferenceValue = Bone(body, "LeftFoot");
                    so.FindProperty("_footRight").objectReferenceValue = Bone(body, "RightFoot");
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                // The head is hidden from the player's own eyes, which leaves the neck open: looking down, the
                // player would see into the hollow body. A dark disc inside the shoulders closes that view; from
                // outside (the mirror) it is hidden by the collar and the neck.
                Transform neck = Bone(body, "Neck");
                var cap = new GameObject("Body_NeckCap");
                cap.transform.SetParent(neck, false);
                cap.transform.position = neck.position + new Vector3(0f, -0.035f, 0f);
                cap.transform.rotation = Quaternion.identity;
                cap.AddComponent<MeshFilter>().sharedMesh = NeckCapMesh();
                var capRenderer = cap.AddComponent<MeshRenderer>();
                capRenderer.sharedMaterial = NeckCapMaterial();
                capRenderer.shadowCastingMode = ShadowCastingMode.Off;
                capRenderer.receiveShadows = false;
                capRenderer.lightProbeUsage = LightProbeUsage.Off;

                var anchor = body.AddComponent<PlayerBodyAnchor>();
                using (var so = new SerializedObject(anchor))
                {
                    so.FindProperty("_headBone").objectReferenceValue = head;
                    so.FindProperty("_headFront").objectReferenceValue = Bone(body, "HeadFront");
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                string[] missingPieces = Pieces.SelectMany(p => p.worn.Concat(p.bare)).Where(n => FindChild(body, n) == null).ToArray();
                if (missingPieces.Length > 0)
                {
                    log.AppendLine("  WARNING pieces missing in the model: " + string.Join(", ", missingPieces));
                }

                // Start in everyday clothes.
                foreach ((OutfitPart _, string[] worn, string[] bare) in Pieces)
                {
                    foreach (string n in worn) FindChild(body, n)?.SetActive(false);
                    foreach (string n in bare) FindChild(body, n)?.SetActive(true);
                }

                NBM500UVRSetup.EnsureFolder(AssetRoot);
                PrefabUtility.SaveAsPrefabAsset(body, PrefabPath);
                int renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                log.AppendLine($"  saved {PrefabPath} ({renderers} skinned pieces, setup v{SetupVersion})");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            ConfigureBodyTracking(log);
            return log.ToString();
        }

        // The automatic mapping places finger joints by position, which blends neighbouring joints into the first
        // finger bone and hangs the thumb's base on the index finger. Every finger bone follows exactly one
        // joint of the tracked hand instead: knuckle, middle joint, last joint, tip.
        private static void FixFingerMapping(string configPath, StringBuilder log)
        {
            JObject root = JObject.Parse(File.ReadAllText(configPath));
            var sourceJoints = new HashSet<string>(root["source"]["joints"].Select(j => (string)j));
            string[] thumbParts = { "Metacarpal", "Proximal", "Distal", "Tip" };
            string[] fingerParts = { "Proximal", "Intermediate", "Distal", "Tip" };
            int changed = 0, missing = 0;
            foreach (string range in new[] { "min", "max" })
            {
                var map = (JObject)root["mapping"][range];
                foreach (string side in new[] { "Left", "Right" })
                {
                    foreach (string finger in FingerNames)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            string target = $"{side}Hand{finger}{i + 1}";
                            string source = side + "Hand" + (finger == "Pinky" ? "LittlePinky" : finger) +
                                            (finger == "Thumb" ? thumbParts[i] : fingerParts[i]);
                            if (map[target] is JObject entry && sourceJoints.Contains(source))
                            {
                                entry["mappings"] = new JObject
                                {
                                    [source] = new JObject { ["weightPosition"] = 1.0, ["weightRotation"] = 1.0 },
                                };
                                changed++;
                            }
                            else
                            {
                                missing++;
                            }
                        }
                    }
                }
            }

            File.WriteAllText(configPath, root.ToString(Formatting.Indented));
            AssetDatabase.ImportAsset(configPath, ImportAssetOptions.ForceUpdate);
            log.AppendLine($"  finger mapping: {changed} entries set one to one, {missing} not found");
        }

        private static void ConfigureModel(StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Model not found: " + ModelPath);
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;

            // The bone map of an earlier import is kept in the .meta file and goes stale when the model's
            // bones are renamed or added (fingers): let Unity map the skeleton again.
            HumanDescription description = importer.humanDescription;
            description.human = new HumanBone[0];
            description.skeleton = new SkeletonBone[0];
            importer.humanDescription = description;
            importer.autoGenerateAvatarMappingIfUnspecified = true;
            importer.SaveAndReimport();

            string folder = AssetRoot + "/Materials";
            NBM500UVRSetup.EnsureFolder(folder);
            int extracted = 0, shared = 0;
            foreach (Material m in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
            {
                string target = $"{folder}/{m.name}.mat";
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
                AssetDatabase.WriteImportSettingsIfDirty(ModelPath);
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            }

            log.AppendLine($"  model imported as Humanoid; {extracted} materials extracted, {shared} shared");
        }

        // Project-wide switches body tracking needs (full body with generated legs, high fidelity).
        private static void ConfigureBodyTracking(StringBuilder log)
        {
            var projectConfig = AssetDatabase.LoadMainAssetAtPath("Assets/Oculus/OculusProjectConfig.asset");
            if (projectConfig != null)
            {
                using var so = new SerializedObject(projectConfig);
                SerializedProperty support = so.FindProperty("bodyTrackingSupport");
                if (support != null && support.intValue == 0)
                {
                    support.intValue = 1;       // Supported: the app also runs without body tracking
                    so.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssetIfDirty(projectConfig);
                }

                log.AppendLine($"  body tracking support: {(support != null ? support.intValue.ToString() : "property not found")} (1 = Supported)");
            }
            else
            {
                log.AppendLine("  WARNING Assets/Oculus/OculusProjectConfig.asset not found");
            }

            OVRRuntimeSettings runtime = OVRRuntimeSettings.Instance;
            runtime.BodyTrackingFidelity = OVRPlugin.BodyTrackingFidelity2.High;
            runtime.BodyTrackingJointSet = OVRPlugin.BodyJointSet.FullBody;
            OVRRuntimeSettings.CommitRuntimeSettings(runtime);
            log.AppendLine($"  body tracking: fidelity {runtime.BodyTrackingFidelity}, joint set {runtime.BodyTrackingJointSet}");
        }

        // ------------------------------------------------------------ scene hooks

        /// <summary>Puts the player body under the camera rig and prepares the rig for it.</summary>
        internal static GameObject AddToRig(GameObject rig, StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                log.Append(BuildPrefab());
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            // The body stays at the scene root with an identity transform, NOT under the rig: the Movement SDK
            // hands the retargeter poses that are already in world space (tracking space applied), so any
            // transform on the character would be applied a second time.
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rig.scene);
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            int headLayer = LayerMask.NameToLayer(HeadLayerName);
            foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
            {
                camera.cullingMask &= ~(1 << headLayer);            // do not look at the inside of your own head
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            }

            var manager = rig.GetComponent<OVRManager>();
            using (var so = new SerializedObject(manager))
            {
                SerializedProperty permission = so.FindProperty("requestBodyTrackingPermissionOnStartup");
                if (permission != null)
                {
                    permission.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
                }
                else
                {
                    log.AppendLine("  WARNING OVRManager.requestBodyTrackingPermissionOnStartup not found");
                }
            }

            ConnectHands(rig, body, log);
            log.AppendLine("  full-body player added to the rig (head hidden from the rig cameras)");
            return body;
        }

        /// <summary>Re-connects the body that is already in the open scene to its camera rig (after a prefab rebuild).</summary>
        [MenuItem("Tools/Player/Connect Player Body In Open Scene")]
        private static void ConnectInOpenSceneMenu() => Debug.Log(ConnectInOpenScene());

        public static string ConnectInOpenScene()
        {
            var log = new StringBuilder("[Player] Connect player body\n");
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var outfit = Object.FindAnyObjectByType<PlayerOutfit>();
            if (rig == null || outfit == null)
            {
                log.AppendLine($"  nothing to do: rig {(rig != null ? "found" : "missing")}, body {(outfit != null ? "found" : "missing")}");
                return log.ToString();
            }

            ConnectHands(rig.gameObject, outfit.gameObject, log);
            EditorSceneManager.MarkSceneDirty(outfit.gameObject.scene);
            log.AppendLine($"  scene {outfit.gameObject.scene.path} (setup v{SetupVersion})");
            return log.ToString();
        }

        // The body's hands follow the Interaction SDK hands of the rig: the same hand that grabs and pokes
        // (with its grab poses) drives the wrist and the fingers of the body, bare or gloved.
        private static void ConnectHands(GameObject rig, GameObject body, StringBuilder log)
        {
            HandVisual left = HandVisualOf(rig, "OVRHandVisualLeft");
            HandVisual right = HandVisualOf(rig, "OVRHandVisualRight");
            if (left == null || right == null)
            {
                log.AppendLine("  WARNING the rig has no OVRHandVisualLeft / OVRHandVisualRight: fingers follow body tracking only");
                return;
            }

            var retargeter = body.GetComponent<CharacterRetargeter>();
            using (var so = new SerializedObject(retargeter))
            {
                SerializedProperty containers = so.FindProperty("_sourceProcessorContainers");
                containers.arraySize = 1;
                SerializedProperty container = containers.GetArrayElementAtIndex(0);
                container.FindPropertyRelative("_currentProcessorType").intValue = (int)SourceProcessor.ProcessorType.ISDK;
                SerializedProperty isdk = container.FindPropertyRelative("_isdkProcessor");
                isdk.FindPropertyRelative("_weight").floatValue = 1f;
                isdk.FindPropertyRelative("_cameraRig").objectReferenceValue = rig.GetComponent<OVRCameraRig>();
                isdk.FindPropertyRelative("_leftHand").objectReferenceValue = left.gameObject;
                isdk.FindPropertyRelative("_rightHand").objectReferenceValue = right.gameObject;
                isdk.FindPropertyRelative("_moveHandBackToOriginalPosition").boolValue = false;
                isdk.FindPropertyRelative("_maxDisplacementDistance").floatValue = 0.05f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(retargeter);

            var visuals = body.GetComponent<PlayerHandVisuals>();
            if (visuals == null)
            {
                visuals = body.AddComponent<PlayerHandVisuals>();
            }

            using (var so = new SerializedObject(visuals))
            {
                so.FindProperty("_wristLeft").objectReferenceValue = Bone(body, "LeftHand");
                so.FindProperty("_wristRight").objectReferenceValue = Bone(body, "RightHand");
                so.FindProperty("_visualLeft").objectReferenceValue = left;
                so.FindProperty("_visualRight").objectReferenceValue = right;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            log.AppendLine("  body hands follow the rig's Interaction SDK hands (wrist and fingers)");
        }

        private static HandVisual HandVisualOf(GameObject rig, string name)
        {
            return rig.GetComponentsInChildren<HandVisual>(true).FirstOrDefault(v => v.name == name);
        }

        /// <summary>Makes a grabbable item something the player can put on.</summary>
        internal static void MakeWearable(GameObject item, OutfitPart part)
        {
            var wearable = item.AddComponent<WearableItem>();
            using var so = new SerializedObject(wearable);
            so.FindProperty("_part").intValue = (int)part;
            so.FindProperty("_grabbable").objectReferenceValue = item.GetComponent<Grabbable>();
            so.FindProperty("_returnToRest").objectReferenceValue = item.GetComponent<NBMReturnToRest>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Turns a flat quad into a working mirror (the quad's face normal is the mirror normal).</summary>
        internal static void AddMirror(MeshRenderer surface, StringBuilder log)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MirrorMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("ConstructionVR/PlanarMirror");
                if (shader == null)
                {
                    throw new InvalidOperationException("Shader ConstructionVR/PlanarMirror not found.");
                }

                NBM500UVRSetup.EnsureFolder(AssetRoot + "/Materials");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MirrorMaterialPath);
            }

            Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
            Vector3 normal = surface.transform.TransformDirection(mesh.normals[0]).normalized;

            surface.sharedMaterial = material;
            surface.gameObject.layer = WaterLayer;
            surface.shadowCastingMode = ShadowCastingMode.Off;
            surface.receiveShadows = false;
            surface.lightProbeUsage = LightProbeUsage.Off;
            surface.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(surface.gameObject, 0);

            var go = new GameObject("Mirror");
            SceneManager.MoveGameObjectToScene(go, surface.gameObject.scene);
            go.transform.SetPositionAndRotation(surface.bounds.center, Quaternion.LookRotation(normal, Vector3.up));
            var mirror = go.AddComponent<PlanarMirror>();
            using var so = new SerializedObject(mirror);
            so.FindProperty("_surface").objectReferenceValue = surface;
            so.FindProperty("_reflectedLayers").intValue = ~(1 << WaterLayer);
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  mirror at {go.transform.position}, facing {normal}");
        }

        // ---------------------------------------------------------------- helpers

        private static Mesh NeckCapMesh()
        {
            const string path = AssetRoot + "/Models/NeckCap.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
            {
                return mesh;
            }

            const int sides = 16;
            const float radiusX = 0.11f, radiusZ = 0.085f;      // wider across the shoulders than front to back
            var vertices = new Vector3[sides + 1];
            var triangles = new int[sides * 6];                 // both faces
            vertices[0] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * radiusX, 0f, Mathf.Sin(a) * radiusZ);
                int next = (i + 1) % sides + 1;
                triangles[i * 6] = 0; triangles[i * 6 + 1] = next; triangles[i * 6 + 2] = i + 1;
                triangles[i * 6 + 3] = 0; triangles[i * 6 + 4] = i + 1; triangles[i * 6 + 5] = next;
            }

            mesh = new Mesh { name = "NeckCap", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material NeckCapMaterial()
        {
            const string path = AssetRoot + "/Materials/M_Player_NeckCap.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static int EnsureLayer(string name, int preferred)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0)
            {
                return existing;
            }

            using var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            int index = -1;
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferred).stringValue))
            {
                index = preferred;
            }
            else
            {
                for (int i = 8; i < layers.arraySize && index < 0; i++)
                {
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) index = i;
                }
            }

            if (index < 0)
            {
                throw new InvalidOperationException("No free layer for '" + name + "'.");
            }

            layers.GetArrayElementAtIndex(index).stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return index;
        }

        private static void SetLayer(GameObject root, int layer)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
        }

        private static GameObject FindChild(GameObject root, string name)
        {
            Transform t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == name);
            return t != null ? t.gameObject : null;
        }

        private static Transform Bone(GameObject root, string name)
        {
            GameObject go = FindChild(root, name);
            if (go == null)
            {
                throw new InvalidOperationException($"'{name}' not found in the player body model.");
            }

            return go.transform;
        }

        private static void SetObjects(SerializedProperty array, IEnumerable<GameObject> objects)
        {
            GameObject[] list = objects.Where(o => o != null).ToArray();
            array.arraySize = list.Length;
            for (int i = 0; i < list.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            }
        }
    }
}
