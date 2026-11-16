using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ConstructionVR.Player
{
    /// <summary>
    /// A flat mirror for VR. Every frame the scene is rendered once more per eye from the eye's position
    /// reflected in the mirror plane, into one texture per eye; the mirror surface shows the texture of the
    /// eye that is looking at it, so the reflection has real depth. The mirror plane goes through this
    /// transform, facing along its forward (+Z) axis. Nothing is rendered while the viewer is behind the
    /// mirror, further away than Max Distance, or not looking at it.
    /// </summary>
    [DefaultExecutionOrder(11000)]          // after the body tracking has posed the player's body
    public class PlanarMirror : MonoBehaviour
    {
        [Tooltip("The renderer of the mirror surface. Its material must use the ConstructionVR/PlanarMirror shader.")]
        [SerializeField] private Renderer _surface;

        [Tooltip("Size in pixels of each eye's reflection texture.")]
        [SerializeField] private int _textureSize = 1024;

        [Tooltip("Metres. The reflection is not updated when the viewer is further away than this.")]
        [SerializeField] private float _maxDistance = 6f;

        [Tooltip("Layers that show up in the mirror. Leave the mirror surface's own layer out.")]
        [SerializeField] private LayerMask _reflectedLayers = ~0;

        [Tooltip("Metres. Pushes the clip plane in front of the mirror plane to hide what touches the wall.")]
        [SerializeField] private float _clipOffset = 0.01f;

        private static readonly int LeftEyeTex = Shader.PropertyToID("_LeftEyeTex");
        private static readonly int RightEyeTex = Shader.PropertyToID("_RightEyeTex");

        private Camera _mirrorCamera;
        private RenderTexture _left;
        private RenderTexture _right;
        private Material _material;

        /// <summary>Number of frames in which the reflection was rendered (for tests).</summary>
        public int RenderedFrames { get; private set; }

        protected virtual void OnDisable()
        {
            Release(ref _left);
            Release(ref _right);
            if (_mirrorCamera != null)
            {
                Destroy(_mirrorCamera.gameObject);
                _mirrorCamera = null;
            }
        }

        protected virtual void LateUpdate()
        {
            Camera eye = Camera.main;
            if (eye == null || _surface == null)
            {
                return;
            }

            Vector3 normal = transform.forward;
            Vector3 toEye = eye.transform.position - transform.position;
            if (Vector3.Dot(toEye, normal) < 0.05f || toEye.sqrMagnitude > _maxDistance * _maxDistance || !_surface.isVisible)
            {
                return;
            }

            EnsureResources(eye);
            bool stereo = eye.stereoEnabled;
            if (stereo)
            {
                Render(eye.GetStereoViewMatrix(Camera.StereoscopicEye.Left),
                    eye.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), _left);
                Render(eye.GetStereoViewMatrix(Camera.StereoscopicEye.Right),
                    eye.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), _right);
            }
            else
            {
                Render(eye.worldToCameraMatrix, eye.projectionMatrix, _left);
            }

            _material.SetTexture(LeftEyeTex, _left);
            _material.SetTexture(RightEyeTex, stereo ? _right : _left);
            RenderedFrames++;
        }

        private void Render(Matrix4x4 view, Matrix4x4 projection, RenderTexture target)
        {
            Vector3 normal = transform.forward;
            Vector3 position = transform.position;
            var plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, position));
            Matrix4x4 reflection = Reflection(plane);

            Matrix4x4 mirroredView = view * reflection;
            _mirrorCamera.worldToCameraMatrix = mirroredView;

            // Everything behind the mirror plane is cut away with an oblique near plane.
            Vector3 planePoint = mirroredView.MultiplyPoint(position + normal * _clipOffset);
            Vector3 planeNormal = mirroredView.MultiplyVector(normal).normalized;
            var clipPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, -Vector3.Dot(planePoint, planeNormal));
            _mirrorCamera.projectionMatrix = projection;
            _mirrorCamera.projectionMatrix = _mirrorCamera.CalculateObliqueMatrix(clipPlane);

            _mirrorCamera.transform.position = reflection.MultiplyPoint(view.inverse.MultiplyPoint(Vector3.zero));
            _mirrorCamera.targetTexture = target;

            // A reflection flips the winding of every triangle.
            bool wasInverted = GL.invertCulling;
            GL.invertCulling = !wasInverted;
            _mirrorCamera.Render();
            GL.invertCulling = wasInverted;
        }

        private void EnsureResources(Camera eye)
        {
            if (_material == null)
            {
                _material = _surface.material;          // this mirror's own copy
            }

            if (_left == null)
            {
                _left = NewTexture("Mirror Left");
                _right = NewTexture("Mirror Right");
            }

            if (_mirrorCamera == null)
            {
                var go = new GameObject("Mirror Camera") { hideFlags = HideFlags.HideAndDontSave };
                _mirrorCamera = go.AddComponent<Camera>();
                _mirrorCamera.enabled = false;          // rendered by hand from LateUpdate
                _mirrorCamera.stereoTargetEye = StereoTargetEyeMask.None;
                UniversalAdditionalCameraData data = _mirrorCamera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;      // the main camera's post-processing covers the mirror too
                data.renderShadows = false;
                data.allowXRRendering = false;
            }

            _mirrorCamera.clearFlags = eye.clearFlags;
            _mirrorCamera.backgroundColor = eye.backgroundColor;
            _mirrorCamera.cullingMask = _reflectedLayers;
            _mirrorCamera.allowHDR = true;
            _mirrorCamera.allowMSAA = false;
            _mirrorCamera.nearClipPlane = eye.nearClipPlane;
            _mirrorCamera.farClipPlane = eye.farClipPlane;
        }

        private RenderTexture NewTexture(string textureName)
        {
            var texture = new RenderTexture(_textureSize, _textureSize, 24, RenderTextureFormat.DefaultHDR)
            {
                name = textureName,
                antiAliasing = 1,           // a multisampled target stays black when a camera is rendered into it by hand
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.Create();
            return texture;
        }

        private static void Release(ref RenderTexture texture)
        {
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
                texture = null;
            }
        }

        private static Matrix4x4 Reflection(Vector4 plane)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = 1f - 2f * plane.x * plane.x; m.m01 = -2f * plane.x * plane.y; m.m02 = -2f * plane.x * plane.z; m.m03 = -2f * plane.w * plane.x;
            m.m10 = -2f * plane.y * plane.x; m.m11 = 1f - 2f * plane.y * plane.y; m.m12 = -2f * plane.y * plane.z; m.m13 = -2f * plane.w * plane.y;
            m.m20 = -2f * plane.z * plane.x; m.m21 = -2f * plane.z * plane.y; m.m22 = 1f - 2f * plane.z * plane.z; m.m23 = -2f * plane.w * plane.z;
            return m;
        }
    }
}
