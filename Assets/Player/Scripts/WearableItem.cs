using NBM500U;
using Oculus.Interaction;
using UnityEngine;

namespace ConstructionVR.Player
{
    /// <summary>
    /// A grabbable piece of protective equipment. When it is let go close to the body part it belongs to
    /// (a glove at the hand, the helmet at the head, ...) the player puts it on: the item disappears from
    /// the world and the matching piece of the <see cref="PlayerOutfit"/> appears on the body. Let go
    /// anywhere else, it behaves as before (it goes back to its place on the stand).
    /// </summary>
    public class WearableItem : MonoBehaviour
    {
        [SerializeField] private OutfitPart _part;
        [SerializeField] private Grabbable _grabbable;

        [Tooltip("Optional: sends the item back to the stand, also used to park it while it is worn.")]
        [SerializeField] private NBMReturnToRest _returnToRest;

        private PlayerOutfit _outfit;
        private Renderer[] _renderers;
        private Collider[] _colliders;

        public OutfitPart Part => _part;

        /// <summary>True while the item is on the player and hidden from the world.</summary>
        public bool IsWorn { get; private set; }

        /// <summary>True while the player holds the item.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>Raised when the player picks an item up (to show where it goes).</summary>
        public static event System.Action<WearableItem> WhenPickedUp = delegate { };

        /// <summary>Raised when the player lets an item go; the flag tells whether that put it on.</summary>
        public static event System.Action<WearableItem, bool> WhenLetGo = delegate { };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEvents()
        {
            WhenPickedUp = delegate { };
            WhenLetGo = delegate { };
        }

        protected virtual void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        protected virtual void Start()
        {
            _outfit = FindAnyObjectByType<PlayerOutfit>();
            if (_outfit != null && _outfit.IsWorn(_part))
            {
                SetInWorld(false);          // already worn, e.g. put on in an earlier scene
            }
        }

        protected virtual void OnEnable()
        {
            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
        }

        protected virtual void OnDisable()
        {
            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select && !IsHeld)
            {
                IsHeld = true;
                WhenPickedUp(this);
                return;
            }

            bool released = evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel;
            if (released && _grabbable.SelectingPointsCount == 0 && IsHeld)
            {
                IsHeld = false;
                bool worn = TryWear();
                WhenLetGo(this, worn);
            }
        }

        /// <summary>Puts the item on if it is close enough to its body part. Returns whether it was put on.</summary>
        public bool TryWear()
        {
            if (IsWorn || _outfit == null)
            {
                return false;
            }

            Vector3 itemCentre = _colliders.Length > 0 ? _colliders[0].bounds.center : transform.position;
            float distance = Vector3.Distance(itemCentre, _outfit.WearPoint(_part));
            if (distance > _outfit.WearRadius(_part))
            {
                return false;
            }

            _outfit.Wear(_part);
            SetInWorld(false);
            return true;
        }

        /// <summary>
        /// Puts the item on from where it lies (pointed at with the hand ray and the trigger pulled), without
        /// carrying it to the body. Returns whether it was put on.
        /// </summary>
        public bool WearNow()
        {
            if (IsWorn || IsHeld || _outfit == null)
            {
                return false;
            }

            _outfit.Wear(_part);
            SetInWorld(false);
            WhenLetGo(this, true);          // the guide says what is still missing
            return true;
        }

        /// <summary>Takes the item off the player and puts it back on the stand.</summary>
        public void PutBack()
        {
            if (_outfit != null)
            {
                _outfit.TakeOff(_part);
            }

            SetInWorld(true);
        }

        // The object stays active (the interaction components keep running); it is only made invisible
        // and untouchable, and parked at its place on the stand.
        private void SetInWorld(bool inWorld)
        {
            IsWorn = !inWorld;
            if (_returnToRest != null)
            {
                _returnToRest.SnapToRest();
            }

            foreach (Renderer r in _renderers)
            {
                r.enabled = inWorld;
            }

            foreach (Collider c in _colliders)
            {
                c.enabled = inWorld;
            }
        }
    }
}
