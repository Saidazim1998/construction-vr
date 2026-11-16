using System;
using NBM500U;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>A hand fire extinguisher: held and with the trigger pulled it sprays where its nozzle points.</summary>
    public class Extinguisher : MonoBehaviour
    {
        [SerializeField] private Grabbable _grabbable;
        [SerializeField] private Transform _nozzle;
        [SerializeField] private Material _sprayMaterial;
        [SerializeField] private float _range = 3f;
        [SerializeField] private float _coneAngle = 22f;

        public bool Held => _grabbable != null && _grabbable.SelectingPointsCount > 0;
        public bool Spraying { get; private set; }

        /// <summary>The fire to fight. Set by the director.</summary>
        public FireHazard Fire;

        private OVRCameraRig _rig;
        private ParticleSystem _spray;
        private bool _simulated;

        protected virtual void Start()
        {
            _rig = FindAnyObjectByType<OVRCameraRig>();
            var go = new GameObject("Spray");
            go.transform.SetParent(_nozzle != null ? _nozzle : transform, false);
            _spray = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _spray.main;
            main.startLifetime = 0.6f;
            main.startSpeed = 5f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new Color(0.95f, 0.95f, 0.95f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = _spray.emission;
            emission.rateOverTime = 160f;
            ParticleSystem.ShapeModule shape = _spray.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.01f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _sprayMaterial;
            _spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        protected virtual void Update()
        {
            bool spraying = _simulated || (Held && Trigger() > 0.5f);
            if (spraying != Spraying)
            {
                Spraying = spraying;
                if (spraying) _spray.Play(true);
                else _spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (!spraying || Fire == null || !Fire.Burning)
            {
                return;
            }

            Transform nozzle = _nozzle != null ? _nozzle : transform;
            Vector3 toFire = Fire.transform.position - nozzle.position;
            if (_simulated || (toFire.magnitude <= _range && Vector3.Angle(nozzle.forward, toFire) <= _coneAngle))
            {
                Fire.Douse(Time.deltaTime);
            }
        }

        private float Trigger()
        {
            if (_rig == null)
            {
                return 0f;
            }

            bool left = Vector3.Distance(_rig.leftHandAnchor.position, transform.position) < Vector3.Distance(_rig.rightHandAnchor.position, transform.position);
            OVRInput.Controller controller = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            return OVRInput.IsControllerConnected(controller) ? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) : 1f;
        }

        /// <summary>For tests: sprays at the fire as if aimed and triggered.</summary>
        public void SimulateSpray(bool on) => _simulated = on;
    }
}
