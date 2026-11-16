using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace NBM500U
{
    /// <summary>
    /// Grab transformer for small rotary knobs. The knob stays where it is and turns about one
    /// of its own local axes, following the twist of the grabbing hand or controller.
    /// (Meta's OneGrabRotateTransformer follows the swing of the grab point around a pivot,
    /// which suits doors and levers but not a knob that is pinched on its axis.)
    /// Assign it as the One Grab Transformer of a <see cref="Grabbable"/>.
    /// </summary>
    public class NBMKnobTwistTransformer : MonoBehaviour, ITransformer
    {
        public enum LocalAxis { X = 0, Y = 1, Z = 2 }

        [Tooltip("Local axis of the knob it turns about.")]
        [SerializeField] private LocalAxis _axis = LocalAxis.Y;

        [Tooltip("Clamp the angle between Min Angle and Max Angle. Off = endless (encoder).")]
        [SerializeField] private bool _limitAngle = false;
        [SerializeField] private float _minAngle = -150f;
        [SerializeField] private float _maxAngle = 150f;

        [Tooltip("0 = smooth. Above 0 the knob snaps to steps of this many degrees.")]
        [SerializeField] private float _detentAngle = 0f;

        [Tooltip("Knob degrees per degree of hand twist.")]
        [SerializeField] private float _twistGain = 1f;

        [Tooltip("Raised with the angle in degrees from the rest pose whenever it changes.")]
        [SerializeField] private UnityEvent<float> _whenAngleChanged = new UnityEvent<float>();

        /// <summary>Current angle in degrees, measured from the rest pose.</summary>
        public float Angle => _angle;

        public UnityEvent<float> WhenAngleChanged => _whenAngleChanged;

        public LocalAxis Axis { get => _axis; set => _axis = value; }

        /// <summary>Knob degrees per degree of hand twist.</summary>
        public float TwistGain { get => _twistGain; set => _twistGain = value; }

        /// <summary>
        /// +1 or -1: the sign of the angle that raises whatever the knob sets (opens the valve, raises the
        /// voltage). Whoever reads the knob sets it, so that a thumbstick pushed right always means "more".
        /// </summary>
        public float IncreaseSign { get; set; } = 1f;

        private IGrabbable _grabbable;
        private Transform _target;
        private Quaternion _restLocalRotation;
        private float _angle;
        private float _rawAngle;
        private Quaternion _previousGrabRotation;

        private Vector3 AxisVector
        {
            get
            {
                Vector3 v = Vector3.zero;
                v[(int)_axis] = 1f;
                return v;
            }
        }

        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;
            _target = grabbable.Transform;
            _restLocalRotation = _target.localRotation;
            _angle = 0f;
            _rawAngle = 0f;
        }

        public void BeginTransform()
        {
            _previousGrabRotation = _grabbable.GrabPoints[0].rotation;
            _rawAngle = _angle;
        }

        public void UpdateTransform()
        {
            Quaternion current = _grabbable.GrabPoints[0].rotation;
            Quaternion delta = current * Quaternion.Inverse(_previousGrabRotation);
            _previousGrabRotation = current;

            _rawAngle += TwistAngle(delta, WorldAxis()) * _twistGain;
            if (_limitAngle)
            {
                _rawAngle = Mathf.Clamp(_rawAngle, _minAngle, _maxAngle);
            }

            float angle = _rawAngle;
            if (_detentAngle > 0f)
            {
                angle = Mathf.Round(angle / _detentAngle) * _detentAngle;
            }

            Apply(angle);
        }

        public void EndTransform()
        {
        }

        /// <summary>Sets the knob angle from code (degrees from the rest pose).</summary>
        public void SetAngle(float angle)
        {
            if (_target == null)
            {
                return;
            }

            if (_limitAngle)
            {
                angle = Mathf.Clamp(angle, _minAngle, _maxAngle);
            }

            _rawAngle = angle;
            Apply(angle);
        }

        private void Apply(float angle)
        {
            _target.localRotation = _restLocalRotation * Quaternion.AngleAxis(angle, AxisVector);
            if (!Mathf.Approximately(angle, _angle))
            {
                _angle = angle;
                _whenAngleChanged.Invoke(_angle);
            }
        }

        // The turning axis in world space. It does not change while the knob turns about it.
        private Vector3 WorldAxis()
        {
            Vector3 axisInParent = _restLocalRotation * AxisVector;
            return _target.parent != null ? _target.parent.rotation * axisInParent : axisInParent;
        }

        // Signed angle, in degrees, of the part of rotation q that turns about the given axis.
        private static float TwistAngle(Quaternion q, Vector3 axis)
        {
            float projection = Vector3.Dot(new Vector3(q.x, q.y, q.z), axis);
            float angle = 2f * Mathf.Atan2(projection, q.w) * Mathf.Rad2Deg;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
