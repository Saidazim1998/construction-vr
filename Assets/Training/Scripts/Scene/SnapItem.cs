using System;
using ConstructionVR.Training.UI;
using NBM500U;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// A thing that is carried to its place and stays there: let go close to the target, it takes the target's
    /// position and rotation. Picked up again it is free (and returns to where it lay if dropped elsewhere).
    /// </summary>
    public class SnapItem : MonoBehaviour
    {
        [SerializeField] private Grabbable _grabbable;
        [SerializeField] private NBMReturnToRest _returnToRest;
        [SerializeField] private Transform _target;
        [SerializeField] private float _radius = 0.12f;
        [Tooltip("Where on the item the distance to the target is measured (the item's own position if empty).")]
        [SerializeField] private Transform _snapPoint;
        [SerializeField] private bool _hideWhenSnapped;
        [SerializeField] private string _kind;

        public bool Snapped { get; private set; }
        public bool Held => _grabbable != null && _grabbable.SelectingPointsCount > 0;
        public string Kind => _kind;
        public Transform Target { get => _target; set => _target = value; }

        /// <summary>Asked before snapping; return false to refuse (the item then goes back to where it lay).</summary>
        public Func<SnapItem, bool> CanSnap;
        public event Action<SnapItem> WhenSnapped;
        public event Action<SnapItem> WhenTaken;

        private bool _wasHeld;

        public float DistanceToTarget => _target == null ? float.MaxValue
            : Vector3.Distance((_snapPoint != null ? _snapPoint : transform).position, _target.position);

        protected virtual void LateUpdate()
        {
            bool held = Held;
            if (held && !_wasHeld && Snapped)
            {
                Snapped = false;
                if (_returnToRest != null) _returnToRest.enabled = true;
                WhenTaken?.Invoke(this);
            }
            else if (!held && _wasHeld && !Snapped && _target != null && DistanceToTarget <= _radius && (CanSnap == null || CanSnap(this)))
            {
                SnapNow();
            }

            _wasHeld = held;
        }

        /// <summary>Lets go of the item at its target, as a hand would: the rules in <see cref="CanSnap"/> apply.</summary>
        public bool TrySnap()
        {
            if (Snapped || _target == null || (CanSnap != null && !CanSnap(this)))
            {
                return false;
            }

            SnapNow();
            return true;
        }

        /// <summary>Picks the item off its target, as a hand would, and puts it back where it lay.</summary>
        public void Take()
        {
            if (!Snapped)
            {
                return;
            }

            Release();
            WhenTaken?.Invoke(this);
        }

        /// <summary>Where the item itself will be once it sits on its target (to show its place beforehand).</summary>
        public bool TryGetSnapPose(out Vector3 position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = transform.rotation;
            if (_target == null)
            {
                return false;
            }

            if (_snapPoint != null)
            {
                // the same move as SnapNow: turn the item so that its snap point matches the target, then shift it there
                rotation = _target.rotation * Quaternion.Inverse(_snapPoint.rotation) * transform.rotation;
                Quaternion turn = rotation * Quaternion.Inverse(transform.rotation);
                position = _target.position - turn * (_snapPoint.position - transform.position);
            }
            else
            {
                position = _target.position;
                rotation = _target.rotation;
            }

            return true;
        }

        /// <summary>Puts the item on its target at once (also used to set up a scene state).</summary>
        public void SnapNow()
        {
            if (_returnToRest != null) _returnToRest.enabled = false;
            if (_snapPoint != null)
            {
                // move the item so that its snap point coincides with the target
                Quaternion rotation = _target.rotation * Quaternion.Inverse(_snapPoint.rotation) * transform.rotation;
                transform.rotation = rotation;
                transform.position += _target.position - _snapPoint.position;
            }
            else
            {
                transform.SetPositionAndRotation(_target.position, _target.rotation);
            }

            Snapped = true;
            if (_hideWhenSnapped)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            }

            WhenSnapped?.Invoke(this);
        }

        /// <summary>Takes the item off its target and back to where it lay.</summary>
        public void Release()
        {
            Snapped = false;
            if (_returnToRest != null)
            {
                _returnToRest.enabled = true;
                _returnToRest.SnapToRest();
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = true;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = true;
        }
    }
}
