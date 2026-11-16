using System.Collections.Generic;
using ConstructionVR.Player;
using ConstructionVR.Training.UI;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// Shows the player how to put the protective equipment on. While an item from the stand is held, a green
    /// ring marks the place on the body it has to be brought to and the alert panel says what to do; letting
    /// it go tells whether it was put on and what is still missing.
    /// </summary>
    [AddComponentMenu("")]
    public class PpeGuide : MonoBehaviour
    {
        private const int Segments = 40;
        private const float RingRadius = 0.11f;

        private static readonly OutfitPart[] Parts =
        {
            OutfitPart.Helmet, OutfitPart.Jacket, OutfitPart.GloveLeft, OutfitPart.GloveRight, OutfitPart.BootLeft, OutfitPart.BootRight,
        };

        private PlayerOutfit _outfit;
        private Transform _eyes;
        private WearableItem _held;
        private LineRenderer _ring;
        private Material _material;

        /// <summary>The item the ring is shown for, if any (for tests).</summary>
        public WearableItem Held => _held;

        /// <summary>True while the ring is visible (for tests).</summary>
        public bool RingShown => _ring != null && _ring.enabled;

        protected virtual void OnEnable()
        {
            WearableItem.WhenPickedUp += OnPickedUp;
            WearableItem.WhenLetGo += OnLetGo;
        }

        protected virtual void OnDisable()
        {
            WearableItem.WhenPickedUp -= OnPickedUp;
            WearableItem.WhenLetGo -= OnLetGo;
            _held = null;
            if (_ring != null)
            {
                _ring.enabled = false;
            }
        }

        protected virtual void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }

        private void OnPickedUp(WearableItem item)
        {
            if (item.IsWorn)
            {
                return;
            }

            _held = item;
            AlertPanel.Instance?.Hint(Loc.T("ppe.hint." + item.Part));
        }

        private void OnLetGo(WearableItem item, bool worn)
        {
            if (_held == item)
            {
                _held = null;
            }

            if (!Find())
            {
                return;
            }

            string name = Capital(Loc.T("ppe." + item.Part));
            if (!worn)
            {
                AlertPanel.Instance?.Hint(Loc.T("ppe.hint.missed", name));
                return;
            }

            var missing = new List<string>();
            foreach (OutfitPart part in Parts)
            {
                if (!_outfit.IsWorn(part))
                {
                    missing.Add(Loc.T("ppe." + part));
                }
            }

            AlertPanel.Instance?.Hint(missing.Count == 0
                ? Loc.T("ppe.hint.all")
                : Loc.T("ppe.hint.worn", name, string.Join(", ", missing)));
        }

        protected virtual void LateUpdate()
        {
            bool show = _held != null && _held.IsHeld && !_held.IsWorn && Find() && _held.Part != OutfitPart.Helmet;
            if (!show)
            {
                if (_ring != null && _ring.enabled)
                {
                    _ring.enabled = false;
                }

                return;
            }

            EnsureRing();
            Vector3 point = _outfit.WearPoint(_held.Part);
            Vector3 toEyes = _eyes.position - point;
            Transform ring = _ring.transform;
            ring.position = point;
            if (toEyes.sqrMagnitude > 1e-4f)
            {
                ring.rotation = Quaternion.LookRotation(toEyes.normalized, Vector3.up);       // faces the player
            }

            ring.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f));  // a slow pulse
            _ring.enabled = true;
        }

        private bool Find()
        {
            if (_outfit == null)
            {
                _outfit = FindAnyObjectByType<PlayerOutfit>();
            }

            if (_eyes == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                _eyes = rig != null ? rig.centerEyeAnchor : null;
            }

            return _outfit != null && _eyes != null;
        }

        private void EnsureRing()
        {
            if (_ring != null)
            {
                return;
            }

            var go = new GameObject("PPE Wear Ring");
            go.transform.SetParent(transform, false);
            _ring = go.AddComponent<LineRenderer>();
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = UiStyle.Good };
            _ring.sharedMaterial = _material;
            _ring.useWorldSpace = false;
            _ring.loop = true;
            _ring.widthMultiplier = 0.012f;
            _ring.alignment = LineAlignment.View;
            _ring.numCornerVertices = 2;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            _ring.positionCount = Segments;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * RingRadius, Mathf.Sin(a) * RingRadius, 0f));
            }
        }

        private static string Capital(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0]) + text.Substring(1);
        }
    }
}
