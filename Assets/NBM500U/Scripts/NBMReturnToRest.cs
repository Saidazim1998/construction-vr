using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace NBM500U
{
    /// <summary>
    /// Sends a grabbable object back to the pose it had at start once it is let go.
    /// Used on the welding torch, whose cable is a static mesh: without this the torch
    /// would stay hanging in the air where it was released. Turn off Return On Release
    /// to leave it where it is dropped.
    /// </summary>
    public class NBMReturnToRest : MonoBehaviour
    {
        [SerializeField] private Grabbable _grabbable;

        [SerializeField] private bool _returnOnRelease = true;

        [Tooltip("Seconds to wait after release before moving back.")]
        [SerializeField] private float _delay = 0.5f;

        [Tooltip("Seconds the move back takes.")]
        [SerializeField] private float _duration = 0.35f;

        public bool ReturnOnRelease { get => _returnOnRelease; set => _returnOnRelease = value; }
        public bool IsAtRest { get; private set; } = true;

        /// <summary>World position of the place the object returns to.</summary>
        public Vector3 RestPosition => transform.parent != null ? transform.parent.TransformPoint(_restLocalPosition) : _restLocalPosition;

        private Vector3 _restLocalPosition;
        private Quaternion _restLocalRotation;
        private Coroutine _routine;

        protected virtual void Awake()
        {
            _restLocalPosition = transform.localPosition;
            _restLocalRotation = transform.localRotation;
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

            StopReturn();
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    IsAtRest = false;
                    StopReturn();
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    if (_returnOnRelease && _grabbable.SelectingPointsCount == 0 && isActiveAndEnabled)
                    {
                        StopReturn();
                        _routine = StartCoroutine(ReturnRoutine());
                    }

                    break;
            }
        }

        /// <summary>Puts the object back at its rest pose at once.</summary>
        public void SnapToRest()
        {
            StopReturn();
            transform.localPosition = _restLocalPosition;
            transform.localRotation = _restLocalRotation;
            IsAtRest = true;
        }

        private void StopReturn()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private IEnumerator ReturnRoutine()
        {
            if (_delay > 0f)
            {
                yield return new WaitForSeconds(_delay);
            }

            Vector3 fromPosition = transform.localPosition;
            Quaternion fromRotation = transform.localRotation;
            float t = 0f;
            while (t < 1f)
            {
                t = _duration > 0f ? Mathf.Min(1f, t + Time.deltaTime / _duration) : 1f;
                float eased = Mathf.SmoothStep(0f, 1f, t);
                transform.localPosition = Vector3.Lerp(fromPosition, _restLocalPosition, eased);
                transform.localRotation = Quaternion.Slerp(fromRotation, _restLocalRotation, eased);
                yield return null;
            }

            IsAtRest = true;
            _routine = null;
        }
    }
}
