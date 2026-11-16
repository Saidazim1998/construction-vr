using System;
using ConstructionVR.Training.UI;
using NBM500U;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// A measuring tool (the square for the fit-up, the fillet gauge for the seam): held at one of its
    /// measuring places it shows a reading, which the director supplies.
    /// </summary>
    public class MeasureTool : MonoBehaviour
    {
        [SerializeField] private Grabbable _grabbable;
        [SerializeField] private NBMReturnToRest _returnToRest;
        [Tooltip("The part of the tool that is put against the work.")]
        [SerializeField] private Transform _probe;
        [SerializeField] private float _reach = 0.05f;
        [SerializeField] private TMP_Text _readout;

        public bool Held => _grabbable != null && _grabbable.SelectingPointsCount > 0;
        public bool AtRest => _returnToRest == null || _returnToRest.IsAtRest;

        /// <summary>Where the tool lies when it is put away.</summary>
        public Vector3 RestPosition => _returnToRest != null ? _returnToRest.RestPosition : transform.position;

        /// <summary>World positions where the tool measures. Set by the director.</summary>
        public Vector3[] Places = new Vector3[0];

        /// <summary>Gives the text of the reading at a place; return null when there is nothing to measure there.</summary>
        public Func<int, string> Read;

        /// <summary>Raised once each time the tool is put to a place.</summary>
        public event Action<int> WhenMeasured;

        private int _current = -1;
        private bool _simulated;
        private int _simulatedPlace;

        // The reading is also shown on a small tag above the tool, turned to the eyes and drawn over everything:
        // the figures printed on the tool itself are under the fingers of the hand that holds it.
        private const float TagShownFor = 3f;           // seconds the last reading stays after the tool leaves the place
        private UiCanvas _tag;
        private TextMeshProUGUI _tagText;
        private string _tagValue;
        private float _tagUntil;
        private Transform _head;

        /// <summary>True while the reading tag is shown (for tests).</summary>
        public bool TagShown => _tag != null && _tag.gameObject.activeSelf;

        protected virtual void OnDestroy()
        {
            if (_tag != null)
            {
                Destroy(_tag.gameObject);
            }
        }

        protected virtual void LateUpdate()
        {
            bool show = Held && _tagValue != null && (_current >= 0 || Time.unscaledTime < _tagUntil) && FindHead();
            if (!show)
            {
                if (_tag != null && _tag.gameObject.activeSelf)
                {
                    _tag.gameObject.SetActive(false);
                }

                return;
            }

            EnsureTag();
            if (_tagText.text != _tagValue)
            {
                _tagText.text = _tagValue;
            }

            if (!_tag.gameObject.activeSelf)
            {
                _tag.gameObject.SetActive(true);
            }

            // above the tool and a little towards the eyes, clear of the hand
            Vector3 at = (_probe != null ? _probe : transform).position;
            Vector3 toEyes = _head.position - at;
            Vector3 position = at + Vector3.up * 0.1f + (toEyes.sqrMagnitude > 1e-4f ? toEyes.normalized * 0.06f : Vector3.zero);
            _tag.Place(position);
            Vector3 away = position - _head.position;
            if (away.sqrMagnitude > 1e-4f)
            {
                _tag.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }

        private bool FindHead()
        {
            if (_head == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                _head = rig != null ? rig.centerEyeAnchor : null;
            }

            return _head != null;
        }

        private void EnsureTag()
        {
            if (_tag != null)
            {
                return;
            }

            const float w = 440f, h = 104f;
            _tag = UiCanvas.Create("Reading " + name, new Vector2(w, h));
            _tag.MakeReadOnly();
            _tag.Root.localScale = Vector3.one * (UiStyle.MetresPerPixel * 0.5f);        // 22 x 5.2 cm
            RectTransform content = Ui.Fill("Content", _tag.Root);
            Ui.Glass(content, 0, 0, w, h, 22, false, false);
            _tagText = Ui.Label(content, string.Empty, 14, 6, w - 28, h - 12, 50, new Color(1f, 0.85f, 0.3f), TextAlignmentOptions.Center, true);
            _tagText.enableAutoSizing = true;                   // a long reading gets smaller rather than cut off
            _tagText.fontSizeMin = 24f;
            _tagText.fontSizeMax = 50f;
            _tagText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        protected virtual void Update()
        {
            int place = -1;
            if (_simulated)
            {
                place = _simulatedPlace;
            }
            else if (Held && _probe != null)
            {
                float best = _reach;
                for (int i = 0; i < Places.Length; i++)
                {
                    float d = Vector3.Distance(_probe.position, Places[i]);
                    if (d < best)
                    {
                        best = d;
                        place = i;
                    }
                }
            }

            if (place == _current)
            {
                return;
            }

            _current = place;
            string text = place >= 0 && Read != null ? Read(place) : null;
            if (_readout != null)
            {
                _readout.text = text ?? string.Empty;
            }

            if (place >= 0 && text != null)
            {
                _tagValue = text;
                WhenMeasured?.Invoke(place);
            }
            else if (place < 0)
            {
                _tagUntil = Time.unscaledTime + TagShownFor;     // left the place: the reading stays a little longer
            }
        }

        /// <summary>For tests: behaves as if the tool were held at a place (-1 = taken away).</summary>
        public void SimulateAt(int place)
        {
            _simulated = place >= 0;
            _simulatedPlace = place;
        }
    }
}
