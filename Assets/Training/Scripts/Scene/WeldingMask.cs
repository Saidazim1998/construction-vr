using System;
using System.Collections.Generic;
using NBM500U;
using Oculus.Interaction;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// The welding mask's filter in front of the eyes. Down, it tints the view slightly (an auto-darkening
    /// filter) and goes dark while an arc burns. B or Y lowers and raises it; it only works when the mask is worn.
    /// </summary>
    public class WeldingMask : MonoBehaviour
    {
        [SerializeField] private Material _overlayMaterial;
        [SerializeField] private float _clearAlpha = 0.28f;
        [SerializeField] private float _darkAlpha = 0.9f;

        public static WeldingMask Instance { get; private set; }

        public bool IsDown { get; private set; }

        /// <summary>Set by the torch while an arc burns in view.</summary>
        public bool ArcBurning { get; set; }

        /// <summary>True when the mask can be used (it is being worn). Set by the director.</summary>
        public Func<bool> IsWorn;
        public event Action<bool> WhenChanged;

        /// <summary>A white flash over the view: an arc was struck without the mask.</summary>
        public float Flash { get; private set; }

        private Transform _quad;
        private Material _material;
        private float _alpha;

        protected virtual void Awake() => Instance = this;

        protected virtual void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        protected virtual void Start()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig == null || _overlayMaterial == null)
            {
                enabled = false;
                return;
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Mask Filter";
            Destroy(go.GetComponent<Collider>());
            _quad = go.transform;
            _quad.SetParent(rig.centerEyeAnchor, false);
            _quad.localPosition = new Vector3(0f, 0f, 0.14f);
            _quad.localScale = new Vector3(1.2f, 1.2f, 1f);
            _material = new Material(_overlayMaterial);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
        }

        protected virtual void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) || OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
            {
                Toggle();
            }

            float target = IsDown ? (ArcBurning ? _darkAlpha : _clearAlpha) : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * (target > _alpha ? 14f : 3f));
            Flash = Mathf.MoveTowards(Flash, 0f, Time.deltaTime * 0.5f);
            bool visible = _alpha > 0.005f || Flash > 0.005f;
            if (_quad != null && _quad.gameObject.activeSelf != visible)
            {
                _quad.gameObject.SetActive(visible);
            }

            if (visible && _material != null)
            {
                Color c = Flash > _alpha ? new Color(1f, 1f, 1f, Mathf.Clamp01(Flash)) : new Color(0.01f, 0.03f, 0.02f, _alpha);
                _material.color = c;
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", c);
            }
        }

        public void Toggle() => Set(!IsDown);

        public bool Set(bool down)
        {
            if (down && IsWorn != null && !IsWorn())
            {
                TrainingSession.Current?.Blocked("blocked.mask.notworn");
                return false;
            }

            if (IsDown != down)
            {
                IsDown = down;
                WhenChanged?.Invoke(down);
            }

            return true;
        }

        public void Dazzle() => Flash = 1f;
    }
}
