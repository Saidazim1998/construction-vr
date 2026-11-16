using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Surfaces;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NBM500U.EditorTools
{
    /// <summary>
    /// Builds the interactive VR version of the NBM-500U welder from NBM500U.fbx with
    /// Meta XR Interaction SDK components, and a test scene around it.
    /// Re-run after the FBX is re-exported: the prefab and the scene are rebuilt from scratch.
    /// </summary>
    public static class NBM500UVRSetup
    {
        public const string FbxPath = "Assets/NBM500U/NBM500U.fbx";
        public const string PrefabFolder = "Assets/NBM500U/Prefabs";
        public const string PrefabPath = PrefabFolder + "/NBM500U_VR.prefab";
        public const string ScenePath = "Assets/Scenes/NBM500U_VR.unity";

        private const string CameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string InteractionRigPrefab =
            "Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/OVRComprehensiveInteractionRig.prefab";

        // Artificial locomotion (thumbstick / teleport) of Meta's comprehensive rig. Off by default:
        // the user walks around the machine in room scale. With it on, the rig's physics capsule fell
        // through the floor during a hand-tracking glitch in the Meta XR Simulator, with no way back.
        private const bool EnableLocomotion = false;

        // How far a button cap travels before it registers as pressed.
        private const float ButtonTravel = 0.002f;
        // Extra width of the pokeable area around a button cap (kept small: caps are 21 mm apart).
        private const float ButtonMargin = 0.004f;

        public static readonly string[] WireFeederButtons = { "WF_Button_01", "WF_Button_02", "WF_Button_03" };

        public static readonly string[] Knobs =
        {
            "PS_EncoderKnob", "WF_Knob_Current", "WF_Knob_Voltage",
            "WF_Drive_TensionKnob_Front", "WF_Drive_TensionKnob_Rear"
        };

        public static readonly string[] TorchParts =
        {
            "Torch_Handle", "Torch_Trigger", "Torch_CableSupport", "Torch_Neck",
            "Torch_NozzleInsulator", "Torch_GasNozzle", "Torch_ContactTip"
        };

        // Hinged parts: opening angle in degrees about the part's Unity-local Y axis.
        // The brief gives -100 and +110 about the Blender-local Y axis. After Bake Axis Conversion the
        // local Y axis is unchanged but local Z is mirrored, which reverses the sense of rotation.
        private static readonly (string name, float openAngle)[] Hinges =
        {
            ("WF_Door", 100f),
            ("WF_SpoolCover", -110f)
        };

        private static readonly string[] StaticBodies = { "PS_Housing", "Cooler_Housing", "WF_Chassis" };

        [MenuItem("Tools/NBM500U/Build VR Prefab")]
        private static void BuildPrefabMenu() => Debug.Log(BuildPrefab());

        [MenuItem("Tools/NBM500U/Build VR Scene")]
        private static void BuildSceneMenu() => Debug.Log(BuildScene());

        [MenuItem("Tools/NBM500U/Build VR Prefab and Scene")]
        private static void BuildAllMenu() => Debug.Log(BuildPrefab() + "\n" + BuildScene());

        // ------------------------------------------------------------------ prefab

        public static string BuildPrefab()
        {
            var log = new StringBuilder("[NBM500U] Build VR prefab\n");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (model == null)
            {
                throw new InvalidOperationException("Model not found: " + FbxPath);
            }

            // Work in a preview scene so the open scene is not touched.
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("NBM500U_VR");
                SceneManager.MoveGameObjectToScene(root, preview);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.transform.SetParent(root.transform, false);

                var map = new Dictionary<string, Transform>();
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (map.ContainsKey(t.name))
                    {
                        log.AppendLine("  WARNING duplicate object name: " + t.name);
                    }
                    else
                    {
                        map.Add(t.name, t);
                    }
                }

                Transform Find(string name)
                {
                    if (!map.TryGetValue(name, out Transform t))
                    {
                        throw new InvalidOperationException("Object not found in model: " + name);
                    }

                    return t;
                }

                // Buttons (poke)
                Transform panel = Find("PS_ControlPanel");
                List<Transform> buttons = panel.Cast<Transform>()
                    .Where(t => t.name.StartsWith("PS_Btn_", StringComparison.Ordinal))
                    .OrderBy(t => t.name, StringComparer.Ordinal).ToList();
                if (buttons.Count != 12)
                {
                    log.AppendLine($"  WARNING expected 12 PS_Btn_* under PS_ControlPanel, found {buttons.Count}");
                }

                buttons.AddRange(WireFeederButtons.Select(Find));
                foreach (Transform button in buttons)
                {
                    SetupButton(button, log);
                }

                log.AppendLine($"  buttons: {buttons.Count}");

                // Knobs (grab and turn about their own axis)
                foreach (string name in Knobs)
                {
                    SetupKnob(Find(name), log);
                }

                // Hinged parts (grab and swing)
                Transform chassis = Find("WF_Chassis");
                foreach ((string name, float openAngle) in Hinges)
                {
                    SetupHinge(Find(name), openAngle, chassis, log);
                }

                // Torch (grab and carry)
                SetupTorch(Find("Torch_MIG"), TorchParts.Select(Find).ToList(), Find("Torch_Handle"), log);

                // Solid bodies
                foreach (string name in StaticBodies)
                {
                    Transform body = Find(name);
                    if (body.GetComponent<Collider>() == null)
                    {
                        body.gameObject.AddComponent<BoxCollider>();
                    }
                }

                EnsureFolder(PrefabFolder);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                log.AppendLine(success ? "  saved " + PrefabPath : "  ERROR prefab was not saved");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static void SetupButton(Transform button, StringBuilder log)
        {
            Bounds bounds = MeshBounds(button);
            int thin = ThinAxis(bounds.size);

            // The cap is flat: its thin axis is the panel normal. The front of the machine faces -Z
            // in model space, so of the two directions take the one that points to the front.
            Vector3 outward = button.TransformDirection(AxisVector(thin)).normalized;
            if (Vector3.Dot(outward, Vector3.back) < 0f)
            {
                outward = -outward;
            }

            Vector3 center = button.TransformPoint(bounds.center);

            // The surface is both the pokeable plane and the fully-pressed limit of the cap.
            // Its +Z points into the panel (PlaneSurface faces "Backward", i.e. towards the user).
            var surface = new GameObject(button.name + "_Surface");
            surface.transform.SetParent(button.parent, false);
            Vector3 upHint = Mathf.Abs(Vector3.Dot(outward, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            surface.transform.SetPositionAndRotation(center - outward * ButtonTravel,
                Quaternion.LookRotation(-outward, upHint));

            // Footprint of the cap in the plane of the surface.
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            foreach (Vector3 corner in Corners(bounds))
            {
                Vector3 p = surface.transform.InverseTransformPoint(button.TransformPoint(corner));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            var plane = surface.AddComponent<PlaneSurface>();
            var clipper = surface.AddComponent<BoundsClipper>();
            clipper.Position = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);
            clipper.Size = new Vector3(max.x - min.x + ButtonMargin, max.y - min.y + ButtonMargin, 0.02f);

            var clipped = surface.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { clipper });

            var poke = surface.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);

            var visual = button.gameObject.AddComponent<PokeInteractableVisual>();
            visual.InjectAllPokeInteractableVisual(poke, surface.transform);

            var nbmButton = surface.AddComponent<NBMButton>();
            var so = new SerializedObject(nbmButton);
            so.FindProperty("_buttonId").stringValue = button.name;
            so.FindProperty("_pokeInteractable").objectReferenceValue = poke;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetupKnob(Transform knob, StringBuilder log)
        {
            Bounds bounds = MeshBounds(knob);
            int axis = OddAxis(bounds.size);

            Rigidbody body = AddKinematicBody(knob.gameObject);
            var box = knob.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size + Vector3.one * 0.01f;

            var twist = knob.gameObject.AddComponent<NBMKnobTwistTransformer>();
            twist.Axis = (NBMKnobTwistTransformer.LocalAxis)axis;

            Grabbable grabbable = AddGrabbable(knob.gameObject, body, twist, null);
            AddGrabInteractables(knob.gameObject, body, grabbable, null);

            log.AppendLine($"  knob {knob.name}: turns about local {"XYZ"[axis]} " +
                           $"(world {knob.TransformDirection(AxisVector(axis)).ToString("F2")})");
        }

        private static void SetupHinge(Transform hinge, float openAngle, Transform solidBody, StringBuilder log)
        {
            Bounds bounds = MeshBounds(hinge);

            Rigidbody body = AddKinematicBody(hinge.gameObject);
            var box = hinge.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.03f);

            var rotate = hinge.gameObject.AddComponent<OneGrabRotateTransformer>();
            rotate.InjectOptionalRotationAxis(OneGrabRotateTransformer.Axis.Up);
            rotate.InjectOptionalConstraints(new OneGrabRotateTransformer.OneGrabRotateConstraints
            {
                MinAngle = new FloatConstraint { Constrain = true, Value = Mathf.Min(0f, openAngle) },
                MaxAngle = new FloatConstraint { Constrain = true, Value = Mathf.Max(0f, openAngle) }
            });

            Grabbable grabbable = AddGrabbable(hinge.gameObject, body, rotate, null);
            AddGrabInteractables(hinge.gameObject, body, grabbable, null);

            // Check the opening direction against the geometry: swinging open must not pass
            // through the solid half of the wire feeder, swinging the other way must.
            Bounds solid = solidBody.GetComponent<Renderer>().bounds;
            solid.Expand(-0.01f);
            bool clear = !SweepHits(hinge, bounds, openAngle, solid);
            bool oppositeHits = SweepHits(hinge, bounds, -openAngle, solid);
            log.AppendLine($"  hinge {hinge.name}: {openAngle:+0;-0} deg about local Y " +
                           $"(world {hinge.TransformDirection(Vector3.up).ToString("F2")}); " +
                           $"opening sweep clear of {solidBody.name}: {(clear ? "yes" : "NO")}; " +
                           $"opposite direction collides: {(oppositeHits ? "yes" : "NO")}" +
                           (clear && oppositeHits ? "" : "  <-- CHECK THE HINGE DIRECTION"));
        }

        private static bool SweepHits(Transform hinge, Bounds localBounds, float angle, Bounds solidWorld)
        {
            Vector3 axis = hinge.TransformDirection(Vector3.up);
            Vector3 pivot = hinge.position;
            const int steps = 20;
            for (int step = 1; step <= steps; step++)
            {
                Quaternion q = Quaternion.AngleAxis(angle * step / steps, axis);
                for (int ix = -1; ix <= 1; ix++)
                for (int iy = -1; iy <= 1; iy++)
                for (int iz = -1; iz <= 1; iz++)
                {
                    Vector3 local = localBounds.center + Vector3.Scale(localBounds.extents * 0.8f,
                        new Vector3(ix, iy, iz));
                    Vector3 world = pivot + q * (hinge.TransformPoint(local) - pivot);
                    if (solidWorld.Contains(world))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void SetupTorch(Transform torchRoot, List<Transform> parts, Transform handle,
            StringBuilder log)
        {
            // One rigid body for the hand-held part. Cable, euro nut and euro plug stay behind.
            var torch = new GameObject("Torch_Grabbable");
            torch.transform.SetParent(torchRoot, false);
            torch.transform.SetPositionAndRotation(handle.position, handle.rotation);
            foreach (Transform part in parts)
            {
                part.SetParent(torch.transform, true);
            }

            Rigidbody body = AddKinematicBody(torch);

            // Only the handle has a collider, so the handle is the only place the torch can be grabbed.
            Bounds bounds = MeshBounds(handle);
            int longAxis = LongAxis(bounds.size);
            var capsule = handle.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = longAxis;
            capsule.center = bounds.center;
            capsule.height = bounds.size[longAxis];
            capsule.radius = Mathf.Max(bounds.size[(longAxis + 1) % 3], bounds.size[(longAxis + 2) % 3]) * 0.5f +
                             0.005f;

            var free = torch.AddComponent<GrabFreeTransformer>();
            Grabbable grabbable = AddGrabbable(torch, body, free, free);
            AddGrabInteractables(torch, body, grabbable, handle);

            var rest = torch.AddComponent<NBMReturnToRest>();
            var so = new SerializedObject(rest);
            so.FindProperty("_grabbable").objectReferenceValue = grabbable;
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine($"  torch: {parts.Count} parts under {torch.name}, grab collider on {handle.name} " +
                           $"(capsule r={capsule.radius:F3} h={capsule.height:F3})");
        }

        internal static Rigidbody AddKinematicBody(GameObject go)
        {
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return body;
        }

        internal static Grabbable AddGrabbable(GameObject go, Rigidbody body, ITransformer oneGrab,
            ITransformer twoGrab)
        {
            var grabbable = go.AddComponent<Grabbable>();
            grabbable.InjectOptionalTargetTransform(go.transform);
            grabbable.InjectOptionalRigidbody(body);
            grabbable.InjectOptionalOneGrabTransformer(oneGrab);
            if (twoGrab != null)
            {
                grabbable.InjectOptionalTwoGrabTransformer(twoGrab);
            }
            else
            {
                grabbable.MaxGrabPoints = 1;
            }

            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(true);
            return grabbable;
        }

        // Controllers use GrabInteractable, tracked hands use HandGrabInteractable.
        internal static void AddGrabInteractables(GameObject go, Rigidbody body, Grabbable grabbable,
            Transform grabSource)
        {
            var grab = go.AddComponent<GrabInteractable>();
            grab.InjectAllGrabInteractable(body);
            grab.InjectOptionalPointableElement(grabbable);
            if (grabSource != null)
            {
                grab.InjectOptionalGrabSource(grabSource);
            }

            var handGrab = go.AddComponent<HandGrabInteractable>();
            handGrab.InjectAllHandGrabInteractable(GrabTypeFlags.All, body,
                GrabbingRule.DefaultPinchRule, GrabbingRule.DefaultPalmRule);
            handGrab.InjectOptionalPointableElement(grabbable);
        }

        // ------------------------------------------------------------------- scene

        public static string BuildScene()
        {
            var log = new StringBuilder("[NBM500U] Build VR scene\n");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException(
                        "An open scene has unsaved changes. Save or discard them, then run this again.");
                }
            }

            var machinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (machinePrefab == null)
            {
                log.Append(BuildPrefab());
                machinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, 20f, 0f);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";

            // The machine stands at the origin, its front (control panels) faces -Z.
            var machine = (GameObject)PrefabUtility.InstantiatePrefab(machinePrefab, scene);
            machine.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The CO2 cylinder stands on the floor to the right of the machine, regulator towards the user.
            var cylinderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GasCylinderVRSetup.PrefabPath);
            if (cylinderPrefab != null)
            {
                var cylinder = (GameObject)PrefabUtility.InstantiatePrefab(cylinderPrefab, scene);
                cylinder.transform.SetPositionAndRotation(GasCylinderVRSetup.ScenePosition, Quaternion.identity);
                log.AppendLine("  CO2 cylinder at " + GasCylinderVRSetup.ScenePosition);
            }

            // The player starts one metre in front of the machine, looking at it.
            AddPlayerRig(scene, new Vector3(0f, 0f, -1f), Quaternion.identity, log);

            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine("  saved " + ScenePath);

            List<EditorBuildSettingsScene> buildScenes = EditorBuildSettings.scenes
                .Where(s => s.path != ScenePath).ToList();
            buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            log.AppendLine("  build scenes: " + string.Join(", ", buildScenes.Select(s => s.path)));
            return log.ToString();
        }

        /// <summary>
        /// Adds the Meta camera rig with the comprehensive interaction rig (hands and controllers) to
        /// <paramref name="scene"/> and wires it. Shared by every scene builder of the project.
        /// </summary>
        internal static GameObject AddPlayerRig(Scene scene, Vector3 position, Quaternion rotation, StringBuilder log)
        {
            var cameraRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefab);
            var interactionRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InteractionRigPrefab);
            if (cameraRigPrefab == null || interactionRigPrefab == null)
            {
                throw new InvalidOperationException(
                    "Meta XR prefabs not found. Are Meta XR Core SDK and Interaction SDK installed?");
            }

            var cameraRig = (GameObject)PrefabUtility.InstantiatePrefab(cameraRigPrefab, scene);
            cameraRig.transform.SetPositionAndRotation(position, rotation);

            var interactionRig = (GameObject)PrefabUtility.InstantiatePrefab(interactionRigPrefab, scene);
            interactionRig.transform.SetParent(cameraRig.transform, false);

            // Meta's own editor auto-wiring. It normally runs when a rig prefab is dropped into the
            // Hierarchy by hand; a scripted build has to ask for it. It fills the rig's unassigned
            // references (camera rig, centre eye camera, player origin, ...).
            Oculus.Interaction.Editor.UnityObjectAddedBroadcaster.HandleObjectWasAdded(interactionRig);
            WireRig(cameraRig, interactionRig, log);

            Transform locomotor = interactionRig.transform.Find("Locomotor");
            if (locomotor != null)
            {
                locomotor.gameObject.SetActive(EnableLocomotion);
                PrefabUtility.RecordPrefabInstancePropertyModifications(locomotor.gameObject);
                log.AppendLine("  Locomotor active: " + EnableLocomotion);
            }
            else
            {
                log.AppendLine("  WARNING Locomotor object not found in the interaction rig");
            }

            // Make sure the wired references are stored as overrides of the prefab instance.
            Undo.FlushUndoRecordObjects();
            foreach (MonoBehaviour behaviour in interactionRig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
                }
            }

            return cameraRig;
        }

        // Fallback for references Meta's auto-wiring leaves unassigned. Only fills empty fields.
        private static void WireRig(GameObject cameraRig, GameObject interactionRig, StringBuilder log)
        {
            var ovrCameraRig = cameraRig.GetComponent<OVRCameraRig>();
            var ovrManager = cameraRig.GetComponent<OVRManager>();

            var managerObject = new SerializedObject(ovrManager);
            SerializedProperty origin = managerObject.FindProperty("_trackingOriginType");
            if (origin != null)
            {
                origin.intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
                managerObject.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (MonoBehaviour behaviour in interactionRig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                string type = behaviour.GetType().Name;
                if (type == "OVRCameraRigRef")
                {
                    var so = new SerializedObject(behaviour);
                    SerializedProperty rig = so.FindProperty("_ovrCameraRig");
                    if (rig != null && rig.objectReferenceValue == null)
                    {
                        rig.objectReferenceValue = ovrCameraRig;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        log.AppendLine("  OVRCameraRigRef -> " + cameraRig.name);
                    }
                }
                else if (type == "TunnelingEffect")
                {
                    var so = new SerializedObject(behaviour);
                    SerializedProperty camera = so.FindProperty("_centerEyeCamera");
                    if (camera != null && camera.objectReferenceValue == null)
                    {
                        camera.objectReferenceValue = ovrCameraRig.centerEyeAnchor.GetComponent<Camera>();
                        so.ApplyModifiedPropertiesWithoutUndo();
                        log.AppendLine($"  {behaviour.name}.TunnelingEffect camera -> CenterEyeAnchor");
                    }
                }
                else if (type == "FirstPersonLocomotor")
                {
                    var so = new SerializedObject(behaviour);
                    SerializedProperty playerOrigin = so.FindProperty("_playerOrigin");
                    SerializedProperty playerEyes = so.FindProperty("_playerEyes");
                    if (playerOrigin != null && playerEyes != null &&
                        playerOrigin.objectReferenceValue == null && playerEyes.objectReferenceValue == null)
                    {
                        playerOrigin.objectReferenceValue = cameraRig.transform;
                        playerEyes.objectReferenceValue = ovrCameraRig.centerEyeAnchor;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        log.AppendLine("  FirstPersonLocomotor origin/eyes -> OVRCameraRig / CenterEyeAnchor");
                    }
                }
            }
        }

        // ----------------------------------------------------------------- helpers

        internal static Bounds MeshBounds(Transform t)
        {
            var filter = t.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                throw new InvalidOperationException(t.name + " has no mesh.");
            }

            return filter.sharedMesh.bounds;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
            {
                yield return b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            }
        }

        internal static Vector3 AxisVector(int axis)
        {
            Vector3 v = Vector3.zero;
            v[axis] = 1f;
            return v;
        }

        private static int ThinAxis(Vector3 size)
        {
            return size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        }

        private static int LongAxis(Vector3 size)
        {
            return size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        }

        // A knob is round: two of its dimensions are equal. The axis it turns about is the third.
        private static int OddAxis(Vector3 size)
        {
            float xy = Mathf.Abs(size.x - size.y);
            float yz = Mathf.Abs(size.y - size.z);
            float xz = Mathf.Abs(size.x - size.z);
            return yz <= xy && yz <= xz ? 0 : xz <= xy && xz <= yz ? 1 : 2;
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }
    }
}
