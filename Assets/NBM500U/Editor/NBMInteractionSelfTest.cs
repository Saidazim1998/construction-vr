using System;
using System.Collections;
using System.Linq;
using System.Text;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NBM500U.EditorTools
{
    /// <summary>
    /// Play-mode check of the NBM-500U interactions without a headset. It drives synthetic Meta
    /// interactors (a PokeInteractor and a controller-style GrabInteractor) through the scene:
    /// presses every button, turns every knob, swings the hinged parts and carries the torch.
    /// It does not cover tracked-hand grabbing (HandGrabInteractor needs real hand data).
    /// Enter Play mode in the NBM500U_VR scene, then run Tools > NBM500U > Run Interaction Self-Test.
    /// </summary>
    public static class NBMInteractionSelfTest
    {
        public static string Result { get; private set; } = "not run";
        public static bool IsRunning { get; private set; }

        private static IEnumerator _routine;
        private static int _lastFrame;
        private static StringBuilder _log;
        private static int _passed;
        private static int _failed;

        [MenuItem("Tools/NBM500U/Run Interaction Self-Test (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                Result = "ERROR: enter Play mode first.";
                Debug.LogError("[NBM500U self-test] " + Result);
                return;
            }

            if (IsRunning)
            {
                return;
            }

            _log = new StringBuilder();
            _passed = 0;
            _failed = 0;
            IsRunning = true;
            Result = "running";
            _routine = Routine();
            _lastFrame = Time.frameCount;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                Finish("aborted: left Play mode");
                return;
            }

            // One step of the test per game frame.
            if (Time.frameCount == _lastFrame)
            {
                return;
            }

            _lastFrame = Time.frameCount;
            try
            {
                if (!_routine.MoveNext())
                {
                    Finish(null);
                }
            }
            catch (Exception e)
            {
                _failed++;
                _log.AppendLine("EXCEPTION " + e);
                Finish(null);
            }
        }

        private static void Finish(string note)
        {
            EditorApplication.update -= Tick;
            IsRunning = false;
            foreach (string name in new[] { "__SelfTestPoke", "__SelfTestGrab" })
            {
                GameObject go = GameObject.Find(name);
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            Result = $"[NBM500U self-test] {(note ?? "finished")}: {_passed} passed, {_failed} failed\n{_log}";
            if (_failed > 0 || note != null)
            {
                Debug.LogError(Result);
            }
            else
            {
                Debug.Log(Result);
            }
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (ok)
            {
                _passed++;
            }
            else
            {
                _failed++;
            }

            _log.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}: {detail}");
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator Seconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
            }
        }

        // Lets physics run so trigger overlaps reach the interaction registry.
        private static IEnumerator Physics()
        {
            float end = Time.fixedTime + Time.fixedDeltaTime * 3f;
            int guard = 0;
            while (Time.fixedTime < end && guard++ < 600)
            {
                yield return null;
            }

            yield return null;
            yield return null;
        }

        private static IEnumerator Run(IEnumerator inner)
        {
            while (inner.MoveNext())
            {
                if (inner.Current is IEnumerator nested)
                {
                    IEnumerator runNested = Run(nested);
                    while (runNested.MoveNext())
                    {
                        yield return null;
                    }
                }
                else
                {
                    yield return null;
                }
            }
        }

        private static IEnumerator Routine()
        {
            return Run(Tests());
        }

        private static IEnumerator Tests()
        {
            yield return Frames(5);
            yield return TestButtons();
            yield return TestGrabs();
        }

        // ---------------------------------------------------------------- buttons

        private static IEnumerator TestButtons()
        {
            NBMButton[] buttons = Object.FindObjectsByType<NBMButton>()
                .OrderBy(b => b.ButtonId, StringComparer.Ordinal).ToArray();
            Check("button count", buttons.Length == 15, buttons.Length + " NBMButton components in the scene");

            const float radius = 0.005f;
            var pokeObject = new GameObject("__SelfTestPoke");
            pokeObject.SetActive(false);
            var point = new GameObject("Point").transform;
            point.SetParent(pokeObject.transform, false);
            var poke = pokeObject.AddComponent<PokeInteractor>();
            poke.InjectAllPokeInteractor(point, radius);
            pokeObject.SetActive(true);
            yield return Frames(3);

            foreach (NBMButton button in buttons)
            {
                Transform surface = button.transform;
                Vector3 normal = -surface.forward;
                Transform cap = surface.parent.Find(button.ButtonId);
                float restOffset = Vector3.Dot(cap.position - surface.position, normal);
                float minOffset = restOffset;
                int pressesBefore = button.PressCount;
                bool hovered = false;

                // Fingertip comes straight down on the cap from 4 cm away until it is 3 mm into the surface.
                for (float d = 0.04f; d >= radius - 0.003f; d -= 0.0015f)
                {
                    pokeObject.transform.position = surface.position + normal * d;
                    yield return null;
                    hovered |= button.PokeInteractable.State == InteractableState.Hover;
                    minOffset = Mathf.Min(minOffset, Vector3.Dot(cap.position - surface.position, normal));
                }

                yield return Frames(2);
                minOffset = Mathf.Min(minOffset, Vector3.Dot(cap.position - surface.position, normal));
                bool pressed = button.PressCount == pressesBefore + 1 && button.IsPressed;

                for (float d = radius - 0.003f; d <= 0.04f; d += 0.003f)
                {
                    pokeObject.transform.position = surface.position + normal * d;
                    yield return null;
                }

                yield return Frames(3);
                float endOffset = Vector3.Dot(cap.position - surface.position, normal);
                bool released = !button.IsPressed;
                bool capMoved = minOffset < restOffset * 0.25f;
                bool capBack = Mathf.Abs(endOffset - restOffset) < 0.0003f;

                Check("button " + button.ButtonId, hovered && pressed && released && capMoved && capBack,
                    $"hover={hovered} pressed={pressed} released={released} " +
                    $"cap travel {(restOffset - minOffset) * 1000f:F1} mm of {restOffset * 1000f:F1} mm, back at rest={capBack}");
            }

            Object.Destroy(pokeObject);
            yield return null;
        }

        // ------------------------------------------------------------------ grabs

        private static GameObject _grabObject;
        private static GrabInteractor _grab;
        private static VirtualSelector _selector;

        private static IEnumerator TestGrabs()
        {
            _grabObject = new GameObject("__SelfTestGrab");
            _grabObject.SetActive(false);
            var body = _grabObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var sphere = _grabObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.015f;
            _selector = _grabObject.AddComponent<VirtualSelector>();
            _grab = _grabObject.AddComponent<GrabInteractor>();
            _grab.InjectAllGrabInteractor(_selector, body);
            _grabObject.transform.position = new Vector3(0f, 3f, 0f);
            _grabObject.SetActive(true);
            yield return Frames(3);

            foreach (NBMKnobTwistTransformer knob in Object
                         .FindObjectsByType<NBMKnobTwistTransformer>()
                         .OrderBy(k => k.name, StringComparer.Ordinal))
            {
                yield return TestKnob(knob);
            }

            foreach (OneGrabRotateTransformer hinge in Object
                         .FindObjectsByType<OneGrabRotateTransformer>()
                         .OrderBy(h => h.name, StringComparer.Ordinal))
            {
                yield return TestHinge(hinge);
            }

            NBMReturnToRest torch = Object.FindAnyObjectByType<NBMReturnToRest>();
            if (torch == null)
            {
                Check("torch", false, "no NBMReturnToRest (torch) in the scene");
            }
            else
            {
                yield return TestTorch(torch);
            }

            Object.Destroy(_grabObject);
            yield return null;
        }

        private static IEnumerator Grab(GrabInteractable interactable, Vector3 position, Quaternion rotation,
            Action<bool, string> report)
        {
            _grabObject.transform.SetPositionAndRotation(position, rotation);
            yield return Physics();
            bool hover = _grab.State == InteractorState.Hover && ReferenceEquals(_grab.Candidate, interactable);
            _selector.Select();
            yield return Frames(3);
            bool selected = _grab.State == InteractorState.Select &&
                            ReferenceEquals(_grab.SelectedInteractable, interactable);
            report(hover && selected, $"hover={hover} select={selected}");
        }

        private static IEnumerator Release()
        {
            _selector.Unselect();
            yield return Frames(3);
            _grabObject.transform.position = new Vector3(0f, 3f, 0f);
            yield return Physics();
        }

        private static IEnumerator TestKnob(NBMKnobTwistTransformer knob)
        {
            Transform t = knob.transform;
            Vector3 axisLocal = Vector3.zero;
            axisLocal[(int)knob.Axis] = 1f;
            Vector3 axis = t.TransformDirection(axisLocal);
            Quaternion startRotation = t.rotation;
            Vector3 startPosition = t.position;
            Vector3 center = t.GetComponent<Collider>().bounds.center;

            bool grabbed = false;
            string grabDetail = "";
            yield return Grab(t.GetComponent<GrabInteractable>(), center, Quaternion.identity,
                (ok, detail) => { grabbed = ok; grabDetail = detail; });

            // Twist the hand 60 degrees about the knob axis, in place.
            for (int i = 1; i <= 12; i++)
            {
                _grabObject.transform.rotation = Quaternion.AngleAxis(5f * i, axis);
                yield return null;
            }

            yield return Seconds(0.5f);
            float measured = TwistAngle(t.rotation * Quaternion.Inverse(startRotation), axis);
            float drift = (t.position - startPosition).magnitude;
            float reported = knob.Angle;
            yield return Release();
            float afterRelease = TwistAngle(t.rotation * Quaternion.Inverse(startRotation), axis);

            Check("knob " + knob.name,
                grabbed && Mathf.Abs(measured - 60f) < 2f && Mathf.Abs(reported - 60f) < 2f && drift < 0.0005f &&
                Mathf.Abs(afterRelease - measured) < 0.5f,
                $"{grabDetail}; hand twist 60 deg -> knob {measured:F1} deg (Angle={reported:F1}), " +
                $"position drift {drift * 1000f:F2} mm, after release {afterRelease:F1} deg");
        }

        private static IEnumerator TestHinge(OneGrabRotateTransformer hinge)
        {
            Transform t = hinge.transform;
            Vector3 axis = t.TransformDirection(Vector3.up);
            Vector3 pivot = t.position;
            Quaternion startRotation = t.rotation;
            float min = hinge.Constraints.MinAngle.Value;
            float max = hinge.Constraints.MaxAngle.Value;
            float open = Mathf.Abs(max) > Mathf.Abs(min) ? max : min;
            float sign = Mathf.Sign(open);
            Vector3 grabPoint = t.GetComponent<Collider>().bounds.center;

            bool grabbed = false;
            string grabDetail = "";
            yield return Grab(t.GetComponent<GrabInteractable>(), grabPoint, Quaternion.identity,
                (ok, detail) => { grabbed = ok; grabDetail = detail; });

            // The hand follows the arc about the hinge line: to 60 degrees, then past the limit, then back.
            IEnumerator Sweep(float from, float to)
            {
                int steps = Mathf.CeilToInt(Mathf.Abs(to - from) / 4f);
                for (int i = 1; i <= steps; i++)
                {
                    float angle = Mathf.Lerp(from, to, i / (float)steps);
                    _grabObject.transform.position = pivot + Quaternion.AngleAxis(angle, axis) * (grabPoint - pivot);
                    yield return null;
                }

                yield return Seconds(0.4f);
            }

            yield return Sweep(0f, 60f * sign);
            float at60 = TwistAngle(t.rotation * Quaternion.Inverse(startRotation), axis);
            yield return Sweep(60f * sign, (Mathf.Abs(open) + 30f) * sign);
            float atLimit = TwistAngle(t.rotation * Quaternion.Inverse(startRotation), axis);
            yield return Sweep((Mathf.Abs(open) + 30f) * sign, -20f * sign);
            float closed = TwistAngle(t.rotation * Quaternion.Inverse(startRotation), axis);
            yield return Release();

            Check("hinge " + hinge.name,
                grabbed && Mathf.Abs(at60 - 60f * sign) < 3f && Mathf.Abs(atLimit - open) < 1.5f &&
                Mathf.Abs(closed) < 1.5f,
                $"{grabDetail}; hand at {60f * sign:F0} deg -> {at60:F1}; pushed past the limit -> {atLimit:F1} " +
                $"(limit {open:F0}); pulled back past closed -> {closed:F1}");
        }

        private static IEnumerator TestTorch(NBMReturnToRest torch)
        {
            Transform t = torch.transform;
            Transform handle = t.Find("Torch_Handle");
            Vector3 restPosition = t.position;
            Quaternion restRotation = t.rotation;
            Vector3 handleCenter = handle.GetComponent<Collider>().bounds.center;

            bool grabbed = false;
            string grabDetail = "";
            Quaternion handRotation = Quaternion.Euler(20f, -30f, 0f);
            yield return Grab(t.GetComponent<GrabInteractable>(), handleCenter, handRotation,
                (ok, detail) => { grabbed = ok; grabDetail = detail; });
            yield return Seconds(0.6f);

            // The grab source is Torch_Handle: the handle snaps into the hand.
            float snapPosition = (handle.position - _grabObject.transform.position).magnitude;
            float snapAngle = Quaternion.Angle(handle.rotation, _grabObject.transform.rotation);

            Vector3 from = _grabObject.transform.position;
            Vector3 to = new Vector3(-0.35f, 1.15f, -0.75f);
            Quaternion toRotation = Quaternion.Euler(-10f, 40f, 15f);
            for (int i = 1; i <= 20; i++)
            {
                _grabObject.transform.SetPositionAndRotation(Vector3.Lerp(from, to, i / 20f),
                    Quaternion.Slerp(handRotation, toRotation, i / 20f));
                yield return null;
            }

            yield return Seconds(0.5f);
            float carryPosition = (handle.position - to).magnitude;
            float carryAngle = Quaternion.Angle(handle.rotation, toRotation);
            float carried = (t.position - restPosition).magnitude;
            Transform neck = t.Find("Torch_Neck");
            bool partsFollow = neck != null && neck.parent == t;

            _selector.Unselect();
            yield return Frames(3);
            _grabObject.transform.position = new Vector3(0f, 3f, 0f);
            yield return Seconds(1.5f);
            float backPosition = (t.position - restPosition).magnitude;
            float backAngle = Quaternion.Angle(t.rotation, restRotation);

            Check("torch grab and carry",
                grabbed && snapPosition < 0.003f && snapAngle < 1.5f && carryPosition < 0.003f && carryAngle < 1.5f &&
                carried > 0.3f && partsFollow,
                $"{grabDetail}; handle to hand {snapPosition * 1000f:F1} mm / {snapAngle:F1} deg; after carrying " +
                $"{carried:F2} m: {carryPosition * 1000f:F1} mm / {carryAngle:F1} deg; parts follow={partsFollow}");
            Check("torch returns to rest when released",
                torch.IsAtRest && backPosition < 0.001f && backAngle < 0.5f,
                $"IsAtRest={torch.IsAtRest}, {backPosition * 1000f:F1} mm / {backAngle:F1} deg from rest pose");
        }

        private static float TwistAngle(Quaternion q, Vector3 axis)
        {
            float projection = Vector3.Dot(new Vector3(q.x, q.y, q.z), axis.normalized);
            float angle = 2f * Mathf.Atan2(projection, q.w) * Mathf.Rad2Deg;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
