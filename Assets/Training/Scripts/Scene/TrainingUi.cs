using ConstructionVR.Training.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace ConstructionVR.Training
{
    /// <summary>
    /// What every scene of the trainer needs besides its own logic: the task card and the alerts that stay with
    /// the learner, and the settings (sound, picture) put into effect.
    /// </summary>
    public class TrainingUi : MonoBehaviour
    {
        public static TrainingUi Instance { get; private set; }

        private float _baseExposure;
        private bool _hasBaseExposure;

        /// <summary>Creates the shared interface of a scene once.</summary>
        public static TrainingUi Ensure()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("Training UI");
            Instance = go.AddComponent<TrainingUi>();
            TaskPanel.Create().transform.SetParent(go.transform, true);
            AlertPanel.Create().transform.SetParent(go.transform, true);
            go.AddComponent<StepGuide>();
            Instance.Apply();
            return Instance;
        }

        protected virtual void OnEnable() => AppSettings.Changed += Apply;

        protected virtual void OnDisable()
        {
            AppSettings.Changed -= Apply;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Apply()
        {
            AppSettings s = AppSettings.Current;
            AudioListener.volume = s.masterVolume;
            XRSettings.eyeTextureResolutionScale = s.highResolution ? 1.25f : 1f;
#if !UNITY_EDITOR
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.supportsHDR = s.hdr;                        // in the Editor this would rewrite the pipeline asset
            }
#endif

            // brightness: 80 % is the picture as it was graded; the slider moves the exposure around it
            foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (volume.isGlobal && volume.profile != null && volume.profile.TryGet(out ColorAdjustments adjustments))
                {
                    if (!_hasBaseExposure)
                    {
                        _baseExposure = adjustments.postExposure.value;
                        _hasBaseExposure = true;
                    }

                    adjustments.postExposure.overrideState = true;
                    adjustments.postExposure.value = _baseExposure + (s.brightness - 0.8f) * 2.5f;
                    break;
                }
            }
        }
    }
}
