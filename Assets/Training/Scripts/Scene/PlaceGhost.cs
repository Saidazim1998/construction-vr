using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>
    /// While a thing that has a place of its own is carried (the earth clamp, a plate, the rag, ...), a
    /// see-through copy of it is shown at that place, in the position it will take there. The copy goes when
    /// the thing is put down.
    /// </summary>
    [AddComponentMenu("")]
    public class PlaceGhost : MonoBehaviour
    {
        private SnapItem[] _items = System.Array.Empty<SnapItem>();
        private float _nextScan;

        /// <summary>How many copies were shown in the last frame (for tests).</summary>
        public int Shown { get; private set; }

        protected virtual void LateUpdate()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                _items = FindObjectsByType<SnapItem>(FindObjectsInactive.Exclude);
            }

            Shown = 0;
            foreach (SnapItem item in _items)
            {
                if (item != null && item.Held && !item.Snapped && item.TryGetSnapPose(out Vector3 position, out Quaternion rotation))
                {
                    ModelGlow.DrawAt(item.transform, ModelGlow.Ghost, position, rotation);
                    Shown++;
                }
            }
        }
    }
}
