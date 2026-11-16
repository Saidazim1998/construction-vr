using System;
using ConstructionVR.Training.UI;
using NBM500U;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>A cable or chain drawn between two points, hanging with some slack.</summary>
    public class SagLine : MonoBehaviour
    {
        [SerializeField] private Transform _from;
        [SerializeField] private Transform _to;
        [SerializeField] private float _slack = 0.25f;              // extra length, metres
        [SerializeField] private float _width = 0.016f;
        [SerializeField] private float _floor = 0.02f;
        [SerializeField] private Material _material;

        private LineRenderer _line;
        private const int Points = 18;

        public Transform From { get => _from; set => _from = value; }
        public Transform To { get => _to; set => _to = value; }

        protected virtual void Awake()
        {
            _line = gameObject.GetComponent<LineRenderer>();
            if (_line == null)
            {
                _line = gameObject.AddComponent<LineRenderer>();
            }

            _line.useWorldSpace = true;
            _line.positionCount = Points;
            _line.widthMultiplier = _width;
            _line.numCapVertices = 3;
            _line.numCornerVertices = 3;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_material != null)
            {
                _line.sharedMaterial = _material;
            }
        }

        protected virtual void LateUpdate()
        {
            if (_from == null || _to == null)
            {
                return;
            }

            Vector3 a = _from.position, b = _to.position;
            float sag = Mathf.Sqrt(Mathf.Max(0f, _slack * (2f * Vector3.Distance(a, b) + _slack))) * 0.5f;
            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                p.y = Mathf.Max(p.y, _floor);
                _line.SetPosition(i, p);
            }
        }
    }
}
