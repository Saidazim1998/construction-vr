using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NBM500U.EditorTools
{
    /// <summary>
    /// Builds the interactive CO2 cylinder (valve hand wheel, pressure gauge, flowmeter) from
    /// CO2Cylinder.fbx. Re-run after the FBX is re-exported from Blender.
    /// </summary>
    public static class GasCylinderVRSetup
    {
        public const string FbxPath = "Assets/GasCylinder/CO2Cylinder.fbx";
        public const string MaterialFolder = "Assets/GasCylinder/Materials";
        public const string PrefabPath = "Assets/GasCylinder/CO2Cylinder_VR.prefab";
        public static readonly Vector3 ScenePosition = new Vector3(0.65f, 0f, -0.45f);

        // The valve opens over one and a half turns; the flowmeter knob over three quarters of a turn.
        private const float ValveMaxOpenAngle = 540f;
        private const float FlowKnobMaxAngle = 270f;

        [MenuItem("Tools/NBM500U/Build CO2 Cylinder Prefab")]
        private static void BuildPrefabMenu() => Debug.Log(BuildPrefab());

        public static string BuildPrefab()
        {
            var log = new StringBuilder("[GasCylinder] Build VR prefab\n");
            ConfigureModel(log);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (model == null)
            {
                throw new InvalidOperationException("Model not found: " + FbxPath);
            }

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("CO2Cylinder_VR");
                SceneManager.MoveGameObjectToScene(root, preview);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.transform.SetParent(root.transform, false);

                Dictionary<string, Transform> map = instance.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                Transform Find(string name) => map.TryGetValue(name, out Transform t)
                    ? t : throw new InvalidOperationException("Object not found in model: " + name);

                Bounds all = new Bounds(Find("Gas_Cylinder_Body").position, Vector3.zero);
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
                {
                    all.Encapsulate(r.bounds);
                }

                log.AppendLine($"  model bounds: centre {all.center.ToString("F2")} size {all.size.ToString("F2")}");

                // Valve hand wheel: grab and turn about the vertical axis; anticlockwise from above opens.
                Transform wheel = Find("Gas_Valve_Handwheel");
                int wheelAxis = ClosestAxis(wheel, Vector3.up);
                float wheelDot = Mathf.Sign(Vector3.Dot(wheel.TransformDirection(NBM500UVRSetup.AxisVector(wheelAxis)), Vector3.up));
                float valveOpenSign = -wheelDot;
                NBMKnobTwistTransformer valve = SetupTwist(wheel, wheelAxis, valveOpenSign, ValveMaxOpenAngle);

                // Flowmeter knob: turns about the axis that points to the front; anticlockwise from the front opens.
                Transform knob = Find("Gas_Flow_Knob");
                int knobAxis = ClosestAxis(knob, Vector3.back);
                float knobDot = Mathf.Sign(Vector3.Dot(knob.TransformDirection(NBM500UVRSetup.AxisVector(knobAxis)), Vector3.back));
                float knobOpenSign = -knobDot;
                NBMKnobTwistTransformer flowKnob = SetupTwist(knob, knobAxis, knobOpenSign, FlowKnobMaxAngle);

                // Gauge needle: clockwise as seen from the front raises the reading.
                Transform needle = Find("Gas_Gauge_Needle");
                int needleAxis = ClosestAxis(needle, Vector3.back);
                float needleSign = Mathf.Sign(Vector3.Dot(needle.TransformDirection(NBM500UVRSetup.AxisVector(needleAxis)), Vector3.back));

                // Check the needle direction on the geometry: a quarter of the sweep must carry the tip
                // from the lower left of the dial towards the upper left (clockwise for the viewer).
                Bounds nb = NBM500UVRSetup.MeshBounds(needle);
                Vector3 tipLocal = FarthestCorner(nb);
                Vector3 tip0 = needle.TransformDirection(tipLocal);
                Vector3 tip90 = needle.TransformDirection(
                    Quaternion.AngleAxis(90f * needleSign, NBM500UVRSetup.AxisVector(needleAxis)) * tipLocal);
                bool needleOk = tip0.x < 0f && tip0.y < 0f && tip90.x < 0f && tip90.y > 0f;
                log.AppendLine($"  needle: axis local {"XYZ"[needleAxis]}, sign {needleSign:+0;-0}; tip at rest {tip0.normalized.ToString("F2")}, " +
                               $"after a quarter turn {tip90.normalized.ToString("F2")} -> {(needleOk ? "clockwise from the front, OK" : "CHECK THE NEEDLE DIRECTION")}");

                // Flowmeter float: travels up inside the glass tube.
                Transform ball = Find("Gas_Flow_Ball");
                Bounds tube = Find("Gas_Flow_Tube").GetComponent<Renderer>().bounds;
                float travel = Mathf.Max(0.01f, tube.max.y - ball.position.y - 0.03f);
                Vector3 floatTravel = ball.parent.InverseTransformVector(Vector3.up * travel);

                var gas = root.AddComponent<NBMGasCylinder>();
                var so = new SerializedObject(gas);
                so.FindProperty("_valve").objectReferenceValue = valve;
                so.FindProperty("_valveOpenSign").floatValue = valveOpenSign;
                so.FindProperty("_needle").objectReferenceValue = needle;
                so.FindProperty("_needleAxis").vector3Value = NBM500UVRSetup.AxisVector(needleAxis);
                so.FindProperty("_needleSign").floatValue = needleSign;
                so.FindProperty("_flowKnob").objectReferenceValue = flowKnob;
                so.FindProperty("_flowKnobOpenSign").floatValue = knobOpenSign;
                so.FindProperty("_flowKnobFullAngle").floatValue = FlowKnobMaxAngle;
                so.FindProperty("_floatBall").objectReferenceValue = ball;
                so.FindProperty("_floatTravel").vector3Value = floatTravel;
                so.ApplyModifiedPropertiesWithoutUndo();

                // The cylinder itself is solid.
                Transform body = Find("Gas_Cylinder_Body");
                var capsule = body.gameObject.AddComponent<CapsuleCollider>();
                Bounds bb = NBM500UVRSetup.MeshBounds(body);
                int longAxis = bb.size.x >= bb.size.y && bb.size.x >= bb.size.z ? 0 : bb.size.y >= bb.size.z ? 1 : 2;
                capsule.direction = longAxis;
                capsule.center = bb.center;
                capsule.height = bb.size[longAxis];
                capsule.radius = bb.size[(longAxis + 1) % 3] * 0.5f;

                log.AppendLine($"  valve: axis local {"XYZ"[wheelAxis]}, opens with knob angle sign {valveOpenSign:+0;-0}, up to {ValveMaxOpenAngle} deg");
                log.AppendLine($"  flow knob: axis local {"XYZ"[knobAxis]}, opens with sign {knobOpenSign:+0;-0}; float travel {travel * 1000f:F0} mm");

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

        private static void ConfigureModel(StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Model not found: " + FbxPath);
            }

            if (!importer.bakeAxisConversion || importer.importCameras || importer.importLights ||
                importer.animationType != ModelImporterAnimationType.None)
            {
                importer.bakeAxisConversion = true;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importBlendShapes = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }

            NBM500UVRSetup.EnsureFolder(MaterialFolder);
            int extracted = 0;
            foreach (Material m in AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Material>())
            {
                string path = $"{MaterialFolder}/{m.name}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) == null && string.IsNullOrEmpty(AssetDatabase.ExtractAsset(m, path)))
                {
                    extracted++;
                }
            }

            if (extracted > 0)
            {
                AssetDatabase.WriteImportSettingsIfDirty(FbxPath);
                AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
            }

            // The gauge window and the flowmeter tube are see-through.
            var glass = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Gas_Glass.mat");
            if (glass != null && glass.HasProperty("_Surface"))
            {
                glass.SetFloat("_Surface", 1f);
                glass.SetFloat("_Blend", 0f);
                glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                glass.SetFloat("_ZWrite", 0f);
                glass.SetOverrideTag("RenderType", "Transparent");
                glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                glass.SetColor("_BaseColor", new Color(0.9f, 0.95f, 1f, 0.07f));
                glass.SetFloat("_Smoothness", 0.95f);
                EditorUtility.SetDirty(glass);
            }

            log.AppendLine($"  model import: bake axis conversion on, {extracted} materials extracted");
        }

        // A centimetre around the mesh to grab by, in metres of the world: the cylinder model is imported at a
        // scale of 100, so a margin given in the mesh's own units would be a metre wide.
        internal static Vector3 GrabMargin(Transform t)
        {
            Vector3 scale = t.lossyScale;
            const float margin = 0.01f;
            return new Vector3(margin / Mathf.Max(1e-5f, Mathf.Abs(scale.x)), margin / Mathf.Max(1e-5f, Mathf.Abs(scale.y)), margin / Mathf.Max(1e-5f, Mathf.Abs(scale.z)));
        }

        private static NBMKnobTwistTransformer SetupTwist(Transform t, int axis, float openSign, float maxOpen)
        {
            Bounds bounds = NBM500UVRSetup.MeshBounds(t);
            Rigidbody body = NBM500UVRSetup.AddKinematicBody(t.gameObject);
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size + GrabMargin(t);

            var twist = t.gameObject.AddComponent<NBMKnobTwistTransformer>();
            twist.Axis = (NBMKnobTwistTransformer.LocalAxis)axis;
            var so = new SerializedObject(twist);
            so.FindProperty("_limitAngle").boolValue = true;
            so.FindProperty("_minAngle").floatValue = openSign > 0f ? 0f : -maxOpen;
            so.FindProperty("_maxAngle").floatValue = openSign > 0f ? maxOpen : 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Grabbable grabbable = NBM500UVRSetup.AddGrabbable(t.gameObject, body, twist, null);
            NBM500UVRSetup.AddGrabInteractables(t.gameObject, body, grabbable, null);
            return twist;
        }

        // Index of the local axis of t that is most parallel to the given world direction.
        private static int ClosestAxis(Transform t, Vector3 worldDirection)
        {
            int best = 0;
            float bestDot = -1f;
            for (int i = 0; i < 3; i++)
            {
                float d = Mathf.Abs(Vector3.Dot(t.TransformDirection(NBM500UVRSetup.AxisVector(i)), worldDirection));
                if (d > bestDot)
                {
                    bestDot = d;
                    best = i;
                }
            }

            return best;
        }

        // The needle is long one way from its pivot: the bounds corner farthest from the pivot is the tip side.
        private static Vector3 FarthestCorner(Bounds b)
        {
            Vector3 best = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                if (c.sqrMagnitude > best.sqrMagnitude)
                {
                    best = c;
                }
            }

            return best;
        }
    }
}
