using System;
using ConstructionVR.Training.UI;
using NBM500U;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// Something the learner has to look at and judge: sound or faulty (a cable, the cylinder chain, the gauge).
    /// A small card next to the object asks the question; the answer is compared with the real state, which a
    /// session may have planted as a fault.
    /// </summary>
    public class InspectPoint : MonoBehaviour
    {
        [SerializeField] private string _stepId;
        [SerializeField] private StageId _stage;
        [Tooltip("Text keys: the question, the answer for 'in order', the answer for 'faulty'.")]
        [SerializeField] private string _questionKey;
        [SerializeField] private string _okKey = "inspect.ok";
        [SerializeField] private string _faultKey = "inspect.fault";
        [Tooltip("Shown and logged when a real fault is found / is overlooked.")]
        [SerializeField] private string _foundKey;
        [SerializeField] private string _missedKey;
        [SerializeField] private Severity _missedSeverity = Severity.High;
        [SerializeField] private float _showWithin = 2.6f;

        /// <summary>The real state. Set by the scene's director from the session's planted faults.</summary>
        public bool Faulty { get; set; }
        public bool Answered { get; private set; }
        public bool SaidFaulty { get; private set; }
        public bool Missed => Answered && Faulty && !SaidFaulty;
        public string StepId => _stepId;

        public event Action<InspectPoint> WhenAnswered;

        private UiCanvas _card;
        private RectTransform _content;
        private Transform _head;
        private bool _shown;

        protected virtual void Start()
        {
            _card = UiCanvas.Create("Inspect " + _stepId, new Vector2(560, 300), transform);
            _card.transform.localPosition = Vector3.zero;
            _content = Ui.Fill("Content", _card.Root);
            _card.gameObject.SetActive(false);
            var rig = FindAnyObjectByType<OVRCameraRig>();
            _head = rig != null ? rig.centerEyeAnchor : null;
        }

        protected virtual void OnEnable() => Loc.Changed += Rebuild;

        protected virtual void OnDisable() => Loc.Changed -= Rebuild;

        protected virtual void Update()
        {
            TrainingSession session = TrainingSession.Current;
            bool show = session != null && !Answered && session.IsCurrent(_stage) && _head != null &&
                        Vector3.Distance(_head.position, transform.position) < _showWithin;
            if (show != _shown)
            {
                _shown = show;
                _card.gameObject.SetActive(show);
                if (show)
                {
                    Rebuild();
                }
            }

            if (show)
            {
                _card.Place(transform.position);                // comes forward when something stands in front of it
                Vector3 away = transform.position - _head.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                {
                    _card.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                }
            }
        }

        private void Rebuild()
        {
            if (_content == null || !_shown)
            {
                return;
            }

            Ui.Clear(_content);
            Ui.Glass(_content, 0, 0, 560, 300, 26);
            Ui.Label(_content, Loc.T("inspect.title"), 30, 20, 500, 24, 17, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            Ui.Label(_content, Loc.T(_questionKey), 30, 50, 500, 110, 24, UiStyle.Text, TextAlignmentOptions.TopLeft, true);
            Ui.Button(_content, Loc.T(_okKey), 30, 196, 244, 76, ButtonKind.Primary, () => Answer(false), 22);
            Ui.Button(_content, Loc.T(_faultKey), 286, 196, 244, 76, ButtonKind.Secondary, () => Answer(true), 22);
        }

        /// <summary>The learner's verdict. Counted once; a wrong verdict costs points, an overlooked fault more.</summary>
        public void Answer(bool saysFaulty)
        {
            TrainingSession session = TrainingSession.Current;
            if (session == null || Answered)
            {
                return;
            }

            if (!session.IsOpen(_stage))
            {
                session.Blocked("blocked.order", Loc.T(session.Stage.TitleKey));
                return;
            }

            Answered = true;
            SaidFaulty = saysFaulty;
            if (Faulty && saysFaulty)
            {
                session.FaultFound(_foundKey);
            }
            else if (Faulty)
            {
                session.Violation(_missedKey, ScoreCategory.Safety, _missedSeverity);
            }
            else if (saysFaulty)
            {
                session.Violation("viol.falsealarm", ScoreCategory.Technique, Severity.Low, Loc.T(_questionKey));
            }

            WhenAnswered?.Invoke(this);
            session.Complete(_stepId);
        }

        /// <summary>Asks again (the director uses it after a fault was put right).</summary>
        public void Reopen(bool faulty)
        {
            Faulty = faulty;
            Answered = false;
        }
    }
}
