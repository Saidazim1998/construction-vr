using ConstructionVR.Player;
using UnityEngine;

namespace ConstructionVR.Training
{
    // What the guide arrow points at for each step done in the engineers' room (see StepGuide).
    public partial class RoomDirector
    {
        private WearableItem[] _wearables;

        private GuideTarget? Guide(string step)
        {
            switch (step)
            {
                case "login.enter":
                case "login.practice":
                case "login.brief":
                    return Point(_hubAnchor.position + Vector3.up * 0.58f, "guide.hub");
                case "journal.read":
                    return Point(_journalPanelAnchor.position + Vector3.up * 0.36f, "guide.journal");
                case "journal.sign":
                    if (_pen == null || _signZone == null)
                    {
                        return null;
                    }

                    return _pen.SelectingPointsCount > 0
                        ? Point(_signZone.position, "guide.sign")
                        : new GuideTarget(HallDirector.Top(_pen.transform), Loc.T("guide.pen"), _pen.transform);
                case "ppe.wear":
                    return NextToWear();
                case "ppe.hall":
                    return Point(_doorPanelAnchor.position + Vector3.up * 0.28f, "guide.door");
                default:
                    return null;
            }
        }

        // The next piece of equipment on the stand. While one is in the hand the green ring of PpeGuide shows
        // where it goes, so the arrow steps aside.
        private GuideTarget? NextToWear()
        {
            if (_wearables == null)
            {
                _wearables = FindObjectsByType<WearableItem>(FindObjectsInactive.Exclude);
            }

            WearableItem next = null;
            foreach (WearableItem item in _wearables)
            {
                if (item == null || item.IsWorn)
                {
                    continue;
                }

                if (item.IsHeld)
                {
                    return null;
                }

                if (next == null || item.Part < next.Part)
                {
                    next = item;            // always the same order: mask, jacket, gloves, boots
                }
            }

            if (next == null)
            {
                return null;
            }

            string name = Loc.T("ppe." + next.Part);
            name = string.IsNullOrEmpty(name) ? name : char.ToUpper(name[0]) + name.Substring(1);
            return new GuideTarget(HallDirector.Top(next.transform), Loc.T("guide.take", name), next.transform);
        }

        private static GuideTarget Point(Vector3 position, string labelKey) => new GuideTarget(position, Loc.T(labelKey));
    }
}
