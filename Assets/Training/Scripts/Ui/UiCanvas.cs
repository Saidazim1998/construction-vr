using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ConstructionVR.Training.UI
{
    /// <summary>
    /// A panel in the world that the player works with a controller ray, a hand ray or a finger: a world-space
    /// canvas wired to the Interaction SDK (PointableCanvas, ray and poke interactables on a clipped plane).
    /// The panel faces along its -Z axis towards the viewer, as Unity canvases do.
    /// </summary>
    public class UiCanvas : MonoBehaviour
    {
        public Canvas Canvas { get; private set; }
        public RectTransform Root { get; private set; }
        public Vector2 Size { get; private set; }

        /// <summary>
        /// True: the panel is drawn over the world, so that a machine or a table between it and the eyes never
        /// hides it (the task card and the alerts that go with the learner, the cards that open at a place).
        /// False: it is hidden behind what stands in front of it, like any object; for panels fixed in the room
        /// that are pressed with a finger, where a hand in front of the panel must stay visible.
        /// </summary>
        public bool OnTop
        {
            get => _onTop;
            set
            {
                if (_onTop != value)
                {
                    _onTop = value;
                    ApplyDepthTest();
                }
            }
        }

        private bool _onTop = true;
        private static readonly List<UiCanvas> s_onTop = new List<UiCanvas>();
        private static readonly List<Graphic> s_graphics = new List<Graphic>();
        private static bool s_hooked;

        protected virtual void OnEnable()
        {
            s_onTop.Add(this);
            if (!s_hooked)
            {
                s_hooked = true;
                Canvas.willRenderCanvases += ApplyAll;
            }
        }

        protected virtual void OnDisable() => s_onTop.Remove(this);

        // Panels are rebuilt whenever their content changes, so new elements are given the right material just
        // before the canvases are drawn, every frame.
        private static void ApplyAll()
        {
            for (int i = s_onTop.Count - 1; i >= 0; i--)
            {
                UiCanvas panel = s_onTop[i];
                if (panel == null)
                {
                    s_onTop.RemoveAt(i);
                }
                else if (panel._onTop)
                {
                    panel.ApplyDepthTest();
                }
            }
        }

        /// <summary>Gives every image and text of the panel the material of its depth test (see <see cref="OnTop"/>).</summary>
        public void ApplyDepthTest()
        {
            GetComponentsInChildren(true, s_graphics);
            foreach (Graphic graphic in s_graphics)
            {
                if (graphic is TMPro.TextMeshProUGUI text)
                {
                    Material current = text.fontSharedMaterial;
                    Material wanted = _onTop ? UiStyle.TextMaterialOnTop(current) : UiStyle.TextMaterialNormal(current);
                    if (wanted != null && wanted != current)
                    {
                        text.fontSharedMaterial = wanted;
                    }
                }
                else if (graphic is Image || graphic is RawImage)
                {
                    Material current = graphic.material;
                    bool ours = current == UiStyle.ImageMaterial || current == UiStyle.ImageMaterialOnTop || current == graphic.defaultMaterial;
                    Material wanted = _onTop ? UiStyle.ImageMaterialOnTop : UiStyle.ImageMaterial;
                    if (ours && wanted != null && wanted != current)
                    {
                        graphic.material = wanted;
                    }
                }
            }

            s_graphics.Clear();
        }

        /// <summary>Creates a panel of <paramref name="size"/> design pixels (1 px = 1 mm).</summary>
        public static UiCanvas Create(string name, Vector2 size, Transform parent = null)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);                                // the Interaction SDK parts check their wiring when enabled
            go.layer = LayerMask.NameToLayer("UI");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var panel = go.AddComponent<UiCanvas>();
            panel.Size = size;
            panel.Canvas = go.AddComponent<Canvas>();
            panel.Canvas.renderMode = RenderMode.WorldSpace;
            panel.Canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();
            panel.Root = (RectTransform)go.transform;
            panel.Root.sizeDelta = size;
            panel.Root.localScale = Vector3.one * UiStyle.MetresPerPixel;

            var pointable = go.AddComponent<PointableCanvas>();
            pointable.InjectAllPointableCanvas(panel.Canvas);

            var surfaceObject = new GameObject("Surface");
            surfaceObject.layer = go.layer;
            surfaceObject.transform.SetParent(go.transform, false);
            var plane = surfaceObject.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            var clipper = surfaceObject.AddComponent<BoundsClipper>();
            clipper.Size = new Vector3(size.x, size.y, 20f);    // local units of the canvas (design pixels)
            var clipped = surfaceObject.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { clipper });

            var ray = surfaceObject.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(clipped);
            ray.InjectOptionalSelectSurface(plane);
            ray.InjectOptionalPointableElement(pointable);

            var poke = surfaceObject.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);
            poke.InjectOptionalPointableElement(pointable);

            go.SetActive(true);
            return panel;
        }

        // ------------------------------------------------------------------ standing in front of things

        private const float PullMargin = 0.05f;         // metres left between the panel and what it comes in front of
        private const float PullMinDistance = 0.4f;     // never closer to the eyes than this
        private const float NearIgnore = 0.3f;          // hits closer than this are the learner's own hands and tools
        private static readonly RaycastHit[] s_hits = new RaycastHit[16];

        private bool _hasPlace;
        private Vector3 _place;
        private Vector3 _baseScale;
        private float _pull = 1f;
        private Transform _head;
        private Transform _rig;

        /// <summary>Where the panel is meant to be (see <see cref="Place"/>); its transform may stand closer.</summary>
        public Vector3 Placed => _hasPlace ? _place : transform.position;

        /// <summary>How far along the way from the eyes to its place the panel stands now, 0..1 (1 = at its place; for tests).</summary>
        public float Pull => _pull;

        /// <summary>
        /// Puts the panel at a place. When a machine, a table or a wall stands between the eyes and that place,
        /// the panel comes forward, just in front of it, and shrinks by as much, so that it looks the same size
        /// while the eyes see it in front, where it is drawn. (Drawn over a thing that is nearer than the panel,
        /// the two eyes would disagree and the text would double.) Called by whoever moves the panel; while it
        /// stays put the panel keeps adjusting by itself.
        /// </summary>
        public void Place(Vector3 position)
        {
            if (!_hasPlace)
            {
                _baseScale = transform.localScale;
                _hasPlace = true;
            }

            _place = position;
            ApplyPlace();
        }

        protected virtual void LateUpdate()
        {
            if (_hasPlace)
            {
                ApplyPlace();
            }
        }

        private void ApplyPlace()
        {
            if (_head == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                _head = rig != null ? rig.centerEyeAnchor : null;
                _rig = rig != null ? rig.transform : null;
            }

            float wanted = _head != null && _onTop ? Clearance() : 1f;
            // in at once (it must not stay hidden), out slowly (no jumping about at an edge)
            _pull = wanted < _pull ? wanted : Mathf.MoveTowards(_pull, wanted, Time.unscaledDeltaTime * 0.8f);
            Vector3 head = _head != null ? _head.position : _place;
            transform.position = head + (_place - head) * _pull;
            transform.localScale = _baseScale * _pull;
        }

        // The share (0..1) of the way from the eyes to the panel's place that is free, at its middle and corners.
        private float Clearance()
        {
            Vector3 head = _head.position;
            float centre = Vector3.Distance(_place, head);
            if (centre < 1e-3f)
            {
                return 1f;
            }

            // the panel's corners at its place, at full size
            float scale = (transform.parent != null ? transform.parent.lossyScale.x : 1f) * _baseScale.x;
            Vector3 right = transform.right * (Size.x * 0.5f * scale * 0.9f);
            Vector3 up = transform.up * (Size.y * 0.5f * scale * 0.9f);
            float share = Free(head, _place);
            share = Mathf.Min(share, Free(head, _place + right + up));
            share = Mathf.Min(share, Free(head, _place - right + up));
            share = Mathf.Min(share, Free(head, _place + right - up));
            share = Mathf.Min(share, Free(head, _place - right - up));
            return Mathf.Clamp(share, Mathf.Min(1f, PullMinDistance / centre), 1f);
        }

        private float Free(Vector3 head, Vector3 point)
        {
            Vector3 toPoint = point - head;
            float length = toPoint.magnitude;
            if (length < 1e-3f)
            {
                return 1f;
            }

            int ui = LayerMask.NameToLayer("UI");
            int mask = Physics.DefaultRaycastLayers & ~(ui >= 0 ? 1 << ui : 0);
            int count = Physics.RaycastNonAlloc(head, toPoint / length, s_hits, length, mask, QueryTriggerInteraction.Ignore);
            float nearest = length;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_hits[i];
                Transform t = hit.collider.transform;
                if (hit.distance < NearIgnore || hit.distance >= nearest || t.IsChildOf(transform)
                    || (_rig != null && t.IsChildOf(_rig)) || InHand(hit.collider))
                {
                    continue;
                }

                nearest = hit.distance;
            }

            return nearest >= length ? 1f : Mathf.Max(0f, nearest - PullMargin) / length;
        }

        private static bool InHand(Collider collider)
        {
            Grabbable grabbable = collider.GetComponentInParent<Grabbable>();
            return grabbable != null && grabbable.SelectingPointsCount > 0;
        }

        /// <summary>A panel that is only read: rays and fingers go through it to what is behind.</summary>
        public void MakeReadOnly()
        {
            foreach (RayInteractable ray in GetComponentsInChildren<RayInteractable>(true)) ray.enabled = false;
            foreach (PokeInteractable poke in GetComponentsInChildren<PokeInteractable>(true)) poke.enabled = false;
        }

        /// <summary>Puts the panel at a place in the world, turned so that its face looks at <paramref name="viewer"/>.</summary>
        public void Face(Vector3 position, Vector3 viewer, bool upright = true)
        {
            Vector3 away = position - viewer;
            if (upright)
            {
                away.y = 0f;
            }

            transform.SetPositionAndRotation(position, Quaternion.LookRotation(away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward, Vector3.up));
        }

        private static void EnsureEventSystem()
        {
            EventSystem system = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            if (system == null)
            {
                var go = new GameObject("EventSystem");
                system = go.AddComponent<EventSystem>();
            }

            if (system.GetComponent<PointableCanvasModule>() == null)
            {
                system.gameObject.AddComponent<PointableCanvasModule>();
            }
        }
    }

    /// <summary>
    /// Keeps a panel in front of the player without gluing it to the head: it stays where it is until the player
    /// has turned or walked away from it, then glides back into view.
    /// </summary>
    public class LazyFollow : MonoBehaviour
    {
        public float Distance = 1.1f;
        public float Height = -0.28f;           // metres relative to the eyes
        public float Side = -0.42f;             // metres to the right of the view direction (negative = left)
        public float MaxAngle = 38f;            // how far the view may turn before the panel follows
        public float MaxDrift = 0.45f;          // how far the player may walk before it follows
        public float Smooth = 0.25f;
        public bool Pinned;

        private Transform _head;
        private Vector3 _target;
        private Vector3 _velocity;
        private bool _moving = true;
        private UiCanvas _panel;

        // where the panel is meant to be (it may be drawn closer, in front of something in the way)
        private Vector3 Place
        {
            get => _panel != null ? _panel.Placed : transform.position;
            set
            {
                if (_panel != null) _panel.Place(value);
                else transform.position = value;
            }
        }

        protected virtual void Awake() => _panel = GetComponent<UiCanvas>();

        public void SnapNow()
        {
            if (FindHead())
            {
                _target = TargetNow();
                Place = _target;
                _moving = false;
                Aim();
            }
        }

        protected virtual void LateUpdate()
        {
            if (!FindHead() || Pinned)
            {
                return;
            }

            Vector3 wanted = TargetNow();
            Vector3 toPanel = Place - _head.position;
            Vector3 view = _head.forward;
            toPanel.y = 0f;
            view.y = 0f;
            float angle = Vector3.Angle(view, toPanel);
            if (!_moving && (angle > MaxAngle + 20f || Vector3.Distance(Place, wanted) > MaxDrift + 0.6f))
            {
                _moving = true;
            }

            if (_moving)
            {
                _target = wanted;
                Place = Vector3.SmoothDamp(Place, _target, ref _velocity, Smooth);
                if (Vector3.Distance(Place, _target) < 0.01f)
                {
                    _moving = false;
                }
            }

            Aim();
        }

        private bool FindHead()
        {
            if (_head == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                _head = rig != null ? rig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : null;
            }

            return _head != null;
        }

        private Vector3 TargetNow()
        {
            Vector3 forward = _head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return _head.position + forward * Distance + right * Side + Vector3.up * Height;
        }

        private void Aim()
        {
            Vector3 away = Place - _head.position;
            if (away.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }
    }
}
