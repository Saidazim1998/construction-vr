using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace ConstructionVR.Player
{
    /// <summary>
    /// Keeps the tracked body under the headset, and takes it out of view when there is no body tracking.
    ///
    /// On a headset the body tracking and the head pose come from the same sensors, so the body's head is
    /// already at the eyes and nothing is moved. In the Meta XR Simulator the body is a recording that is
    /// played back somewhere else in the tracking space: there the whole body is shifted so that its head
    /// stays under the eyes, which makes the mirror, the first-person view and the "put it on" zones testable
    /// without a headset.
    ///
    /// Without body tracking (Quest Link without "Developer Runtime Features", or the permission refused) the
    /// skeleton would stand frozen in its rest pose, so the body is hidden; <see cref="IsTracked"/> tells the
    /// rest of the player (the outfit, the hand visuals) to work from the headset and the hands instead.
    /// </summary>
    [DefaultExecutionOrder(10500)]          // after the retargeter has posed the body, before the mirror renders
    public class PlayerBodyAnchor : MonoBehaviour
    {
        [SerializeField] private Transform _headBone;

        [Tooltip("A bone in front of the head bone: tells which way the face looks.")]
        [SerializeField] private Transform _headFront;

        [Tooltip("Metres. How far in front of the head bone (the top of the neck) the eyes are.")]
        [SerializeField] private float _eyesForward = 0.09f;

        [Tooltip("Metres. The body is only moved when its head is further than this from the eyes (horizontally).")]
        [SerializeField] private float _maxHorizontalError = 0.35f;

        [Tooltip("Seconds without a body pose after which the body counts as not tracked.")]
        [SerializeField] private float _lostAfter = 1f;

        private Transform _eyes;
        private bool _engaged;
        private CharacterRetargeter _retargeter;
        private Renderer[] _renderers;
        private bool[] _wasEnabled;
        private bool _hidden;
        private float _lastPose = -1f;

        /// <summary>True once the body had to be moved, i.e. body tracking and head pose disagree.</summary>
        public bool IsCorrecting => _engaged;

        /// <summary>True while the body is animated by body tracking. False from the start until the first pose.</summary>
        public bool IsTracked { get; private set; }

        /// <summary>Behave as if there were no body tracking (for tests in the simulator, where there always is).</summary>
        public bool SimulateUntracked { get; set; }

        protected virtual void Awake()
        {
            _retargeter = GetComponent<CharacterRetargeter>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _wasEnabled = new bool[_renderers.Length];
        }

        protected virtual void OnDisable() => SetHidden(false);

        protected virtual void LateUpdate()
        {
            UpdateTracked();
            SetHidden(!IsTracked);
            if (!IsTracked || _headBone == null)
            {
                return;
            }

            if (_eyes == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                _eyes = rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
                if (_eyes == null)
                {
                    return;
                }
            }

            Vector3 bodyEyes = _headBone.position;
            if (_headFront != null)
            {
                Vector3 face = _headFront.position - _headBone.position;
                face.y = 0f;
                if (face.sqrMagnitude > 1e-6f)
                {
                    bodyEyes += face.normalized * _eyesForward;
                }
            }

            Vector3 error = _eyes.position - bodyEyes;
            error.y = 0f;
            if (_engaged || error.magnitude > _maxHorizontalError)
            {
                _engaged = true;
                transform.position += error;
            }
        }

        private void UpdateTracked()
        {
            // a body without a retargeter is posed by something else: leave it alone
            bool pose = _retargeter == null
                        || (_retargeter.isActiveAndEnabled && _retargeter.SkeletonRetargeter != null && _retargeter.RetargeterValid);
            if (pose && !SimulateUntracked)
            {
                _lastPose = Time.unscaledTime;
                IsTracked = true;
            }
            else if (SimulateUntracked || _lastPose < 0f || Time.unscaledTime - _lastPose > _lostAfter)
            {
                IsTracked = false;
            }
        }

        // The renderers are switched, not the objects: the outfit switches the objects of the pieces that are worn.
        private void SetHidden(bool hidden)
        {
            if (hidden == _hidden || _renderers == null)
            {
                return;
            }

            _hidden = hidden;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null)
                {
                    continue;
                }

                if (hidden)
                {
                    _wasEnabled[i] = r.enabled;
                    r.enabled = false;
                }
                else
                {
                    r.enabled = _wasEnabled[i];
                }
            }
        }
    }
}
