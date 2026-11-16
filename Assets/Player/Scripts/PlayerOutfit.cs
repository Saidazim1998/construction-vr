using System;
using UnityEngine;
using UnityEngine.Events;

namespace ConstructionVR.Player
{
    /// <summary>The pieces of protective equipment the player can put on.</summary>
    [Flags]
    public enum OutfitPart
    {
        None = 0,
        Helmet = 1,
        Jacket = 2,
        GloveLeft = 4,
        GloveRight = 8,
        BootLeft = 16,
        BootRight = 32,
        All = Helmet | Jacket | GloveLeft | GloveRight | BootLeft | BootRight,
    }

    /// <summary>
    /// The skin system of the full-body player. The body is one skeleton with two sets of skinned pieces:
    /// the everyday clothes (t-shirt, bare hands, sneakers) and the protective ones (jacket, gauntlets,
    /// boots, plus a helmet on the head bone). This component switches the pieces per <see cref="OutfitPart"/>,
    /// so the body tracking keeps animating whatever is worn. What is worn survives a scene change: the
    /// player who dressed in the engineers' room arrives dressed in the factory hall.
    /// </summary>
    public class PlayerOutfit : MonoBehaviour
    {
        [Serializable]
        public class Piece
        {
            public OutfitPart part;

            [Tooltip("Shown while the part is worn.")]
            public GameObject[] worn;

            [Tooltip("Shown while the part is not worn.")]
            public GameObject[] bare;
        }

        [SerializeField] private Piece[] _pieces = Array.Empty<Piece>();

        [Header("Bones the equipment is put on")]
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _chest;
        [SerializeField] private Transform _handLeft;
        [SerializeField] private Transform _handRight;
        [SerializeField] private Transform _kneeLeft;
        [SerializeField] private Transform _kneeRight;
        [SerializeField] private Transform _footLeft;
        [SerializeField] private Transform _footRight;

        [Tooltip("Parts worn from the start, in addition to what was put on in an earlier scene.")]
        [SerializeField] private OutfitPart _wornAtStart = OutfitPart.None;

        [SerializeField] private UnityEvent<OutfitPart> _whenChanged = new UnityEvent<OutfitPart>();

        // What the player wears, carried from one scene to the next.
        private static OutfitPart s_carried = OutfitPart.None;

        private OutfitPart _worn;
        private PlayerBodyAnchor _anchor;
        private OVRCameraRig _rig;

        /// <summary>Everything that is worn right now.</summary>
        public OutfitPart Worn => _worn;

        public bool IsFullyDressed => (_worn & OutfitPart.All) == OutfitPart.All;

        /// <summary>Raised with the new set of worn parts whenever it changes.</summary>
        public event Action<OutfitPart> WhenChanged = delegate { };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCarried() => s_carried = OutfitPart.None;

        protected virtual void Awake()
        {
            _worn = s_carried | _wornAtStart;
            s_carried = _worn;
            Apply();
        }

        public bool IsWorn(OutfitPart part) => part != OutfitPart.None && (_worn & part) == part;

        public void Wear(OutfitPart part) => SetWorn(_worn | part);

        public void TakeOff(OutfitPart part) => SetWorn(_worn & ~part);

        public void SetWorn(OutfitPart worn)
        {
            worn &= OutfitPart.All;
            if (worn == _worn)
            {
                return;
            }

            _worn = worn;
            s_carried = worn;
            Apply();
            WhenChanged(_worn);
            _whenChanged.Invoke(_worn);
        }

        /// <summary>
        /// True while the body is animated by body tracking, so that its bones are where the player's body is.
        /// Without it the places to put things on are worked out from the headset and the hands.
        /// </summary>
        public bool IsBodyTracked
        {
            get
            {
                if (_anchor == null)
                {
                    _anchor = GetComponent<PlayerBodyAnchor>();
                }

                return _anchor == null || _anchor.IsTracked;
            }
        }

        /// <summary>World position an item has to be brought to in order to put it on.</summary>
        public Vector3 WearPoint(OutfitPart part)
        {
            if (!IsBodyTracked && TryWearPointWithoutBody(part, out Vector3 point))
            {
                return point;
            }

            switch (part)
            {
                case OutfitPart.Helmet: return Position(_head) + Vector3.up * 0.10f;
                case OutfitPart.Jacket: return Position(_chest);
                case OutfitPart.GloveLeft: return Position(_handLeft);
                case OutfitPart.GloveRight: return Position(_handRight);
                case OutfitPart.BootLeft: return (Position(_kneeLeft) + Position(_footLeft)) * 0.5f;
                case OutfitPart.BootRight: return (Position(_kneeRight) + Position(_footRight)) * 0.5f;
                default: return transform.position;
            }
        }

        /// <summary>How close (metres) to <see cref="WearPoint"/> the item has to be let go.</summary>
        public float WearRadius(OutfitPart part)
        {
            switch (part)
            {
                case OutfitPart.Helmet: return 0.30f;
                case OutfitPart.Jacket: return 0.40f;
                case OutfitPart.GloveLeft:
                case OutfitPart.GloveRight: return 0.22f;
                default: return 0.45f;          // boots: bend down to shin height, no need to touch the floor
            }
        }

        private Vector3 Position(Transform bone) => bone != null ? bone.position : transform.position;

        // No body tracking: the head is at the eyes, the chest below them, the hands at the controllers (or the
        // tracked hands) and the shins a little above the floor under the head.
        private bool TryWearPointWithoutBody(OutfitPart part, out Vector3 point)
        {
            point = default;
            if (_rig == null)
            {
                _rig = FindAnyObjectByType<OVRCameraRig>();
                if (_rig == null)
                {
                    return false;
                }
            }

            Transform eyes = _rig.centerEyeAnchor;
            Vector3 right = eyes.right;
            right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            float floor = _rig.trackingSpace != null ? _rig.trackingSpace.position.y : _rig.transform.position.y;
            Vector3 shin = new Vector3(eyes.position.x, floor + 0.30f, eyes.position.z);
            switch (part)
            {
                case OutfitPart.Helmet: point = eyes.position + Vector3.up * 0.05f; return true;
                case OutfitPart.Jacket: point = eyes.position + Vector3.down * 0.35f; return true;
                case OutfitPart.GloveLeft: point = _rig.leftHandAnchor.position; return true;
                case OutfitPart.GloveRight: point = _rig.rightHandAnchor.position; return true;
                case OutfitPart.BootLeft: point = shin - right * 0.12f; return true;
                case OutfitPart.BootRight: point = shin + right * 0.12f; return true;
                default: return false;
            }
        }

        private void Apply()
        {
            foreach (Piece piece in _pieces)
            {
                bool on = IsWorn(piece.part);
                SetActive(piece.worn, on);
                SetActive(piece.bare, !on);
            }
        }

        private static void SetActive(GameObject[] objects, bool active)
        {
            if (objects == null)
            {
                return;
            }

            foreach (GameObject go in objects)
            {
                if (go != null && go.activeSelf != active)
                {
                    go.SetActive(active);
                }
            }
        }
    }
}
