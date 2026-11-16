using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// Draws a model's meshes once more with the highlight shader: as a glow over the model where it is (the
    /// thing a hand ray points at, the control the current step is about) or as a see-through copy somewhere
    /// else (the place a carried thing has to be put). Nothing is added to the model: the meshes are submitted
    /// for the frame, so a highlight is gone the moment it is no longer asked for.
    /// </summary>
    public static class ModelGlow
    {
        private struct Part
        {
            public Transform Transform;
            public Renderer Renderer;
            public Mesh Mesh;
        }

        private class Model
        {
            public Transform Root;
            public Part[] Parts;
            public float BuiltAt;
        }

        private static readonly Dictionary<Transform, Model> s_models = new Dictionary<Transform, Model>();
        private static Material s_hover, s_focus, s_ghost;

        /// <summary>The thing the hand ray points at or holds from afar: a cool white glow.</summary>
        public static Material Hover => s_hover != null ? s_hover : s_hover = Make(new Color(0.82f, 0.93f, 1f, 1f), 0.16f, 0.95f, 2f, 0.0015f, 0f);

        /// <summary>The thing the current step is about: the guide arrow's green, breathing.</summary>
        public static Material Focus => s_focus != null ? s_focus : s_focus = Make(new Color(0.18f, 0.83f, 0.63f, 1f), 0.14f, 0.9f, 2f, 0.0015f, 0.6f);

        /// <summary>The copy of a carried thing at the place it goes: the interface's blue, half see-through.</summary>
        public static Material Ghost => s_ghost != null ? s_ghost : s_ghost = Make(new Color(0.38f, 0.67f, 1f, 1f), 0.34f, 0.6f, 1.6f, 0f, 0.35f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_models.Clear();
            s_hover = s_focus = s_ghost = null;
        }

        /// <summary>Draws the glow over the model, where it is.</summary>
        public static void Draw(Transform root, Material material)
        {
            Model model = Find(root);
            if (model == null || material == null)
            {
                return;
            }

            var parameters = Parameters(material, root);
            foreach (Part part in model.Parts)
            {
                if (part.Transform == null || part.Renderer == null || !part.Renderer.enabled || !part.Renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Matrix4x4 matrix = part.Transform.localToWorldMatrix;
                for (int i = 0; i < part.Mesh.subMeshCount; i++)
                {
                    Graphics.RenderMesh(parameters, part.Mesh, i, matrix);
                }
            }
        }

        /// <summary>Draws the model as it would look with its root at another position and rotation.</summary>
        public static void DrawAt(Transform root, Material material, Vector3 position, Quaternion rotation)
        {
            Model model = Find(root);
            if (model == null || material == null)
            {
                return;
            }

            var parameters = Parameters(material, root);
            Matrix4x4 moved = Matrix4x4.TRS(position, rotation, root.lossyScale) * root.worldToLocalMatrix;
            foreach (Part part in model.Parts)
            {
                if (part.Transform == null || part.Renderer == null || !part.Renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Matrix4x4 matrix = moved * part.Transform.localToWorldMatrix;
                for (int i = 0; i < part.Mesh.subMeshCount; i++)
                {
                    Graphics.RenderMesh(parameters, part.Mesh, i, matrix);
                }
            }
        }

        private static RenderParams Parameters(Material material, Transform root)
        {
            return new RenderParams(material)
            {
                layer = root.gameObject.layer,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
            };
        }

        private static Model Find(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            if (s_models.TryGetValue(root, out Model model) && Time.unscaledTime - model.BuiltAt < 5f)
            {
                return model;
            }

            // (looked at again every few seconds: parts may be added to or taken from a model while it is used)
            var parts = new List<Part>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null || filter.GetComponent<TMP_Text>() != null)
                {
                    continue;
                }

                parts.Add(new Part { Transform = filter.transform, Renderer = renderer, Mesh = filter.sharedMesh });
            }

            model = new Model { Root = root, Parts = parts.ToArray(), BuiltAt = Time.unscaledTime };
            s_models[root] = model;
            return model;
        }

        private static Material Make(Color color, float fill, float rim, float rimPower, float push, float pulse)
        {
            Shader shader = Resources.Load<Shader>("StereoShaders/Highlight");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            material.SetColor("_Color", color);
            material.SetFloat("_Fill", fill);
            material.SetFloat("_Rim", rim);
            material.SetFloat("_RimPower", rimPower);
            material.SetFloat("_Push", push);
            material.SetFloat("_Pulse", pulse);
            return material;
        }
    }
}
