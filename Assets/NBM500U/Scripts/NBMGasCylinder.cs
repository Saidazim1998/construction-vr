using UnityEngine;
using UnityEngine.Events;

namespace NBM500U
{
    /// <summary>
    /// Shielding-gas cylinder with regulator. Turning the cylinder valve hand wheel open lets pressure
    /// into the regulator and the gauge needle swings to the cylinder pressure. With the valve shut the
    /// pressure stays trapped until the flowmeter knob is opened, then it bleeds off.
    /// The flowmeter float rises with the gas flow.
    /// </summary>
    public class NBMGasCylinder : MonoBehaviour
    {
        [Header("Cylinder valve")]
        [SerializeField] private NBMKnobTwistTransformer _valve;
        [Tooltip("+1 or -1: sign of the valve knob angle that opens the valve (anticlockwise seen from above).")]
        [SerializeField] private float _valveOpenSign = -1f;
        [Tooltip("Degrees of opening at which the full cylinder pressure reaches the gauge.")]
        [SerializeField] private float _valveFullPressureAngle = 30f;

        [Header("Pressure gauge")]
        [SerializeField] private Transform _needle;
        [Tooltip("Local axis of the needle it turns about (the dial normal).")]
        [SerializeField] private Vector3 _needleAxis = Vector3.up;
        [Tooltip("+1 or -1: sign that turns the needle clockwise as seen from the front.")]
        [SerializeField] private float _needleSign = 1f;
        [Tooltip("Pressure in a full CO2 cylinder at room temperature, MPa.")]
        [SerializeField] private float _cylinderPressureMpa = 5.7f;
        [Tooltip("Full scale of the gauge, psi (the dial is drawn 0..3500 psi).")]
        [SerializeField] private float _gaugeFullScalePsi = 3500f;
        [Tooltip("Needle travel for the full scale, degrees.")]
        [SerializeField] private float _gaugeSweepAngle = 270f;
        [SerializeField] private float _riseTime = 0.35f;
        [SerializeField] private float _bleedTime = 1.5f;

        [Header("Flowmeter")]
        [SerializeField] private NBMKnobTwistTransformer _flowKnob;
        [SerializeField] private float _flowKnobOpenSign = 1f;
        [Tooltip("Knob angle for full flow, degrees.")]
        [SerializeField] private float _flowKnobFullAngle = 270f;
        [SerializeField] private Transform _floatBall;
        [Tooltip("Local direction and distance the float travels at full flow.")]
        [SerializeField] private Vector3 _floatTravel = new Vector3(0f, 0.1f, 0f);
        [SerializeField] private float _maxFlowLitresPerMinute = 25f;

        [SerializeField] private UnityEvent<float> _whenPressureChanged = new UnityEvent<float>();

        public const float PsiPerMpa = 145.038f;

        /// <summary>Pressure shown on the gauge, MPa.</summary>
        public float PressureMpa { get; private set; }
        public float PressurePsi => PressureMpa * PsiPerMpa;

        /// <summary>Pressure in the cylinder itself, MPa (a nearly empty cylinder shows it on the gauge).</summary>
        public float CylinderPressureMpa
        {
            get => _cylinderPressureMpa;
            set => _cylinderPressureMpa = Mathf.Max(0f, value);
        }
        /// <summary>How far the cylinder valve is open, in degrees (0 = shut).</summary>
        public float ValveOpenAngle => _valve != null ? Mathf.Max(0f, _valve.Angle * _valveOpenSign) : 0f;
        public bool IsValveOpen => ValveOpenAngle > 1f;

        /// <summary>The cylinder valve (to point at it).</summary>
        public Transform ValveTransform => _valve != null ? _valve.transform : null;

        /// <summary>The flow knob of the regulator (to point at it).</summary>
        public Transform FlowKnobTransform => _flowKnob != null ? _flowKnob.transform : null;
        /// <summary>Flowmeter knob opening, 0..1.</summary>
        public float FlowKnobOpening => _flowKnob != null && _flowKnobFullAngle > 0f
            ? Mathf.Clamp01(_flowKnob.Angle * _flowKnobOpenSign / _flowKnobFullAngle) : 0f;
        /// <summary>Gas flow through the flowmeter, litres per minute.</summary>
        public float FlowLitresPerMinute { get; private set; }
        /// <summary>Needle deflection from the zero mark, degrees.</summary>
        public float NeedleAngle { get; private set; }
        public UnityEvent<float> WhenPressureChanged => _whenPressureChanged;

        private Quaternion _needleRest;
        private Vector3 _floatRest;
        private float _lastReported = -1f;

        protected virtual void Awake()
        {
            if (_needle != null)
            {
                _needleRest = _needle.localRotation;
            }

            if (_floatBall != null)
            {
                _floatRest = _floatBall.localPosition;
            }

            if (_valve != null) _valve.IncreaseSign = Mathf.Sign(_valveOpenSign);
            if (_flowKnob != null) _flowKnob.IncreaseSign = Mathf.Sign(_flowKnobOpenSign);
        }

        protected virtual void Update()
        {
            float opening = ValveOpenAngle;
            float flowKnob = FlowKnobOpening;

            // Pressure upstream of the regulator.
            if (opening > 1f)
            {
                float target = _cylinderPressureMpa * Mathf.Clamp01(opening / Mathf.Max(1f, _valveFullPressureAngle));
                PressureMpa = Approach(PressureMpa, target, _cylinderPressureMpa, _riseTime);
            }
            else if (flowKnob > 0.01f)
            {
                // Valve shut and gas flowing out: the trapped pressure bleeds off.
                PressureMpa = Approach(PressureMpa, 0f, _cylinderPressureMpa, _bleedTime / Mathf.Max(0.05f, flowKnob));
            }

            float supply = Mathf.Clamp01(PressureMpa / 0.3f);
            FlowLitresPerMinute = _maxFlowLitresPerMinute * flowKnob * supply;

            NeedleAngle = _gaugeFullScalePsi > 0f
                ? Mathf.Clamp(PressurePsi / _gaugeFullScalePsi, 0f, 1.03f) * _gaugeSweepAngle
                : 0f;
            if (_needle != null)
            {
                _needle.localRotation = _needleRest * Quaternion.AngleAxis(NeedleAngle * _needleSign, _needleAxis);
            }

            if (_floatBall != null)
            {
                float flow01 = _maxFlowLitresPerMinute > 0f ? FlowLitresPerMinute / _maxFlowLitresPerMinute : 0f;
                _floatBall.localPosition = _floatRest + _floatTravel * flow01;
            }

            if (Mathf.Abs(PressureMpa - _lastReported) > 0.01f)
            {
                _lastReported = PressureMpa;
                _whenPressureChanged.Invoke(PressureMpa);
            }
        }

        // Moves value towards target at a rate that covers the full range in about `time` seconds.
        private static float Approach(float value, float target, float range, float time)
        {
            float step = range / Mathf.Max(0.01f, time) * Time.deltaTime;
            return Mathf.MoveTowards(value, target, step);
        }
    }
}
