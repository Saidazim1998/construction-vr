using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// Getting around with the thumbsticks, as set in Sozlamalar → VR-qulaylik. Left stick: walk (Silliq) or
    /// aim and jump (Teleport: push forward, let go). Right stick: turn in steps (Snap) or smoothly.
    /// The camera rig itself is moved, without physics; a box keeps the player inside the walls.
    /// </summary>
    public class SimpleLocomotion : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig _rig;

        [Tooltip("World-space box (x, z) the player's head stays inside.")]
        [SerializeField] private Vector2 _areaMin = new Vector2(-4.2f, -3.2f);
        [SerializeField] private Vector2 _areaMax = new Vector2(4.2f, 3.2f);

        [SerializeField] private float _walkSpeed = 1.5f;
        [SerializeField] private float _smoothTurnSpeed = 90f;
        [SerializeField] private float _teleportRange = 6f;

        private bool _turnArmed = true;
        private bool _aiming;
        private Vector3 _teleportTarget;
        private Transform _marker;

        /// <summary>Metres walked or jumped since the scene started (for tests).</summary>
        public float Travelled { get; private set; }

        protected virtual void Awake()
        {
            if (_rig == null)
            {
                _rig = GetComponent<OVRCameraRig>();
            }
        }

        protected virtual void Update()
        {
            if (_rig == null)
            {
                return;
            }

            AppSettings settings = AppSettings.Current;
            Vector2 move = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            Vector2 turn = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
            if (RemoteGrab.StickBusy(OVRInput.Controller.LTouch)) move = Vector2.zero;     // that stick is turning a held knob
            if (RemoteGrab.StickBusy(OVRInput.Controller.RTouch)) turn = Vector2.zero;
            Transform head = _rig.centerEyeAnchor;

            // turning
            if (settings.turnMode == TurnMode.Snap)
            {
                if (_turnArmed && Mathf.Abs(turn.x) > 0.7f)
                {
                    _turnArmed = false;
                    transform.RotateAround(head.position, Vector3.up, Mathf.Sign(turn.x) * settings.turnStep);
                }
                else if (Mathf.Abs(turn.x) < 0.3f)
                {
                    _turnArmed = true;
                }
            }
            else if (Mathf.Abs(turn.x) > 0.2f)
            {
                transform.RotateAround(head.position, Vector3.up, turn.x * _smoothTurnSpeed * Time.deltaTime);
            }

            // moving
            if (settings.moveMode == MoveMode.Smooth)
            {
                HideMarker();
                if (move.sqrMagnitude > 0.04f)
                {
                    Vector3 forward = head.forward;
                    forward.y = 0f;
                    forward.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    Shift((forward * move.y + right * move.x) * (_walkSpeed * Time.deltaTime));
                }
            }
            else
            {
                Teleport(move);
            }
        }

        private void Teleport(Vector2 stick)
        {
            Transform hand = _rig.leftControllerAnchor != null ? _rig.leftControllerAnchor : _rig.leftHandAnchor;
            if (stick.y > 0.7f)
            {
                // where the controller points, on the floor; pointing level or upwards reaches the full range
                Vector3 origin = hand.position;
                Vector3 direction = hand.forward;
                float floor = transform.position.y;
                Vector3 flat = new Vector3(direction.x, 0f, direction.z);
                float reach = direction.y < -0.05f ? Mathf.Min(_teleportRange, (origin.y - floor) / -direction.y * flat.magnitude) : _teleportRange;
                Vector3 target = new Vector3(origin.x, floor, origin.z) + (flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.zero) * reach;
                _teleportTarget = Clamp(target);
                _aiming = true;
                ShowMarker(_teleportTarget);
            }
            else if (_aiming && stick.magnitude < 0.3f)
            {
                _aiming = false;
                HideMarker();
                Vector3 head = _rig.centerEyeAnchor.position;
                Shift(new Vector3(_teleportTarget.x - head.x, 0f, _teleportTarget.z - head.z));
            }
        }

        private void Shift(Vector3 delta)
        {
            Vector3 head = _rig.centerEyeAnchor.position;
            Vector3 wanted = Clamp(head + delta);
            Vector3 applied = new Vector3(wanted.x - head.x, 0f, wanted.z - head.z);
            transform.position += applied;
            Travelled += applied.magnitude;
        }

        private Vector3 Clamp(Vector3 p)
        {
            return new Vector3(Mathf.Clamp(p.x, _areaMin.x, _areaMax.x), p.y, Mathf.Clamp(p.z, _areaMin.y, _areaMax.y));
        }

        private void ShowMarker(Vector3 position)
        {
            if (_marker == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Teleport Marker";
                Destroy(go.GetComponent<Collider>());
                go.transform.localScale = new Vector3(0.5f, 0.004f, 0.5f);
                var renderer = go.GetComponent<Renderer>();
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.color = new Color(0.25f, 0.6f, 1f, 1f);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _marker = go.transform;
            }

            _marker.gameObject.SetActive(true);
            _marker.position = position + Vector3.up * 0.01f;
        }

        private void HideMarker()
        {
            if (_marker != null && _marker.gameObject.activeSelf)
            {
                _marker.gameObject.SetActive(false);
            }
        }
    }
}
