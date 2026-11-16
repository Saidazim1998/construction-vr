using System;
using System.Collections.Generic;
using ConstructionVR.Player;
using ConstructionVR.Training.UI;
using NBM500U;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input.Visuals;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConstructionVR.Training
{
    /// <summary>
    /// Taking things from afar. Each controller casts a ray; when it lands on something that can be grabbed, a
    /// white line is drawn from the hand to it and the thing glows. Squeezing the grip then takes it: a loose
    /// thing flies into the hand, a knob or a hinged cover stays in its place and is worked from where the
    /// learner stands. Pulling the trigger on a piece of protective equipment puts it on.
    ///
    /// The thing is held through the same pointer events a hand's own grab sends (its Grabbable cannot tell
    /// the difference), and everything grabbable in the trainer is kinematic, so nothing is thrown about by
    /// physics on the way to the hand.
    ///
    /// The component also hides the controller models while hands are shown in their place and lets the
    /// thumbstick of the holding hand turn a knob, which is easier than twisting the wrist.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(200)]
    public class RemoteGrab : MonoBehaviour
    {
        public const float Range = 6f;                  // metres the ray reaches
        private const float NearReach = 0.12f;          // closer than this the hand takes the thing itself
        private const float AimHelp = 0.035f;           // radius around the ray within which a thing still counts as pointed at
        private const float PullSeconds = 0.25f;        // a loose thing's flight into the hand
        private const float KnobStickSpeed = 90f;       // degrees a held knob turns per second with the stick pushed fully
        private const float KnobTwistGain = 2f;         // degrees a knob turns per degree of wrist twist
        private const int FirstPointerId = 918200;

        private static readonly RaycastHit[] s_hits = new RaycastHit[48];
        private static int s_knobHints, s_wearHints;

        private class Hand
        {
            public int Index;
            public OVRInput.Controller Controller;
            public Transform Anchor;
            public Transform Side;                      // the rig's interactors of this hand
            public RayInteractor[] UiRays = Array.Empty<RayInteractor>();
            public IInteractor[] Near = Array.Empty<IInteractor>();
            public LineRenderer Line;

            public Grabbable Hover;
            public Vector3 HoverPoint;
            public Grabbable Held;
            public NBMKnobTwistTransformer Knob;
            public Action<PointerEvent> Listener;
            public bool Pulls, Cancelled;
            public float Pull;
            public Vector3 GrabPoint, HandAtGrab;
            public Pose Pose;
            public bool Grip, Trigger;

            // for tests: an aim and buttons that replace the controller's
            public Ray? TestRay;
            public bool TestGrip, TestTrigger;
        }

        private readonly Hand[] _hands = new Hand[2];
        private OVRCameraRig _rig;
        private ControllerVisual[] _controllerModels = Array.Empty<ControllerVisual>();
        private HandVisual[] _handMeshes = Array.Empty<HandVisual>();
        private PlayerBodyAnchor _body;
        private float _nextBodySearch;
        private Material _lineMaterial;
        private float _nextKnobScan;

        public static RemoteGrab Instance { get; private set; }

        // ------------------------------------------------------------------ set-up

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_knobHints = s_wearHints = 0;
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        private static void Attach()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.GetComponent<RemoteGrab>() == null)
            {
                rig.gameObject.AddComponent<RemoteGrab>();
            }
        }

        protected virtual void Awake()
        {
            Instance = this;
            _rig = GetComponent<OVRCameraRig>();
            _controllerModels = _rig.GetComponentsInChildren<ControllerVisual>(true);
            _handMeshes = _rig.GetComponentsInChildren<HandVisual>(true);

            Shader lineShader = Resources.Load<Shader>("StereoShaders/HandLine");
            if (lineShader != null)
            {
                _lineMaterial = new Material(lineShader) { hideFlags = HideFlags.HideAndDontSave };
            }

            _hands[0] = NewHand(0, OVRInput.Controller.LTouch, _rig.leftHandAnchor, "Left");
            _hands[1] = NewHand(1, OVRInput.Controller.RTouch, _rig.rightHandAnchor, "Right");
            if (GetComponent<PlaceGhost>() == null)
            {
                gameObject.AddComponent<PlaceGhost>();
            }
        }

        private Hand NewHand(int index, OVRInput.Controller controller, Transform anchor, string sideName)
        {
            var hand = new Hand { Index = index, Controller = controller, Anchor = anchor };

            // the rig keeps each hand's interactors under "...Interactors<Side>"
            foreach (HandGrabInteractor any in _rig.GetComponentsInChildren<HandGrabInteractor>(true))
            {
                Transform t = any.transform;
                while (t != null && t != _rig.transform && !(t.name.Contains("Interactors") && t.name.EndsWith(sideName)))
                {
                    t = t.parent;
                }

                if (t != null && t != _rig.transform)
                {
                    hand.Side = t;
                    break;
                }
            }

            if (hand.Side != null)
            {
                hand.UiRays = hand.Side.GetComponentsInChildren<RayInteractor>(true);
                var near = new List<IInteractor>();
                foreach (IInteractor interactor in hand.Side.GetComponentsInChildren<IInteractor>(true))
                {
                    if (interactor is GrabInteractor || interactor is HandGrabInteractor || interactor is PokeInteractor)
                    {
                        near.Add(interactor);
                    }
                }

                hand.Near = near.ToArray();
            }

            var lineObject = new GameObject("Hand Ray Line " + sideName);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = _lineMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 3;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.0045f), new Keyframe(1f, 0.0025f));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(1f, 1f) });
            line.colorGradient = gradient;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            hand.Line = line;
            return hand;
        }

        protected virtual void OnDisable()
        {
            foreach (Hand hand in _hands)
            {
                if (hand == null)
                {
                    continue;
                }

                End(hand);
                hand.Hover = null;
                if (hand.Line != null)
                {
                    hand.Line.enabled = false;
                }
            }
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
            }
        }

        // ------------------------------------------------------------------ every frame

        protected virtual void Update()
        {
            HideControllersInHands();

            if (Time.unscaledTime >= _nextKnobScan)
            {
                _nextKnobScan = Time.unscaledTime + 3f;
                foreach (NBMKnobTwistTransformer knob in FindObjectsByType<NBMKnobTwistTransformer>(FindObjectsInactive.Exclude))
                {
                    if (knob.TwistGain < KnobTwistGain)
                    {
                        knob.TwistGain = KnobTwistGain;
                    }
                }
            }

            foreach (Hand hand in _hands)
            {
                Step(hand);
            }
        }

        protected virtual void LateUpdate()
        {
            // the glow: once per thing, even with both hands on it
            Grabbable first = null;
            foreach (Hand hand in _hands)
            {
                Grabbable shown = hand.Held != null ? (hand.Pulls ? null : hand.Held) : hand.Hover != null ? hand.Hover : WithinReach(hand);
                if (shown != null && shown != first)
                {
                    ModelGlow.Draw(shown.transform, ModelGlow.Hover);
                    first = shown;
                }
            }
        }

        // A controller model is hidden while a hand is shown in its place (the body's hand, or the rig's own hand
        // mesh): two things in one grip look wrong. With no hand to show, the controller stays, so that the
        // learner always sees where their hand is.
        private void HideControllersInHands()
        {
            if (_body == null && Time.unscaledTime >= _nextBodySearch)
            {
                _nextBodySearch = Time.unscaledTime + 2f;
                _body = FindAnyObjectByType<PlayerBodyAnchor>();
            }

            bool bodyShown = _body != null && _body.isActiveAndEnabled && _body.IsTracked;
            foreach (ControllerVisual model in _controllerModels)
            {
                if (model == null)
                {
                    continue;
                }

                bool left = model.name.Contains("Left");
                bool handShown = bodyShown;
                for (int i = 0; i < _handMeshes.Length && !handShown; i++)
                {
                    HandVisual mesh = _handMeshes[i];
                    handShown = mesh != null && mesh.IsVisible && mesh.name.Contains("Left") == left;
                }

                if (model.ForceOffVisibility != handShown)
                {
                    model.ForceOffVisibility = handShown;
                }
            }
        }

        private void Step(Hand hand)
        {
            bool connected = hand.TestRay.HasValue || OVRInput.IsControllerConnected(hand.Controller);
            bool grip = hand.TestGrip || (connected && Pressed(OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand.Controller), hand.Grip));
            bool trigger = hand.TestTrigger || (connected && Pressed(OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand.Controller), hand.Trigger));
            bool gripDown = grip && !hand.Grip, triggerDown = trigger && !hand.Trigger;
            hand.Grip = grip;
            hand.Trigger = trigger;

            if (hand.Held != null)
            {
                if (!grip || hand.Cancelled || !hand.Held.isActiveAndEnabled || TakenByHand(hand))
                {
                    End(hand);          // (taken by the hand itself: it goes on holding, this pointer just lets go)
                }
                else
                {
                    Carry(hand);
                }
            }

            TurnKnobWithStick(hand);
            if (hand.Held != null)
            {
                return;
            }

            // nothing in the hand: look along the ray
            hand.Hover = null;
            if (connected && !Busy(hand) && Aim(hand, out Ray ray, out float reach)
                && Pick(ray, reach, out Grabbable target, out Vector3 point))
            {
                hand.Hover = target;
                hand.HoverPoint = point;
                ShowLine(hand, ray.origin, point);

                var wearable = target.GetComponent<WearableItem>();
                if (wearable != null && s_wearHints < 2 && AlertPanel.Instance != null)
                {
                    s_wearHints++;
                    AlertPanel.Instance.Hint(Loc.T("ppe.hint.ray"));
                }

                if (triggerDown && wearable != null)
                {
                    wearable.WearNow();
                    hand.Hover = null;
                }
                else if (gripDown)
                {
                    Begin(hand);
                }
            }

            if (hand.Hover == null && hand.Held == null)
            {
                hand.Line.enabled = false;
            }
        }

        private static bool Pressed(float value, bool was) => value > (was ? 0.35f : 0.55f);

        // True while the hand is busy with something of its own: it holds or presses a thing, or one is within reach.
        private static bool Busy(Hand hand)
        {
            foreach (IInteractor interactor in hand.Near)
            {
                var behaviour = interactor as MonoBehaviour;
                if (behaviour == null || !behaviour.isActiveAndEnabled)
                {
                    continue;
                }

                // (a grip squeezed in the empty air also reads as "selecting": that hand is free)
                if (interactor.State == InteractorState.Hover
                    || (interactor.State == InteractorState.Select && (interactor is PokeInteractor || Selected(interactor) != null)))
                {
                    return true;
                }
            }

            foreach (RayInteractor ui in hand.UiRays)
            {
                if (ui != null && ui.isActiveAndEnabled && ui.State == InteractorState.Select)
                {
                    return true;                                // a panel button or slider is being pressed
                }
            }

            return false;
        }

        // Where the hand points, and how far the ray may go before a panel of the interface is in the way.
        private bool Aim(Hand hand, out Ray ray, out float reach)
        {
            reach = Range;
            if (hand.TestRay.HasValue)
            {
                ray = hand.TestRay.Value;
                return true;
            }

            ray = new Ray(hand.Anchor.position, hand.Anchor.forward);
            foreach (RayInteractor ui in hand.UiRays)
            {
                if (ui == null || !ui.isActiveAndEnabled || ui.Forward.sqrMagnitude < 0.5f)
                {
                    continue;
                }

                ray = new Ray(ui.Origin, ui.Forward);            // the same ray the interface is pointed at with
                if (ui.State == InteractorState.Hover)
                {
                    reach = Mathf.Min(reach, Vector3.Distance(ui.Origin, ui.End));
                }

                break;
            }

            return true;
        }

        // The grabbable thing the ray lands on: the first thing straight along it or, failing that, the one
        // closest to the ray among those it passes within a few centimetres of (small things are hard to hit).
        private bool Pick(Ray ray, float reach, out Grabbable target, out Vector3 point)
        {
            target = null;
            point = default;

            float wall = reach;
            Grabbable direct = null;
            Vector3 directPoint = default;
            int count = Physics.RaycastNonAlloc(ray, s_hits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_hits[i];
                if (hit.distance >= wall || hit.collider.transform.IsChildOf(transform))
                {
                    continue;                                   // the rig's own colliders are not in the way
                }

                wall = hit.distance;
                direct = GrabbableOf(hit.collider);
                directPoint = hit.point;
            }

            if (direct != null)
            {
                if (wall < NearReach || direct.SelectingPointsCount > 0)
                {
                    return false;
                }

                target = direct;
                point = directPoint;
                return true;
            }

            float best = float.MaxValue;
            count = Physics.SphereCastNonAlloc(ray, AimHelp, s_hits, Mathf.Min(reach, wall + 0.05f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_hits[i];
                if (hit.distance < NearReach || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                Grabbable candidate = GrabbableOf(hit.collider);
                if (candidate == null || candidate.SelectingPointsCount > 0)
                {
                    continue;
                }

                float off = Vector3.Cross(ray.direction, hit.point - ray.origin).magnitude;
                if (off < best)
                {
                    best = off;
                    target = candidate;
                    point = hit.point;
                }
            }

            return target != null;
        }

        private static Grabbable GrabbableOf(Collider collider)
        {
            Rigidbody body = collider.attachedRigidbody;
            Grabbable grabbable = body != null ? body.GetComponent<Grabbable>() : null;
            if (grabbable == null)
            {
                grabbable = collider.GetComponentInParent<Grabbable>();
            }

            return grabbable != null && grabbable.isActiveAndEnabled ? grabbable : null;
        }

        private void ShowLine(Hand hand, Vector3 from, Vector3 to)
        {
            hand.Line.SetPosition(0, from);
            hand.Line.SetPosition(1, to);
            hand.Line.enabled = true;
        }

        // ------------------------------------------------------------------ holding

        private Pose HandPose(Hand hand)
        {
            if (hand.TestRay.HasValue)
            {
                Ray ray = hand.TestRay.Value;
                return new Pose(ray.origin, Quaternion.LookRotation(ray.direction, Vector3.up));
            }

            return new Pose(hand.Anchor.position, hand.Anchor.rotation);
        }

        private void Begin(Hand hand)
        {
            Grabbable thing = hand.Hover;
            hand.Hover = null;
            hand.Held = thing;
            hand.Knob = thing.GetComponent<NBMKnobTwistTransformer>();
            hand.Pulls = hand.Knob == null && thing.GetComponent<OneGrabRotateTransformer>() == null;   // knobs and hinges stay in their place
            hand.Pull = 0f;
            hand.Cancelled = false;

            Pose pose = HandPose(hand);
            hand.GrabPoint = hand.HoverPoint;
            hand.HandAtGrab = pose.position;
            hand.Pose = new Pose(hand.GrabPoint, pose.rotation);

            int id = FirstPointerId + hand.Index;
            hand.Listener = evt =>
            {
                if (evt.Identifier == id && evt.Type == PointerEventType.Cancel)
                {
                    hand.Cancelled = true;                      // taken over, e.g. by the other hand
                }
            };
            thing.WhenPointerEventRaised += hand.Listener;
            Send(hand, PointerEventType.Hover);
            Send(hand, PointerEventType.Select);

            if (hand.Knob != null && s_knobHints < 3 && AlertPanel.Instance != null)
            {
                s_knobHints++;
                AlertPanel.Instance.Hint(Loc.T("knob.hint"));
            }
        }

        private void Carry(Hand hand)
        {
            Pose pose = HandPose(hand);
            Vector3 position;
            if (hand.Pulls)
            {
                hand.Pull = Mathf.MoveTowards(hand.Pull, 1f, Time.deltaTime / PullSeconds);
                float eased = 1f - Mathf.Pow(1f - hand.Pull, 3f);
                position = Vector3.Lerp(hand.GrabPoint, pose.position, eased);
                hand.Line.enabled = false;
            }
            else
            {
                position = hand.GrabPoint + (pose.position - hand.HandAtGrab);      // worked from afar: the hand's movement, one to one
                ShowLine(hand, pose.position, hand.Held.transform.position);
            }

            hand.Pose = new Pose(position, pose.rotation);
            Send(hand, PointerEventType.Move);
        }

        private void End(Hand hand)
        {
            Grabbable thing = hand.Held;
            if (thing == null)
            {
                return;
            }

            hand.Held = null;
            hand.Knob = null;
            thing.WhenPointerEventRaised -= hand.Listener;
            hand.Listener = null;
            hand.Line.enabled = false;
            if (!hand.Cancelled)
            {
                int id = FirstPointerId + hand.Index;
                thing.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Unselect, hand.Pose));
                thing.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Unhover, hand.Pose));
            }

            hand.Cancelled = false;
        }

        // A thing that has flown into the hand is within the reach of the hand's own grab, which takes it (the
        // grip is still squeezed). From then on the hand holds it like anything picked up directly.
        private static bool TakenByHand(Hand hand)
        {
            foreach (IInteractor interactor in hand.Near)
            {
                if (interactor.State != InteractorState.Select)
                {
                    continue;
                }

                Component selected = Selected(interactor);
                if (selected != null && selected.GetComponentInParent<Grabbable>() == hand.Held)
                {
                    return true;
                }
            }

            return false;
        }

        // The thing the hand itself is about to take (it is close enough to grab directly): it glows as well.
        private static Grabbable WithinReach(Hand hand)
        {
            foreach (IInteractor interactor in hand.Near)
            {
                if (interactor.State != InteractorState.Hover)
                {
                    continue;
                }

                Component candidate = interactor is GrabInteractor grab ? grab.Candidate
                    : interactor is HandGrabInteractor handGrab ? handGrab.Candidate : (Component)null;
                Grabbable grabbable = candidate != null ? candidate.GetComponentInParent<Grabbable>() : null;
                if (grabbable != null)
                {
                    return grabbable;
                }
            }

            return null;
        }

        private static Component Selected(IInteractor interactor)
        {
            return interactor is GrabInteractor grab ? grab.SelectedInteractable
                : interactor is HandGrabInteractor handGrab ? handGrab.SelectedInteractable : (Component)null;
        }

        private static void Send(Hand hand, PointerEventType type)
        {
            hand.Held.ProcessPointerEvent(new PointerEvent(FirstPointerId + hand.Index, type, hand.Pose));
        }

        // ------------------------------------------------------------------ knobs

        // The knob this hand holds, whether from afar or in its own grip.
        private static NBMKnobTwistTransformer HeldKnob(Hand hand)
        {
            if (hand.Held != null)
            {
                return hand.Knob;
            }

            foreach (IInteractor interactor in hand.Near)
            {
                if (interactor.State != InteractorState.Select)
                {
                    continue;
                }

                Component selected = Selected(interactor);
                if (selected != null && selected.TryGetComponent(out NBMKnobTwistTransformer knob))
                {
                    return knob;
                }
            }

            return null;
        }

        private static void TurnKnobWithStick(Hand hand)
        {
            NBMKnobTwistTransformer knob = HeldKnob(hand);
            if (knob == null)
            {
                return;
            }

            float stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, hand.Controller).x;
            if (Mathf.Abs(stick) > 0.25f)
            {
                // (squared: a light push turns slowly, for the last few units)
                knob.SetAngle(knob.Angle + stick * Mathf.Abs(stick) * KnobStickSpeed * Time.deltaTime * knob.IncreaseSign);
            }
        }

        /// <summary>True while that controller's thumbstick turns a held knob (it must not move the player then).</summary>
        public static bool StickBusy(OVRInput.Controller controller)
        {
            if (Instance == null)
            {
                return false;
            }

            foreach (Hand hand in Instance._hands)
            {
                if (hand != null && hand.Controller == controller && HeldKnob(hand) != null)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ for tests

        /// <summary>Replaces a hand's aim (0 left, 1 right) with a ray; the hand is taken to be at the ray's origin.</summary>
        public void TestAim(int hand, Vector3 origin, Vector3 direction) => _hands[hand].TestRay = new Ray(origin, direction.normalized);

        public void TestGrip(int hand, bool pressed) => _hands[hand].TestGrip = pressed;

        public void TestTrigger(int hand, bool pressed) => _hands[hand].TestTrigger = pressed;

        /// <summary>Gives the hands back to the controllers.</summary>
        public void TestClear()
        {
            foreach (Hand hand in _hands)
            {
                hand.TestRay = null;
                hand.TestGrip = hand.TestTrigger = false;
            }
        }

        /// <summary>What each hand points at and holds, as one line of text.</summary>
        public string TestState()
        {
            var text = new System.Text.StringBuilder();
            foreach (Hand hand in _hands)
            {
                text.Append(hand.Index == 0 ? "L" : " | R");
                text.Append(" hover=").Append(hand.Hover != null ? hand.Hover.name : "-");
                text.Append(" held=").Append(hand.Held != null ? hand.Held.name + (hand.Pulls ? " (pull " + hand.Pull.ToString("0.00") + ")" : " (in place)") : "-");
                text.Append(" line=").Append(hand.Line.enabled);
                text.Append(" side=").Append(hand.Side != null ? hand.Side.name : "none").Append(" near=").Append(hand.Near.Length).Append(" ui=").Append(hand.UiRays.Length);
            }

            int hidden = 0;
            foreach (ControllerVisual model in _controllerModels)
            {
                if (model != null && model.ForceOffVisibility) hidden++;
            }

            text.Append(" | controller models hidden ").Append(hidden).Append('/').Append(_controllerModels.Length);
            return text.ToString();
        }
    }
}
