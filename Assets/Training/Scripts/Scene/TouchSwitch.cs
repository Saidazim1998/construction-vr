using System;
using ConstructionVR.Training.UI;
using NBM500U;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// A switch that is flipped by touching it with a hand or a controller (the mains switch on the back of the
    /// power source, the button of the fume extractor). The lever turns between two angles.
    /// </summary>
    public class TouchSwitch : MonoBehaviour
    {
        [SerializeField] private Transform _lever;
        [SerializeField] private Vector3 _leverAxis = Vector3.right;
        [SerializeField] private float _leverAngle = 28f;
        [SerializeField] private float _touchRadius = 0.06f;
        [SerializeField] private float _fingerReach = 0.07f;        // from the controller pose to the finger tip

        public bool IsOn { get; private set; }

        /// <summary>Asked before the switch changes; return false to refuse.</summary>
        public Func<bool, bool> CanSwitch;
        public event Action<bool> WhenSwitched;

        private OVRCameraRig _rig;
        private Quaternion _leverRest;
        private bool _inside;
        private float _readyAt;

        protected virtual void Start()
        {
            _rig = FindAnyObjectByType<OVRCameraRig>();
            if (_lever != null)
            {
                _leverRest = _lever.localRotation;
            }
        }

        protected virtual void Update()
        {
            if (_rig == null)
            {
                return;
            }

            bool inside = false;
            foreach (Transform hand in new[] { _rig.leftHandAnchor, _rig.rightHandAnchor })
            {
                if (hand == null) continue;
                Vector3 tip = hand.position + hand.forward * _fingerReach;
                if (Vector3.Distance(tip, transform.position) < _touchRadius || Vector3.Distance(hand.position, transform.position) < _touchRadius)
                {
                    inside = true;
                }
            }

            if (inside && !_inside && Time.time >= _readyAt)
            {
                Set(!IsOn);
                _readyAt = Time.time + 0.8f;
            }

            _inside = inside;
        }

        /// <summary>Flips the switch as a touch does (asks <see cref="CanSwitch"/> first).</summary>
        public bool Set(bool on)
        {
            if (on == IsOn || (CanSwitch != null && !CanSwitch(on)))
            {
                return false;
            }

            IsOn = on;
            if (_lever != null)
            {
                _lever.localRotation = _leverRest * Quaternion.AngleAxis(on ? _leverAngle : 0f, _leverAxis);
            }

            WhenSwitched?.Invoke(on);
            return true;
        }
    }
}
