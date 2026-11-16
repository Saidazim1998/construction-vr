using System;
using System.IO;
using System.Linq;
using System.Text;
using ConstructionVR.Training;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ConstructionVR.EditorTools
{
    /// <summary>
    /// Puts the scenario (logic and interface) into the scenes that the scene builders have made. It only adds
    /// its own "Training" object and a locomotion component on the camera rig, so it can be run again at any
    /// time without rebuilding or re-baking a scene: Tools → Training → Install In Open Scene.
    /// </summary>
    public static partial class TrainingSceneSetup
    {
        private const string RootName = "Training";
        private const int SetupVersion = 5;      // bump to check that new code is loaded

        [MenuItem("Tools/Training/Install In Open Scene")]
        private static void InstallMenu() => Debug.Log(InstallInOpenScene());

        public static string InstallInOpenScene()
        {
            var log = new StringBuilder($"[Training] Install (setup v{SetupVersion})\n");
            EnsureTextResources(log);
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject old in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray())
            {
                Object.DestroyImmediate(old);
            }

            foreach (GameObject root in scene.GetRootGameObjects())       // what an earlier install put outside its own object
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                }

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name.StartsWith("Training_", StringComparison.Ordinal)).ToArray())
                {
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }
            }

            if (Find(scene, "Journal_Open") != null)
            {
                InstallRoom(scene, log);
            }
            else if (Find(scene, "Torch_Grabbable") != null && Find(scene, "Gas_Valve_Handwheel") != null)
            {
                InstallHall(scene, log);
            }
            else
            {
                log.AppendLine("  this scene has no part of the scenario yet: " + scene.path);
                return log.ToString();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            OrderBuildScenes(log);
            log.AppendLine("  scene " + scene.path);
            return log.ToString();
        }

        // The application starts in the engineers' room (log in); the hall is loaded from there.
        private static void OrderBuildScenes(StringBuilder log)
        {
            string[] first = { "Assets/Scenes/EngineerRoom.unity", "Assets/Scenes/Factory.unity" };
            var scenes = EditorBuildSettings.scenes.ToList();
            var ordered = first.Select(path => scenes.FirstOrDefault(s => s.path == path) ?? new EditorBuildSettingsScene(path, true)).ToList();
            foreach (EditorBuildSettingsScene s in ordered)
            {
                s.enabled = true;
            }

            ordered.AddRange(scenes.Where(s => !first.Contains(s.path)));
            if (!ordered.Select(s => s.path).SequenceEqual(scenes.Select(s => s.path)))
            {
                EditorBuildSettings.scenes = ordered.ToArray();
                log.AppendLine("  build scenes: " + string.Join(", ", ordered.Select(s => Path.GetFileNameWithoutExtension(s.path))));
            }
        }

        // ------------------------------------------------------------------ engineers' room

        private static void InstallRoom(Scene scene, StringBuilder log)
        {
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var director = root.AddComponent<RoomDirector>();

            // The learner starts facing the main window; the journal desk is then on the left, the stand with
            // the protective equipment behind, the door to the hall across the room.
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            if (rig == null)
            {
                throw new InvalidOperationException("The scene has no OVRCameraRig.");
            }

            rig.transform.rotation = Quaternion.Euler(0f, 90f, 0f);          // east: the only direction with free space for the window
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig.transform);
            Vector3 start = rig.transform.position;

            Transform hub = Anchor(root, "Hub Anchor", new Vector3(start.x + 2.4f, 1.62f, start.z), Vector3.right);

            Transform journal = Find(scene, "Journal_Open");
            Bounds page = journal.GetComponent<Renderer>().bounds;
            // the panel stands behind the journal against the wall and looks into the room (+X)
            Transform journalPanel = Anchor(root, "Journal Panel Anchor", new Vector3(page.min.x - 0.22f, page.max.y + 0.52f, page.center.z + 0.16f), Vector3.left);

            // signature field: near edge of the right-hand page as seen from the room
            var zone = new GameObject("Sign Zone").transform;
            zone.SetParent(root.transform, false);
            zone.position = new Vector3(page.max.x - 0.055f, page.max.y, page.center.z + 0.11f);
            zone.localScale = new Vector3(0.07f, 0.05f, 0.16f);

            Transform pen = Find(scene, "Journal_Pen");
            if (pen == null || pen.GetComponent<Grabbable>() == null)
            {
                throw new InvalidOperationException("Journal_Pen with a Grabbable was not found: build the room scene first.");
            }

            // the door to the hall is in the east wall (see D:\Blender\EngineerRoom\build_room.py)
            Transform door = Anchor(root, "Door Panel Anchor", new Vector3(4.40f, 1.5f, -1.0f), Vector3.right);

            using (var so = new SerializedObject(director))
            {
                so.FindProperty("_hubAnchor").objectReferenceValue = hub;
                so.FindProperty("_journalPanelAnchor").objectReferenceValue = journalPanel;
                so.FindProperty("_signZone").objectReferenceValue = zone;
                so.FindProperty("_pen").objectReferenceValue = pen.GetComponent<Grabbable>();
                so.FindProperty("_penMesh").objectReferenceValue = pen.GetComponentInChildren<MeshFilter>();
                so.FindProperty("_doorPanelAnchor").objectReferenceValue = door;
                so.FindProperty("_hallScene").stringValue = "Factory";
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AddLocomotion(rig, new Vector2(-4.2f, -3.2f), new Vector2(4.2f, 3.2f));
            log.AppendLine($"  room: hub at {hub.position}, journal panel at {journalPanel.position}, signature field at {zone.position}, door at {door.position}");
        }

        // ------------------------------------------------------------------ helpers

        private static void AddLocomotion(OVRCameraRig rig, Vector2 min, Vector2 max)
        {
            var locomotion = rig.GetComponent<SimpleLocomotion>();
            if (locomotion == null)
            {
                locomotion = rig.gameObject.AddComponent<SimpleLocomotion>();
            }

            using (var so = new SerializedObject(locomotion))
            {
                so.FindProperty("_rig").objectReferenceValue = rig;
                so.FindProperty("_areaMin").vector2Value = min;
                so.FindProperty("_areaMax").vector2Value = max;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(locomotion);
        }

        private static Transform Find(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Transform t = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == name);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>The interface uses TextMesh Pro, whose default font and settings come as a package that is imported once.</summary>
        private static void EnsureTextResources(StringBuilder log)
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
            {
                return;
            }

            string package = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(package))
            {
                throw new InvalidOperationException("TMP Essential Resources.unitypackage not found in com.unity.ugui.");
            }

            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
            log.AppendLine("  TextMesh Pro essential resources imported");
        }
    }
}
