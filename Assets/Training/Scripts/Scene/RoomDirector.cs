using System.Collections.Generic;
using ConstructionVR.Player;
using ConstructionVR.Training.UI;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ConstructionVR.Training
{
    /// <summary>
    /// The engineers' room part of the practice (stages 0, 1 and the first half of 2): the main window to log in
    /// and pick the practice, the briefing journal that is read and signed with the pen, the protective
    /// equipment on the stand, and the door to the welding hall that stays shut until all of it is done.
    /// </summary>
    public partial class RoomDirector : MonoBehaviour
    {
        [Header("Main window")]
        [SerializeField] private Transform _hubAnchor;

        [Header("Journal (stage 1)")]
        [SerializeField] private Transform _journalPanelAnchor;
        [Tooltip("A box (position, rotation, scale) over the signature field of the journal: X and Z along the page, Y up.")]
        [SerializeField] private Transform _signZone;
        [SerializeField] private Grabbable _pen;
        [SerializeField] private MeshFilter _penMesh;
        [SerializeField] private float _signatureLength = 0.06f;

        [Header("Door to the welding hall (stage 2)")]
        [SerializeField] private Transform _doorPanelAnchor;
        [SerializeField] private float _doorDistance = 0.9f;
        [SerializeField] private string _hallScene = "Factory";

        public static RoomDirector Instance { get; private set; }

        /// <summary>Metres of ink the learner has drawn in the signature field.</summary>
        public float InkLength { get; private set; }
        public bool Signed { get; private set; }
        public bool ReadTicked { get; private set; }

        private PlayerOutfit _outfit;
        private OVRCameraRig _rig;
        private UiCanvas _journalPanel;
        private UiCanvas _doorPanel;
        private RectTransform _journalContent;
        private RectTransform _doorContent;
        private LineRenderer _stroke;
        private LineRenderer _frame;
        private readonly List<LineRenderer> _strokes = new List<LineRenderer>();
        private Vector3 _tipA, _tipB;
        private Vector3 _lastInk;
        private Material _inkMaterial;
        private float _nextCheck;
        private float _doorCooldown;
        private bool _loading;

        protected virtual void Awake() => Instance = this;

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (StepGuide.Source == Guide)
            {
                StepGuide.Source = null;
            }
        }

        protected virtual void Start()
        {
            StepGuide.Source = Guide;                       // the arrow that shows where the current step is done
            _rig = FindAnyObjectByType<OVRCameraRig>();
            _outfit = FindAnyObjectByType<PlayerOutfit>();
            Vector3 eyes = _rig != null ? _rig.transform.position + Vector3.up * 1.6f : Vector3.up * 1.6f;

            TrainingUi.Ensure();
            HubPanel.Create(_hubAnchor.position, eyes);
            if (GetComponent<PpeGuide>() == null)
            {
                gameObject.AddComponent<PpeGuide>();        // shows where each piece of equipment is put on
            }

            _journalPanel = UiCanvas.Create("Journal Panel", new Vector2(820, 640));
            _journalPanel.OnTop = false;                        // fixed on the desk, signed with the pen in front of it
            _journalPanel.transform.SetPositionAndRotation(_journalPanelAnchor.position, _journalPanelAnchor.rotation);
            _journalContent = Ui.Fill("Content", _journalPanel.Root);

            _doorPanel = UiCanvas.Create("Door Panel", new Vector2(700, 420));
            _doorPanel.OnTop = false;                           // part of the door
            _doorPanel.transform.SetPositionAndRotation(_doorPanelAnchor.position, _doorPanelAnchor.rotation);
            _doorContent = Ui.Fill("Content", _doorPanel.Root);

            if (_penMesh != null)
            {
                Bounds b = _penMesh.sharedMesh.bounds;                      // the pen's two ends, along its longest side
                Vector3 axis = b.extents.x >= b.extents.y && b.extents.x >= b.extents.z ? Vector3.right : b.extents.y >= b.extents.z ? Vector3.up : Vector3.forward;
                float half = Vector3.Dot(b.extents, axis);
                _tipA = b.center + axis * half;
                _tipB = b.center - axis * half;
            }

            _inkMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.05f, 0.1f, 0.45f, 1f) };
            _frame = NewLine("Signature Frame", 0.0025f);
            _frame.loop = true;
            _frame.positionCount = 4;
            Vector3[] corners = { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            for (int i = 0; i < 4; i++)
            {
                _frame.SetPosition(i, _signZone.TransformPoint(corners[i]) + Vector3.up * 0.0015f);
            }

            RebuildPanels();
        }

        protected virtual void OnEnable()
        {
            TrainingSession.Changed += RebuildPanels;
            Loc.Changed += RebuildPanels;
        }

        protected virtual void OnDisable()
        {
            TrainingSession.Changed -= RebuildPanels;
            Loc.Changed -= RebuildPanels;
        }

        protected virtual void Update()
        {
            TrainingSession session = TrainingSession.Current;
            UpdateSigning(session);

            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + 0.25f;
                if (session != null && _outfit != null && session.IsOpen(StageId.Ppe) && _outfit.IsFullyDressed && !session.IsDone("ppe.wear"))
                {
                    session.Complete("ppe.wear");
                }

                // walking up to the door asks for the hall, like pressing the button on its panel
                if (_rig != null && !_loading && Time.time >= _doorCooldown)
                {
                    Vector3 d = _rig.centerEyeAnchor.position - _doorPanelAnchor.position;
                    d.y = 0f;
                    if (d.magnitude < _doorDistance)
                    {
                        TryEnterHall();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ journal

        /// <summary>The "Yo'riqnoma bilan tanishdim" box of the journal panel.</summary>
        public void SetRead(bool read)
        {
            TrainingSession session = TrainingSession.Current;
            if (read && (session == null || !session.IsDone("login.brief")))
            {
                if (session != null) session.Blocked("blocked.journal.login");
                else AlertPanel.Instance?.Push(EventKind.Blocked, Loc.T("blocked.journal.login"));
                read = false;
            }

            ReadTicked = read;
            if (read)
            {
                session.Complete("journal.read");
            }

            RebuildPanels();
        }

        private void UpdateSigning(TrainingSession session)
        {
            if (Signed || _pen == null || _signZone == null)
            {
                return;
            }

            bool held = _pen.SelectingPointsCount > 0;
            bool touching = false;
            Vector3 point = default;
            if (held)
            {
                foreach (Vector3 tip in new[] { _tipA, _tipB })
                {
                    Vector3 local = _signZone.InverseTransformPoint(_penMesh.transform.TransformPoint(tip));
                    if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f && Mathf.Abs(local.y) <= 0.5f)
                    {
                        touching = true;
                        point = _signZone.TransformPoint(new Vector3(local.x, 0f, local.z)) + Vector3.up * 0.002f;
                        break;
                    }
                }
            }

            if (!touching)
            {
                _stroke = null;
                return;
            }

            if (_stroke == null)
            {
                _stroke = NewLine("Ink", 0.002f);
                _strokes.Add(_stroke);
                _stroke.positionCount = 1;
                _stroke.SetPosition(0, point);
                _lastInk = point;
                return;
            }

            float step = Vector3.Distance(point, _lastInk);
            if (step < 0.002f)
            {
                return;
            }

            _stroke.positionCount++;
            _stroke.SetPosition(_stroke.positionCount - 1, point);
            _lastInk = point;
            InkLength += step;
            if (InkLength < _signatureLength)
            {
                return;
            }

            if (session == null || !session.IsDone("journal.read"))
            {
                if (session != null) session.Blocked("blocked.journal.read");
                else AlertPanel.Instance?.Push(EventKind.Blocked, Loc.T("blocked.journal.login"));
                ClearInk();
                return;
            }

            if (session.Complete("journal.sign"))
            {
                Signed = true;
                RebuildPanels();
            }
        }

        private void ClearInk()
        {
            foreach (LineRenderer line in _strokes)
            {
                if (line != null) Destroy(line.gameObject);
            }

            _strokes.Clear();
            _stroke = null;
            InkLength = 0f;
        }

        private LineRenderer NewLine(string lineName, float width)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.sharedMaterial = _inkMaterial;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);   // flat on the page
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        // ------------------------------------------------------------------ door

        /// <summary>Asks to go to the welding hall. Refused, with the reason, until the journal is signed and everything is worn.</summary>
        public bool TryEnterHall()
        {
            TrainingSession session = TrainingSession.Current;
            _doorCooldown = Time.time + 6f;
            if (session == null || !session.IsDone("login.brief"))
            {
                AlertPanel.Instance?.Push(EventKind.Blocked, Loc.T("blocked.door.login"));
                return false;
            }

            if (!session.IsDone("journal.sign"))
            {
                session.Blocked("blocked.door.journal");
                return false;
            }

            if (_outfit != null && !_outfit.IsFullyDressed)
            {
                session.Blocked("blocked.door.ppe", MissingEquipment());
                return false;
            }

            if (!session.Complete("ppe.hall") || _loading)
            {
                return false;
            }

            _loading = true;
            SceneManager.LoadScene(_hallScene, LoadSceneMode.Single);
            return true;
        }

        private string MissingEquipment()
        {
            var names = new List<string>();
            foreach (OutfitPart part in new[] { OutfitPart.Helmet, OutfitPart.Jacket, OutfitPart.GloveLeft, OutfitPart.GloveRight, OutfitPart.BootLeft, OutfitPart.BootRight })
            {
                if (!_outfit.IsWorn(part))
                {
                    names.Add(Loc.T("ppe." + part));
                }
            }

            return string.Join(", ", names);
        }

        // ------------------------------------------------------------------ panels

        private void RebuildPanels()
        {
            if (_journalContent == null)
            {
                return;
            }

            TrainingSession session = TrainingSession.Current;
            bool read = session != null && session.IsDone("journal.read");
            bool signed = session != null && session.IsDone("journal.sign");
            if (_frame != null)
            {
                Color c = signed ? (Color)UiStyle.Good : read ? (Color)UiStyle.Warn : new Color(0.4f, 0.45f, 0.55f, 1f);
                _frame.startColor = _frame.endColor = c;
                _frame.sharedMaterial = FrameMaterial(c);
            }

            // journal
            Ui.Clear(_journalContent);
            Ui.Glass(_journalContent, 0, 0, 820, 640, 28);
            Ui.Label(_journalContent, Loc.T("journal.title"), 36, 26, 748, 40, 28, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(_journalContent, Loc.T("journal.meta", System.DateTime.Now.ToString("dd.MM.yyyy"), Loc.T("practice.mag.short")), 36, 70, 748, 30, 19, UiStyle.AccentText);
            RectTransform text = Ui.Card(_journalContent, 36, 112, 748, 330, 18);
            Ui.Label(text, Loc.T("journal.text"), 22, 16, 704, 300, 18.5f, UiStyle.TextDim);
            Toggle toggle = Ui.Check(_journalContent, 36, 462, 64, read || ReadTicked, SetRead);
            toggle.interactable = !read;
            Ui.Label(_journalContent, Loc.T("journal.confirm"), 118, 462, 660, 64, 23, UiStyle.Text, TextAlignmentOptions.Left, true);
            string status = signed ? "journal.status.signed" : read ? "journal.status.sign" : "journal.status.read";
            Ui.Label(_journalContent, Loc.T(status), 36, 546, 748, 70, 21, signed ? UiStyle.Good : UiStyle.Warn);

            // door
            Ui.Clear(_doorContent);
            Ui.Glass(_doorContent, 0, 0, 700, 420, 28);
            Ui.Label(_doorContent, Loc.T("door.title"), 36, 26, 628, 44, 30, UiStyle.Text, TextAlignmentOptions.Left, true);
            bool dressed = _outfit != null && _outfit.IsFullyDressed;
            (string key, bool ok)[] needs = { ("door.need.journal", signed), ("door.need.ppe", dressed) };
            for (int i = 0; i < needs.Length; i++)
            {
                Ui.StepBadge(_doorContent, 36, 96 + i * 76f, 52, (i + 1).ToString(), needs[i].ok, !needs[i].ok);
                Ui.Label(_doorContent, Loc.T(needs[i].key), 106, 96 + i * 76f, 560, 52, 22, needs[i].ok ? UiStyle.TextDim : UiStyle.Text, TextAlignmentOptions.Left);
            }

            Ui.Button(_doorContent, Loc.T("door.enter"), 36, 300, 628, 76, signed && dressed ? ButtonKind.Primary : ButtonKind.Secondary, () => TryEnterHall(), 26);
        }

        private Material _frameMaterial;

        private Material FrameMaterial(Color color)
        {
            if (_frameMaterial == null)
            {
                _frameMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            }

            _frameMaterial.color = color;
            return _frameMaterial;
        }

        protected virtual void LateUpdate()
        {
            // the door panel shows whether everything is worn: refresh it when the outfit changes
            if (_outfit != null && _outfit.Worn != _lastWorn)
            {
                _lastWorn = _outfit.Worn;
                RebuildPanels();
            }
        }

        private OutfitPart _lastWorn = (OutfitPart)(-1);
    }
}
