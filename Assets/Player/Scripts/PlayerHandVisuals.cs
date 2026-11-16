using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace ConstructionVR.Player
{
    /// <summary>
    /// The camera rig shows the Interaction SDK's own hand meshes; the full body has hands of its own (bare or
    /// gloved) whose fingers follow the same tracked hand. Two hands in one place would poke through each other,
    /// so the rig's hand mesh is switched off for as long as the body's hand is where the tracked hand is.
    /// When they are apart (no body tracking, or the Meta XR Simulator, where the body is a recording) the rig's
    /// hand stays visible, so there is always a hand at the place the player interacts with.
    /// </summary>
    [DefaultExecutionOrder(10600)]          // after the retargeter and PlayerBodyAnchor have placed the body
    public class PlayerHandVisuals : MonoBehaviour
    {
        [SerializeField] private Transform _wristLeft;
        [SerializeField] private Transform _wristRight;

        [Tooltip("The rig's hand meshes (Interaction SDK HandVisual).")]
        [SerializeField] private HandVisual _visualLeft;
        [SerializeField] private HandVisual _visualRight;

        [Tooltip("Metres. The rig's hand is hidden while the body's wrist is closer than this to the tracked wrist.")]
        [SerializeField] private float _hideWithin = 0.08f;

        [Tooltip("Metres. The rig's hand comes back when the wrists are further apart than this.")]
        [SerializeField] private float _showBeyond = 0.12f;

        private PlayerBodyAnchor _anchor;

        /// <summary>True while the rig's left hand mesh is hidden because the body's hand covers it.</summary>
        public bool LeftCovered { get; private set; }

        /// <summary>True while the rig's right hand mesh is hidden because the body's hand covers it.</summary>
        public bool RightCovered { get; private set; }

        protected virtual void LateUpdate()
        {
            LeftCovered = Apply(_wristLeft, _visualLeft, LeftCovered);
            RightCovered = Apply(_wristRight, _visualRight, RightCovered);
        }

        protected virtual void OnDisable()
        {
            if (_visualLeft != null) _visualLeft.ForceOffVisibility = false;
            if (_visualRight != null) _visualRight.ForceOffVisibility = false;
            LeftCovered = RightCovered = false;
        }

        private bool Apply(Transform wrist, HandVisual visual, bool covered)
        {
            if (wrist == null || visual == null)
            {
                return false;
            }

            if (_anchor == null)
            {
                _anchor = GetComponent<PlayerBodyAnchor>();
            }

            // without body tracking the body is hidden: its frozen hand covers nothing
            bool bodyShown = _anchor == null || _anchor.IsTracked;
            IHand hand = visual.Hand;
            if (bodyShown && hand != null && hand.IsTrackedDataValid && hand.GetRootPose(out Pose root))
            {
                float distance = Vector3.Distance(root.position, wrist.position);
                covered = covered ? distance < _showBeyond : distance < _hideWithin;
            }
            else
            {
                covered = false;
            }

            if (visual.ForceOffVisibility != covered)
            {
                visual.ForceOffVisibility = covered;
            }

            return covered;
        }
    }
}
