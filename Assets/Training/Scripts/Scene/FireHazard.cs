using System;
using NBM500U;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>Something that can catch fire from welding sparks, and the fire itself.</summary>
    public class FireHazard : MonoBehaviour
    {
        [SerializeField] private Material _flameMaterial;
        [SerializeField] private float _strength = 3f;              // seconds of extinguishing it takes

        public bool Burning { get; private set; }
        public bool Extinguished { get; private set; }
        public float BurningSeconds { get; private set; }

        public event Action WhenExtinguished;

        private ParticleSystem _flames;
        private Light _light;
        private float _left;

        public void Ignite()
        {
            if (Burning || Extinguished)
            {
                return;
            }

            Burning = true;
            _left = _strength;
            var go = new GameObject("Flames");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up);
            _flames = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.1f, 0.9f), new Color(1f, 0.25f, 0.05f, 0.8f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            ParticleSystem.EmissionModule emission = _flames.emission;
            emission.rateOverTime = 70f;
            ParticleSystem.ShapeModule shape = _flames.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.09f;
            ParticleSystem.SizeOverLifetimeModule size = _flames.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _flameMaterial;
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.55f, 0.2f);
            _light.range = 3f;
            _light.intensity = 3f;
        }

        protected virtual void Update()
        {
            if (!Burning)
            {
                return;
            }

            BurningSeconds += Time.deltaTime;
            if (_light != null)
            {
                _light.intensity = 2.5f + Mathf.PerlinNoise(Time.time * 9f, 0f) * 2f;
            }
        }

        /// <summary>Extinguishing agent hits the fire for a moment.</summary>
        public void Douse(float dt)
        {
            if (!Burning)
            {
                return;
            }

            _left -= dt;
            if (_left > 0f)
            {
                return;
            }

            Burning = false;
            Extinguished = true;
            if (_flames != null) _flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_light != null) _light.enabled = false;
            WhenExtinguished?.Invoke();
        }
    }
}
