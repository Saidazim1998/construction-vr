using System;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>What the guide arrow points at: a place in the world and a few words about it.</summary>
    public readonly struct GuideTarget
    {
        public readonly Vector3 Position;
        public readonly string Label;

        /// <summary>The thing itself, when the step is about one thing (a knob, a tool): it glows. May be null.</summary>
        public readonly Transform Focus;

        public GuideTarget(Vector3 position, string label, Transform focus = null)
        {
            Position = position;
            Label = label;
            Focus = focus;
        }
    }

    /// <summary>
    /// Shows the learner where the current step of the practice is done: an arrow bobs over the thing to take,
    /// the place to put it or the control to use, with a short caption, and stays faintly visible through
    /// obstacles. When that place is out of view, a small arrow in front of the eyes tells which way to turn.
    /// The scene's director says what each step points at (<see cref="Source"/>); it knows the objects.
    /// </summary>
    [AddComponentMenu("")]
    public class StepGuide : MonoBehaviour
    {
        /// <summary>Given a step id, returns what to point at, or null for "nothing to show".</summary>
        public static Func<string, GuideTarget?> Source;

        public static StepGuide Instance { get; private set; }

        private const float AskEvery = 0.15f;
        private const float InViewAngle = 34f;          // further off the view direction, the turn arrow appears
        private static readonly Color ArrowColor = new Color(0.18f, 0.83f, 0.63f, 1f);
        private static readonly Color PlateColor = new Color(0.03f, 0.08f, 0.19f, 0.86f);

        private Transform _head;
        private Transform _arrow;
        private Transform _caption;
        private Transform _plate;
        private Transform _turn;
        private TextMeshPro _text;
        private Material _arrowMaterial;
        private Material _plateMaterial;
        private Material _textMaterial;
        private float _nextAsk;
        private string _shownLabel;
        private Vector3 _shownPosition;
        private float _captionLift = 0.27f;

        /// <summary>The step the arrow is shown for (for tests).</summary>
        public string StepId { get; private set; }

        /// <summary>What the arrow points at right now, if anything (for tests).</summary>
        public GuideTarget? Target { get; private set; }

        /// <summary>True while the small "turn this way" arrow is shown (for tests).</summary>
        public bool TurnShown => _turn != null && _turn.gameObject.activeSelf;

        /// <summary>The first step of the current stage that still has to be done, or null.</summary>
        public static string CurrentStep(TrainingSession session)
        {
            if (session == null)
            {
                return "login.enter";
            }

            if (session.IsFinished)
            {
                return null;
            }

            foreach (StepDef step in session.Stage.Steps)
            {
                if (session.IsRequired(step) && !session.IsDone(step.Id))
                {
                    return step.Id;
                }
            }

            return null;
        }

        protected virtual void Awake() => Instance = this;

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_arrowMaterial != null) Destroy(_arrowMaterial);
            if (_plateMaterial != null) Destroy(_plateMaterial);
            if (_textMaterial != null) Destroy(_textMaterial);
        }

        protected virtual void LateUpdate()
        {
            if (Time.unscaledTime >= _nextAsk)
            {
                _nextAsk = Time.unscaledTime + AskEvery;
                StepId = CurrentStep(TrainingSession.Current);
                Target = StepId != null && Source != null ? Source(StepId) : null;
            }

            if (Target == null || !FindHead() || !Build())
            {
                Show(false);
                return;
            }

            GuideTarget target = Target.Value;
            if (target.Focus != null)
            {
                ModelGlow.Draw(target.Focus, ModelGlow.Focus);      // the thing the step is about glows
            }

            if (!_arrow.gameObject.activeSelf || (target.Position - _shownPosition).sqrMagnitude > 0.25f)
            {
                _shownPosition = target.Position;                   // a new place: jump there
            }
            else
            {
                _shownPosition = Vector3.Lerp(_shownPosition, target.Position, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            }

            Vector3 toTarget = _shownPosition - _head.position;
            float distance = toTarget.magnitude;
            float size = Mathf.Clamp(distance * 0.16f, 0.12f, 0.6f);       // about the same size on the eye at any distance
            float bob = Mathf.Sin(Time.unscaledTime * 4f) * 0.18f + 0.18f;
            Show(true);

            // the arrow: tip down, just above the place, turning slowly
            _arrow.position = _shownPosition + Vector3.up * (0.03f + size * (0.12f + bob));
            _arrow.rotation = Quaternion.AngleAxis(Time.unscaledTime * 70f, Vector3.up);
            _arrow.localScale = Vector3.one * size;

            // the caption: above the arrow, facing the learner
            SetLabel(target.Label);
            bool hasLabel = !string.IsNullOrEmpty(target.Label);
            _caption.gameObject.SetActive(hasLabel);
            if (hasLabel)
            {
                _caption.position = _arrow.position + Vector3.up * (size * (1.06f + _captionLift));     // its lower edge just above the arrow
                Vector3 away = _caption.position - _head.position;
                if (away.sqrMagnitude > 1e-4f)
                {
                    _caption.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                }

                _caption.localScale = Vector3.one * size;
            }

            // out of view: a small arrow in front of the eyes points the way
            float angle = distance > 0.05f ? Vector3.Angle(_head.forward, toTarget) : 0f;
            bool turn = angle > InViewAngle;
            _turn.gameObject.SetActive(turn);
            if (turn)
            {
                Vector3 local = _head.InverseTransformDirection(toTarget.normalized);
                var onScreen = new Vector2(local.x, local.y);
                if (onScreen.sqrMagnitude < 1e-4f)
                {
                    onScreen = Vector2.right;                       // straight behind: turn right
                }

                onScreen.Normalize();
                Vector3 direction = _head.TransformDirection(new Vector3(onScreen.x, onScreen.y, 0f));
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                _turn.position = _head.position + _head.forward * 0.7f + direction * (0.19f + 0.025f * pulse);
                _turn.rotation = Quaternion.LookRotation(_head.forward, -direction);    // the tip (the mesh's -Y) points the way
                _turn.localScale = Vector3.one * 0.11f;
            }
        }

        private void Show(bool show)
        {
            if (_arrow == null)
            {
                return;
            }

            if (_arrow.gameObject.activeSelf != show)
            {
                _arrow.gameObject.SetActive(show);
            }

            if (!show)
            {
                _caption.gameObject.SetActive(false);
                _turn.gameObject.SetActive(false);
            }
        }

        private void SetLabel(string label)
        {
            if (label == _shownLabel || string.IsNullOrEmpty(label))
            {
                return;
            }

            _shownLabel = label;
            _text.text = label;
            _text.ForceMeshUpdate();
            Vector2 size = _text.GetPreferredValues(label);
            _plate.localScale = new Vector3(size.x + 0.22f, size.y + 0.1f, 1f);
            _captionLift = (size.y + 0.1f) * 0.5f;
        }

        private bool FindHead()
        {
            if (_head == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                _head = rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
            }

            return _head != null;
        }

        private bool Build()
        {
            if (_arrow != null)
            {
                return true;
            }

            Shader shader = Resources.Load<Shader>("StereoShaders/GuideArrow");
            if (shader == null)
            {
                return false;
            }

            _arrowMaterial = new Material(shader) { color = ArrowColor };
            _plateMaterial = new Material(shader) { color = PlateColor, renderQueue = 2999 };    // before the text on it
            _plateMaterial.SetFloat("_Shade", 0f);
            _plateMaterial.SetFloat("_Hidden", 0.45f);

            Mesh arrowMesh = ArrowMesh();
            _arrow = NewShape("Guide Arrow", arrowMesh, _arrowMaterial);
            _turn = NewShape("Guide Turn Arrow", arrowMesh, _arrowMaterial);

            // caption: a dark plate with the text a hair in front of it; one unit of the caption is the arrow's height
            _caption = new GameObject("Guide Caption").transform;
            _caption.SetParent(transform, false);
            _plate = NewShape("Plate", QuadMesh(), _plateMaterial);
            _plate.SetParent(_caption, false);
            var textObject = new GameObject("Text");
            textObject.transform.SetParent(_caption, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            _text = textObject.AddComponent<TextMeshPro>();
            _text.fontSize = 3.4f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.color = Color.white;
            _text.fontStyle = FontStyles.Bold;
            _text.rectTransform.sizeDelta = new Vector2(6f, 0.6f);

            // the caption is read through whatever stands in the way, like the arrow: no depth test for its text
            _textMaterial = _text.fontMaterial;             // this text's own copy of the font material
            _textMaterial.SetFloat("_UiZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            Show(false);
            return true;
        }

        private Transform NewShape(string shapeName, Mesh mesh, Material material)
        {
            var go = new GameObject(shapeName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        // An arrow one unit tall with its tip at the origin, pointing down (-Y): a cone and a shaft above it.
        private static Mesh ArrowMesh()
        {
            const int n = 20;
            const float headHeight = 0.46f, headRadius = 0.24f, shaftRadius = 0.095f;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();

            void Ring(float y, float radius)
            {
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    vertices.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                }
            }

            void Fan(int centre, int ring, bool flip)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = ring + i, b = ring + (i + 1) % n;
                    triangles.Add(centre);
                    triangles.Add(flip ? b : a);
                    triangles.Add(flip ? a : b);
                }
            }

            void Band(int lower, int upper)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = lower + i, b = lower + (i + 1) % n, c = upper + i, d = upper + (i + 1) % n;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            // every surface has its own vertices, so that the edges stay crisp
            int tip = vertices.Count; vertices.Add(Vector3.zero);
            int coneRing = vertices.Count; Ring(headHeight, headRadius);
            Fan(tip, coneRing, false);

            int capCentre = vertices.Count; vertices.Add(new Vector3(0f, headHeight, 0f));
            int capRing = vertices.Count; Ring(headHeight, headRadius);
            Fan(capCentre, capRing, true);

            int shaftLow = vertices.Count; Ring(headHeight, shaftRadius);
            int shaftHigh = vertices.Count; Ring(1f, shaftRadius);
            Band(shaftLow, shaftHigh);

            int topCentre = vertices.Count; vertices.Add(new Vector3(0f, 1f, 0f));
            int topRing = vertices.Count; Ring(1f, shaftRadius);
            Fan(topCentre, topRing, true);

            var mesh = new Mesh { name = "Guide Arrow", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh QuadMesh()
        {
            var mesh = new Mesh { name = "Guide Plate", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
