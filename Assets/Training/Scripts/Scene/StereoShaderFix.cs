using System.Collections.Generic;
using ConstructionVR.Training.UI;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// Makes the visuals of the Meta interaction rig show up in both eyes.
    ///
    /// With single-pass stereo rendering in this Unity version, shaders built on the legacy CG includes get no
    /// view-projection matrix for the second eye: they are drawn in the left eye only, and slightly shifted.
    /// The rays, cursors and markers of the Interaction SDK use such shaders and live in a package that cannot
    /// be edited, so this component swaps their shaders for equivalents written on the URP shader library.
    /// It installs itself when a scene starts and looks through the camera rig once a second, because the rig
    /// creates and enables visuals as controllers and hands come and go.
    /// </summary>
    [AddComponentMenu("")]
    public class StereoShaderFix : MonoBehaviour
    {
        private const float Interval = 1f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static StereoShaderFix _instance;

        private readonly Dictionary<Shader, Shader> _replacements = new Dictionary<Shader, Shader>();
        private readonly Dictionary<Material, Material> _copies = new Dictionary<Material, Material>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Material> _materials = new List<Material>();
        private Transform _rig;
        private float _next;

        /// <summary>Number of materials whose shader was swapped so far (for tests).</summary>
        public int Swapped { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("Stereo Shader Fix");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<StereoShaderFix>();
        }

        protected virtual void Awake()
        {
            Replace("Interaction/OculusRayVisual", Resources.Load<Shader>("StereoShaders/RayVisual"));
            Replace("Interaction/OculusRayCursor", Resources.Load<Shader>("StereoShaders/RayCursor"));
            Replace("UI/Default", UiStyle.ImageMaterial != null ? UiStyle.ImageMaterial.shader : null);
            Replace("Unlit/Color", Shader.Find("Universal Render Pipeline/Unlit"));
        }

        private void Replace(string legacyName, Shader replacement)
        {
            Shader legacy = Shader.Find(legacyName);        // null when nothing in the build uses it
            if (legacy != null && replacement != null && legacy != replacement)
            {
                _replacements[legacy] = replacement;
            }
        }

        protected virtual void Update()
        {
            if (Time.unscaledTime < _next || _replacements.Count == 0)
            {
                return;
            }

            _next = Time.unscaledTime + Interval;
            if (_rig == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig == null)
                {
                    return;
                }

                _rig = rig.transform;
            }

            _rig.GetComponentsInChildren(true, _renderers);
            foreach (Renderer renderer in _renderers)
            {
                renderer.GetSharedMaterials(_materials);
                bool changed = false;
                for (int i = 0; i < _materials.Count; i++)
                {
                    Material material = _materials[i];
                    if (material == null || !_replacements.TryGetValue(material.shader, out Shader replacement))
                    {
                        continue;
                    }

                    if (IsAsset(material))
                    {
                        // never touch a material asset: the change would stay in the project after Play mode
                        if (!_copies.TryGetValue(material, out Material copy) || copy == null)
                        {
                            copy = new Material(material) { name = material.name + " (Stereo)" };
                            Swap(copy, replacement);
                            _copies[material] = copy;
                        }

                        _materials[i] = copy;
                        changed = true;
                    }
                    else
                    {
                        Swap(material, replacement);        // an instance a visual made for itself and keeps writing to
                    }
                }

                if (changed)
                {
                    renderer.SetSharedMaterials(_materials);
                }
            }

            _renderers.Clear();
            _materials.Clear();
        }

        private void Swap(Material material, Shader replacement)
        {
            bool hadColor = material.HasProperty(ColorId);
            Color color = hadColor ? material.GetColor(ColorId) : Color.white;
            int queue = material.renderQueue;
            bool customQueue = queue != material.shader.renderQueue;

            material.shader = replacement;
            if (customQueue)
            {
                material.renderQueue = queue;
            }

            if (hadColor && !material.HasProperty(ColorId) && material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);      // Unlit/Color -> URP Unlit
            }

            Swapped++;
        }

        private static bool IsAsset(Material material)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.IsPersistent(material);
#else
            return false;
#endif
        }

        protected virtual void OnDestroy()
        {
            foreach (Material copy in _copies.Values)
            {
                if (copy != null)
                {
                    Destroy(copy);
                }
            }

            _copies.Clear();
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
