using System;
using NBM500U;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// The working state of the NBM-500U set: mains switch, the welding mode read from its knobs and shown on
    /// its displays, the wire (spool, roller pressure, feeding the wire through to the torch).
    /// It only models what the practice needs; the director decides what each change means for the scenario.
    /// </summary>
    public class WeldMachine : MonoBehaviour
    {
        [Header("Power")]
        [SerializeField] private TouchSwitch _mainSwitch;
        [SerializeField] private Transform _fan;
        [SerializeField] private Vector3 _fanAxis = Vector3.forward;

        [Header("Mode knobs (value at the knob's starting position, change per degree)")]
        [SerializeField] private NBMKnobTwistTransformer _voltageKnob;
        [SerializeField] private float _voltageSign = 1f;
        [SerializeField] private NBMKnobTwistTransformer _currentKnob;
        [SerializeField] private float _currentSign = 1f;
        [SerializeField] private NBMKnobTwistTransformer _speedKnob;
        [SerializeField] private float _speedSign = 1f;
        [SerializeField] private float _startVoltage = 19f;
        [SerializeField] private float _voltsPerDegree = 1f / 12f;
        [SerializeField] private float _startCurrent = 140f;
        [SerializeField] private float _ampsPerDegree = 1f;
        [SerializeField] private float _startSpeed = 3.5f;
        [SerializeField] private float _speedPerDegree = 1f / 30f;

        [Header("Displays (texts placed over the display windows)")]
        [SerializeField] private TMP_Text[] _voltageDisplays;
        [SerializeField] private TMP_Text[] _currentDisplays;

        [Header("Wire")]
        [SerializeField] private NBMKnobTwistTransformer _tensionFront;
        [SerializeField] private NBMKnobTwistTransformer _tensionRear;
        [SerializeField] private float _tensionSign = 1f;
        [SerializeField] private float _startTension = 1f;
        [SerializeField] private float _tensionPerDegree = 1f / 90f;
        [SerializeField] private NBMButton _wireInchButton;
        [SerializeField] private GameObject _spoolWire;             // the coil on the spool: hidden when the spool is empty
        [SerializeField] private GameObject _wireAtTip;             // the wire end at the torch tip
        [SerializeField] private float _feedSeconds = 2.5f;

        public bool PowerOn => _mainSwitch != null && _mainSwitch.IsOn;
        public float Voltage { get; private set; }
        public float Current { get; private set; }
        public float WireSpeed { get; private set; }
        public float Tension { get; private set; }
        public bool SpoolEmpty { get; private set; }
        public bool WireLoaded { get; private set; }
        public float FeedProgress { get; private set; }
        public bool FeedButtonHeld => _wireInchButton != null && _wireInchButton.IsPressed;
        public TouchSwitch MainSwitch => _mainSwitch;

        // The controls, for the guide arrow.
        public Transform VoltageKnob => _voltageKnob != null ? _voltageKnob.transform : null;
        public Transform CurrentKnob => _currentKnob != null ? _currentKnob.transform : null;
        public Transform SpeedKnob => _speedKnob != null ? _speedKnob.transform : null;
        public Transform TensionKnob => _tensionFront != null ? _tensionFront.transform : _tensionRear != null ? _tensionRear.transform : null;
        public Transform TensionFrontKnob => _tensionFront != null ? _tensionFront.transform : null;
        public Transform TensionRearKnob => _tensionRear != null ? _tensionRear.transform : null;

        /// <summary>What each of the two roller knobs is set to (the feeder works at the lower of the two).</summary>
        public float TensionFront { get; private set; }
        public float TensionRear { get; private set; }
        public Transform WireButton => _wireInchButton != null ? _wireInchButton.transform : null;

        /// <summary>Raised while the feed button is held but the wire cannot move; the argument says why.</summary>
        public event Action<string> WhenFeedRefused;
        public event Action WhenWireLoaded;

        private string _lastRefusal;
        private float _refusalAt;
        private bool _simulatedFeed;

        protected virtual void Start()
        {
            // a thumbstick pushed right turns a held knob towards "more"
            if (_voltageKnob != null) _voltageKnob.IncreaseSign = Mathf.Sign(_voltageSign);
            if (_currentKnob != null) _currentKnob.IncreaseSign = Mathf.Sign(_currentSign);
            if (_speedKnob != null) _speedKnob.IncreaseSign = Mathf.Sign(_speedSign);
            if (_tensionFront != null) _tensionFront.IncreaseSign = Mathf.Sign(_tensionSign);
            if (_tensionRear != null) _tensionRear.IncreaseSign = Mathf.Sign(_tensionSign);
            Read();
            Show();
            if (_wireAtTip != null) _wireAtTip.SetActive(WireLoaded);
        }

        protected virtual void Update()
        {
            Read();
            Show();
            if (PowerOn && _fan != null)
            {
                _fan.Rotate(_fanAxis, 900f * Time.deltaTime, Space.Self);
            }

            if (FeedButtonHeld || _simulatedFeed)
            {
                Feed(Time.deltaTime);
            }
        }

        private void Read()
        {
            TrainingConfig c = TrainingConfig.Current;
            Voltage = Knob(_voltageKnob, _voltageSign, _startVoltage, _voltsPerDegree, c.voltageKnob);
            Current = Knob(_currentKnob, _currentSign, _startCurrent, _ampsPerDegree, c.currentKnob);
            WireSpeed = Knob(_speedKnob, _speedSign, _startSpeed, _speedPerDegree, c.wireSpeedKnob);
            float front = Knob(_tensionFront, _tensionSign, _startTension, _tensionPerDegree, new ValueRange(0f, 5f));
            float rear = Knob(_tensionRear, _tensionSign, _startTension, _tensionPerDegree, new ValueRange(0f, 5f));
            TensionFront = front;
            TensionRear = _tensionRear != null ? rear : front;
            Tension = _tensionRear != null ? Mathf.Min(front, rear) : front;       // both rollers have to press
        }

        private static float Knob(NBMKnobTwistTransformer knob, float sign, float start, float perDegree, ValueRange limits)
        {
            float angle = knob != null ? knob.Angle * sign : 0f;
            return Mathf.Clamp(start + angle * perDegree, limits.min, limits.max);
        }

        private void Show()
        {
            string volts = PowerOn ? Voltage.ToString("0.0") : string.Empty;
            string amps = PowerOn ? Current.ToString("0") : string.Empty;
            if (_voltageDisplays != null)
            {
                foreach (TMP_Text t in _voltageDisplays) if (t != null && t.text != volts) t.text = volts;
            }

            if (_currentDisplays != null)
            {
                foreach (TMP_Text t in _currentDisplays) if (t != null && t.text != amps) t.text = amps;
            }
        }

        private void Feed(float dt)
        {
            if (WireLoaded)
            {
                return;
            }

            string refusal = !PowerOn ? "feed.nopower" : SpoolEmpty ? "feed.nospool"
                : !TrainingConfig.Current.rollerTension.Contains(Tension) ? (Tension < TrainingConfig.Current.rollerTension.min ? "feed.slips" : "feed.crushed") : null;
            if (refusal != null)
            {
                if (refusal != _lastRefusal || Time.time > _refusalAt + 4f)
                {
                    _lastRefusal = refusal;
                    _refusalAt = Time.time;
                    WhenFeedRefused?.Invoke(refusal);
                }

                return;
            }

            FeedProgress = Mathf.Clamp01(FeedProgress + dt / Mathf.Max(0.1f, _feedSeconds));
            if (FeedProgress >= 1f)
            {
                WireLoaded = true;
                if (_wireAtTip != null) _wireAtTip.SetActive(true);
                WhenWireLoaded?.Invoke();
            }
        }

        /// <summary>Sets the spool state (a session may start with an empty spool; a new spool refills it).</summary>
        public void SetSpool(bool empty)
        {
            SpoolEmpty = empty;
            if (_spoolWire != null) _spoolWire.SetActive(!empty);
            if (empty)
            {
                WireLoaded = false;
                FeedProgress = 0f;
                if (_wireAtTip != null) _wireAtTip.SetActive(false);
            }
        }

        /// <summary>For tests: behaves as if the wire button were held.</summary>
        public void SimulateFeedButton(bool held) => _simulatedFeed = held;
    }
}
