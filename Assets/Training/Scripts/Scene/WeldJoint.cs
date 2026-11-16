using System;
using System.Collections.Generic;
using NBM500U;
using Oculus.Interaction;
using UnityEngine;

namespace ConstructionVR.Training
{
    [Flags]
    public enum WeldDefect { None = 0, Porosity = 1, BurnThrough = 2, LackOfFusion = 4, Uneven = 8, Spatter = 16 }

    /// <summary>
    /// The joint that is welded: a straight seam in the corner between the base plate and the standing plate.
    /// The object's Z axis runs along the seam from its start, Y is up (the base plate's normal), X points out
    /// of the corner towards the welder. It keeps what was welded where, and how well.
    /// </summary>
    public class WeldJoint : MonoBehaviour
    {
        public struct Segment
        {
            public bool Welded;
            public float LegMm;
            public float Score;             // 0 .. 1
            public WeldDefect Defects;
        }

        [SerializeField] private float _length = 0.3f;
        [SerializeField] private float _segmentLength = 0.005f;
        [SerializeField] private Material _beadMaterial;
        [SerializeField] private Material _markerMaterial;
        [SerializeField] private float[] _tackPositions = { 0.1f, 0.5f, 0.9f };

        public float Length => _length;
        public Segment[] Segments { get; private set; }
        public bool[] Tacks { get; private set; }
        public float[] TackHeat { get; private set; }
        public int TacksDone { get; private set; }
        public int WeldedCount { get; private set; }
        public float Coverage => Segments == null || Segments.Length == 0 ? 0f : WeldedCount / (float)Segments.Length;

        /// <summary>Seconds of arc on the seam with each parameter inside / outside its range (technique score).</summary>
        public float ArcSeconds { get; private set; }
        public float SpeedBadSeconds { get; private set; }
        public float StickOutBadSeconds { get; private set; }
        public float AngleBadSeconds { get; private set; }
        public int ArcStarts { get; set; }

        public event Action<int> WhenTackDone;

        private readonly List<(Renderer renderer, float born)> _hot = new List<(Renderer, float)>();
        private Transform[] _tackMarkers;
        private MaterialPropertyBlock _block;
        private float _lastU = -1f;
        private float _lastTime;
        private float _speed;

        protected virtual void Awake()
        {
            Segments = new Segment[Mathf.Max(1, Mathf.RoundToInt(_length / _segmentLength))];
            Tacks = new bool[_tackPositions.Length];
            TackHeat = new float[_tackPositions.Length];
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Position on the seam: u = 0 .. 1 along it, how far to the side of it, how far from it in all.</summary>
        public void Project(Vector3 world, out float u, out float sideways, out float distance)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            u = local.z / _length;
            float clamped = Mathf.Clamp(local.z, 0f, _length);
            sideways = new Vector2(local.x, local.y).magnitude;
            distance = new Vector3(local.x, local.y, local.z - clamped).magnitude;
        }

        public Vector3 PointAt(float u) => transform.TransformPoint(0f, 0f, Mathf.Clamp01(u) * _length);

        public Vector3 TackPoint(int index) => PointAt(_tackPositions[index]);

        /// <summary>Index of the tack point within <paramref name="reach"/> metres of a position, or -1.</summary>
        public int TackNear(Vector3 world, float reach)
        {
            int best = -1;
            float bestDistance = reach;
            for (int i = 0; i < _tackPositions.Length; i++)
            {
                float d = Vector3.Distance(world, TackPoint(i));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Shows or hides the rings that mark where to tack.</summary>
        public void ShowTackMarkers(bool show)
        {
            if (_tackMarkers == null)
            {
                _tackMarkers = new Transform[_tackPositions.Length];
                for (int i = 0; i < _tackMarkers.Length; i++)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "Tack Marker " + (i + 1);
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    go.transform.position = TackPoint(i) + transform.TransformDirection(new Vector3(0.006f, 0.006f, 0f));
                    go.transform.localScale = Vector3.one * 0.012f;
                    go.GetComponent<Renderer>().sharedMaterial = _markerMaterial;
                    _tackMarkers[i] = go.transform;
                }
            }

            for (int i = 0; i < _tackMarkers.Length; i++)
            {
                _tackMarkers[i].gameObject.SetActive(show && !Tacks[i]);
            }
        }

        /// <summary>The arc dwells at a tack point; after about a second the tack is made.</summary>
        public bool HeatTack(int index, float dt)
        {
            if (Tacks[index])
            {
                return false;
            }

            TackHeat[index] += dt;
            if (TackHeat[index] < 0.9f)
            {
                return false;
            }

            Tacks[index] = true;
            TacksDone++;
            Bead(TackPoint(index), 0.011f, 0.007f, 0.012f, Color.white);
            if (_tackMarkers != null && _tackMarkers[index] != null) _tackMarkers[index].gameObject.SetActive(false);
            WhenTackDone?.Invoke(index);
            return true;
        }

        /// <summary>
        /// One frame of arc on the seam. The quality of what is laid down follows from the welding mode, the gas
        /// and the way the torch is held and moved.
        /// </summary>
        public void Weld(float u, float dt, float stickOutMm, float workAngle, float travelAngle, float volts, float amps, float gasFlow)
        {
            TrainingConfig c = TrainingConfig.Current;
            float now = Time.time;
            if (_lastU >= 0f && now - _lastTime < 0.25f && now > _lastTime)
            {
                float instant = Mathf.Abs(u - _lastU) * _length * 1000f / (now - _lastTime);
                _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-dt * 4f));
            }
            else
            {
                _speed = c.travelSpeedMmS.Middle;                   // a fresh start: no speed known yet
            }

            _lastU = u;
            _lastTime = now;

            bool speedBad = !c.travelSpeedMmS.Contains(_speed);
            bool stickBad = !c.stickOutMm.Contains(stickOutMm);
            bool angleBad = !c.workAngleDeg.Contains(workAngle) || travelAngle > c.travelAngleDeg.max;
            ArcSeconds += dt;
            if (speedBad) SpeedBadSeconds += dt;
            if (stickBad) StickOutBadSeconds += dt;
            if (angleBad) AngleBadSeconds += dt;

            int index = Mathf.Clamp(Mathf.FloorToInt(u * Segments.Length), 0, Segments.Length - 1);
            if (Segments[index].Welded)
            {
                return;                                             // dwelling: counted as slow travel above
            }

            WeldDefect defects = WeldDefect.None;
            float score = 1f;
            if (gasFlow < c.gasFlow.min * 0.85f || stickOutMm > c.stickOutMm.max * 1.6f)
            {
                defects |= WeldDefect.Porosity;
                score -= 0.5f;
            }

            float heat = (volts / c.voltage.Middle) * (amps / c.current.Middle) * (c.travelSpeedMmS.Middle / Mathf.Max(1f, _speed));
            if (volts > c.voltage.max || amps > c.current.max || _speed < c.travelSpeedMmS.min * 0.7f)
            {
                if (heat > 1.35f)
                {
                    defects |= WeldDefect.BurnThrough;
                    score -= 0.5f;
                }
                else
                {
                    score -= 0.15f;
                }
            }

            if (volts < c.voltage.min || amps < c.current.min || _speed > c.travelSpeedMmS.max * 1.3f)
            {
                if (heat < 0.72f)
                {
                    defects |= WeldDefect.LackOfFusion;
                    score -= 0.5f;
                }
                else
                {
                    score -= 0.15f;
                }
            }

            if (angleBad || speedBad)
            {
                defects |= WeldDefect.Uneven;
                score -= 0.2f;
            }

            if (stickBad)
            {
                defects |= WeldDefect.Spatter;
                score -= 0.15f;
            }

            float leg = Mathf.Clamp(c.legSizeMm.Middle * Mathf.Sqrt(Mathf.Clamp(heat, 0.2f, 3f)), 2f, 10f);
            Segments[index] = new Segment { Welded = true, LegMm = leg, Score = Mathf.Clamp01(score), Defects = defects };
            WeldedCount++;

            Color tint = (defects & WeldDefect.Porosity) != 0 ? new Color(0.35f, 0.33f, 0.3f)
                : (defects & WeldDefect.BurnThrough) != 0 ? new Color(0.25f, 0.22f, 0.22f)
                : (defects & WeldDefect.LackOfFusion) != 0 ? new Color(0.75f, 0.78f, 0.85f) : new Color(0.62f, 0.66f, 0.72f);
            float size = leg * 0.001f;
            float ripple = 1f + 0.08f * Mathf.Sin(index * 1.7f);
            Bead(PointAt((index + 0.5f) / Segments.Length), size * 1.35f * ripple, size * 0.75f, _segmentLength * 1.7f, tint);
        }

        /// <summary>The arc left the seam: the next arc starts a new run for the speed measurement.</summary>
        public void Lift() => _lastU = -1f;

        private void Bead(Vector3 seamPoint, float width, float height, float length, Color tint)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Bead";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            // in the corner, lying across it at 45 degrees
            go.transform.position = seamPoint + transform.TransformDirection(new Vector3(height * 0.35f, height * 0.35f, 0f));
            go.transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, 45f);
            go.transform.localScale = new Vector3(width, height, length);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = _beadMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _block.SetColor("_BaseColor", tint);
            _block.SetColor("_EmissionColor", new Color(6f, 2.2f, 0.4f));
            renderer.SetPropertyBlock(_block);
            _hot.Add((renderer, Time.time));
        }

        protected virtual void Update()
        {
            // fresh metal glows and cools down
            for (int i = _hot.Count - 1; i >= 0; i--)
            {
                (Renderer renderer, float born) = _hot[i];
                float age = Time.time - born;
                if (renderer == null || age > 5f)
                {
                    if (renderer != null)
                    {
                        renderer.GetPropertyBlock(_block);
                        _block.SetColor("_EmissionColor", Color.black);
                        renderer.SetPropertyBlock(_block);
                    }

                    _hot.RemoveAt(i);
                    continue;
                }

                float glow = Mathf.Pow(1f - age / 5f, 2.2f);
                renderer.GetPropertyBlock(_block);
                _block.SetColor("_EmissionColor", new Color(6f, 2.2f, 0.4f) * glow);
                renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>Defects that show on a noticeable part of the seam (what an inspector would write down).</summary>
        public WeldDefect VisibleDefects(float share = 0.08f)
        {
            WeldDefect result = WeldDefect.None;
            foreach (WeldDefect d in new[] { WeldDefect.Porosity, WeldDefect.BurnThrough, WeldDefect.LackOfFusion, WeldDefect.Uneven, WeldDefect.Spatter })
            {
                int n = 0;
                foreach (Segment s in Segments)
                {
                    if (s.Welded && (s.Defects & d) != 0) n++;
                }

                if (n >= Segments.Length * share)
                {
                    result |= d;
                }
            }

            return result;
        }

        public float MeanScore()
        {
            float sum = 0f;
            foreach (Segment s in Segments)
            {
                sum += s.Welded ? s.Score : 0f;
            }

            return sum / Segments.Length;
        }

        /// <summary>Leg of the fillet at a place on the seam (0 when nothing was welded there).</summary>
        public float LegAt(float u)
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(u * Segments.Length), 0, Segments.Length - 1);
            float sum = 0f;
            int n = 0;
            for (int i = Mathf.Max(0, index - 2); i <= Mathf.Min(Segments.Length - 1, index + 2); i++)
            {
                if (Segments[i].Welded)
                {
                    sum += Segments[i].LegMm;
                    n++;
                }
            }

            return n > 0 ? sum / n : 0f;
        }
    }
}
