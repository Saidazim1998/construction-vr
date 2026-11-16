using System;
using System.Collections.Generic;
using NBM500U;
using Oculus.Interaction;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// The MIG torch in the learner's hand. Pulling the trigger strikes an arc when the set is ready (power,
    /// earth clamp, wire) and the wire end is at the joint; the arc then lays tacks or the seam.
    /// </summary>
    public class WeldTorch : MonoBehaviour
    {
        [SerializeField] private Grabbable _grabbable;
        [SerializeField] private NBMReturnToRest _returnToRest;
        [SerializeField] private Transform _tip;
        [Tooltip("Direction of the wire at the tip, in the tip's local space.")]
        [SerializeField] private Vector3 _tipAxis = Vector3.forward;
        [SerializeField] private WeldMachine _machine;
        [SerializeField] private NBMGasCylinder _gas;
        [SerializeField] private WeldJoint _joint;
        [SerializeField] private Material _arcMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private float _arcReach = 0.028f;

        public bool Held => _simulatedHold || (_grabbable != null && _grabbable.SelectingPointsCount > 0);
        public bool AtRest => !_simulatedHold && (_returnToRest == null || _returnToRest.IsAtRest);
        /// <summary>Where the torch hangs when it is put away.</summary>
        public Vector3 RestPosition => _returnToRest != null ? _returnToRest.RestPosition : transform.position;

        public bool ArcOn { get; private set; }
        public float StickOutMm { get; private set; }
        public float WorkAngle { get; private set; }
        public float TravelAngle { get; private set; }
        public Transform Tip => _tip;
        public WeldJoint Joint => _joint;

        /// <summary>Is the earth clamp on the work? Asked by the torch, answered by the director.</summary>
        public Func<bool> IsEarthed;

        /// <summary>May an arc burn now, and for what: 0 = no (reason key in the out value), 1 = tacks only, 2 = the seam.</summary>
        public Func<(int mode, string reason)> Permission;

        public event Action WhenArcStarted;
        public event Action WhenArcEnded;
        public event Action<string> WhenRefused;

        private OVRCameraRig _rig;
        private Transform _glow;
        private Light _light;
        private ParticleSystem _sparks;
        private AudioSource _audio;
        private bool _simulatedTrigger;
        private string _lastRefusal;
        private float _refusedAt;

        protected virtual void Start()
        {
            _rig = FindAnyObjectByType<OVRCameraRig>();

            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "Arc";
            Destroy(glow.GetComponent<Collider>());
            _glow = glow.transform;
            _glow.SetParent(transform, false);
            _glow.localScale = Vector3.one * 0.012f;
            glow.GetComponent<Renderer>().sharedMaterial = _arcMaterial;
            glow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _light = glow.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(0.72f, 0.85f, 1f);
            _light.range = 2.5f;
            _light.shadows = LightShadows.None;
            glow.SetActive(false);

            var sparkObject = new GameObject("Sparks");
            sparkObject.transform.SetParent(transform, false);
            _sparks = sparkObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.002f, 0.005f);
            main.startColor = new Color(1f, 0.75f, 0.35f);
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = _sparks.emission;
            emission.rateOverTime = 90f;
            ParticleSystem.ShapeModule shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.004f;
            var sparkRenderer = sparkObject.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.sharedMaterial = _sparkMaterial;
            sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparkRenderer.lengthScale = 2.5f;
            sparkRenderer.velocityScale = 0.06f;
            _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = Crackle();
            _audio.loop = true;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 0.4f;
            _audio.maxDistance = 12f;
            _audio.playOnAwake = false;
        }

        protected virtual void Update()
        {
            bool wanted = Held && Trigger() > 0.5f;
            bool arc = false;
            if (wanted && _joint != null && _tip != null)
            {
                Vector3 tip = _tip.position;
                _joint.Project(tip, out float u, out float sideways, out float distance);
                Vector3 axis = _tip.TransformDirection(_tipAxis).normalized;
                Measure(axis, sideways);

                if (distance <= _arcReach && u > -0.03f && u < 1.03f)
                {
                    string refusal = _machine == null || !_machine.PowerOn ? "arc.nopower" : IsEarthed != null && !IsEarthed() ? "arc.noearth"
                        : !_machine.WireLoaded ? "arc.nowire" : null;
                    int mode = 2;
                    if (refusal == null && Permission != null)
                    {
                        (mode, refusal) = Permission();
                    }

                    if (refusal != null || mode == 0)
                    {
                        Refuse(refusal);
                    }
                    else
                    {
                        arc = true;
                        float gas = _gas != null ? _gas.FlowLitresPerMinute : 0f;
                        int tack = _joint.TackNear(tip, 0.02f);
                        if (mode == 1)
                        {
                            if (tack >= 0) _joint.HeatTack(tack, Time.deltaTime);
                        }
                        else
                        {
                            _joint.Weld(Mathf.Clamp01(u), Time.deltaTime, StickOutMm, WorkAngle, TravelAngle, _machine.Voltage, _machine.Current, gas);
                        }
                    }
                }
            }

            if (arc != ArcOn)
            {
                ArcOn = arc;
                _glow.gameObject.SetActive(arc);
                if (arc)
                {
                    _sparks.Play(true);
                    _audio.volume = AppSettings.Current.effectsVolume;
                    _audio.Play();
                    if (_joint != null) _joint.ArcStarts++;
                    WhenArcStarted?.Invoke();
                }
                else
                {
                    _sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    _audio.Stop();
                    if (_joint != null) _joint.Lift();
                    WhenArcEnded?.Invoke();
                }
            }

            if (arc)
            {
                _glow.position = _tip.position + _tip.TransformDirection(_tipAxis).normalized * 0.006f;
                _sparks.transform.position = _glow.position;
                _light.intensity = 2.5f + UnityEngine.Random.value * 2.5f;
            }

            if (WeldingMask.Instance != null)
            {
                WeldingMask.Instance.ArcBurning = arc;
            }
        }

        private void Measure(Vector3 axis, float sideways)
        {
            Transform j = _joint.transform;
            float down = -Vector3.Dot(axis, j.up);                      // into the base plate
            float inwards = -Vector3.Dot(axis, j.right);                // into the standing plate
            float along = Vector3.Dot(axis, j.forward);
            WorkAngle = Mathf.Atan2(down, inwards) * Mathf.Rad2Deg;     // 0 = lying on the base plate, 90 = straight down
            TravelAngle = Mathf.Asin(Mathf.Clamp(Mathf.Abs(along), 0f, 1f)) * Mathf.Rad2Deg;
            StickOutMm = sideways * 1000f;
        }

        private float Trigger()
        {
            if (_simulatedTrigger)
            {
                return 1f;
            }

            if (_rig == null)
            {
                return 0f;
            }

            // the hand that is nearer to the torch holds it
            bool left = Vector3.Distance(_rig.leftHandAnchor.position, transform.position) < Vector3.Distance(_rig.rightHandAnchor.position, transform.position);
            OVRInput.Controller controller = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            if (!OVRInput.IsControllerConnected(controller))
            {
                return 1f;                                              // tracked hands have no trigger: the arc strikes on contact
            }

            return OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);
        }

        private void Refuse(string reason)
        {
            if (reason == null || (reason == _lastRefusal && Time.time < _refusedAt + 5f))
            {
                return;
            }

            _lastRefusal = reason;
            _refusedAt = Time.time;
            WhenRefused?.Invoke(reason);
        }

        /// <summary>For tests: behaves as if the trigger were pulled.</summary>
        public void SimulateTrigger(bool pulled) => _simulatedTrigger = pulled;

        private bool _simulatedHold;

        /// <summary>
        /// For tests: holds the torch at a place on the seam the way a welder would, with the given distance
        /// from the contact tip to the corner and the given angles. Hold false puts it back.
        /// </summary>
        public void SimulatePose(bool hold, float u = 0f, float stickOutMm = 12f, float workAngle = 45f, float travelAngle = 10f)
        {
            _simulatedHold = hold;
            if (!hold)
            {
                _simulatedTrigger = false;
                if (_returnToRest != null) _returnToRest.SnapToRest();
                return;
            }

            Transform j = _joint.transform;
            float w = workAngle * Mathf.Deg2Rad, t = travelAngle * Mathf.Deg2Rad;
            Vector3 axis = (-(Mathf.Sin(w) * j.up + Mathf.Cos(w) * j.right) * Mathf.Cos(t) + j.forward * Mathf.Sin(t)).normalized;
            Vector3 current = _tip.TransformDirection(_tipAxis).normalized;
            transform.rotation = Quaternion.FromToRotation(current, axis) * transform.rotation;
            Vector3 wanted = _joint.PointAt(u) - axis * (stickOutMm * 0.001f);
            transform.position += wanted - _tip.position;
        }

        private static AudioClip Crackle()
        {
            const int rate = 22050;
            int count = rate * 2;
            var data = new float[count];
            var random = new System.Random(7);
            float low = 0f;
            for (int i = 0; i < count; i++)
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                low = Mathf.Lerp(low, noise, 0.35f);
                float buzz = Mathf.Sin(2f * Mathf.PI * 100f * i / rate) > 0f ? 1f : 0.55f;
                data[i] = (noise * 0.35f + low * 0.5f) * buzz * 0.5f;
            }

            AudioClip clip = AudioClip.Create("arc", count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
