using ConstructionVR.Training.UI;
using UnityEngine;

namespace ConstructionVR.Training
{
    // What the guide arrow points at for each step done in the welding hall (see StepGuide).
    // The arrow shows where a step is done, never what the right answer is: at a check it points at the place
    // to look at, and when parts are picked it points at the whole rack, not at the right plate.
    public partial class HallDirector
    {
        private const float CardClearance = 0.2f;       // an inspection card hangs at its point: the arrow goes above it

        private GuideTarget? Guide(string step)
        {
            if (_session == null || _session.IsFinished)
            {
                return null;
            }

            TrainingConfig c = TrainingConfig.Current;
            switch (step)
            {
                // stage 2: the workplace
                case "area.fire": return Check(_inspectFire);
                case "area.vent": return At(_extractor, "guide.vent");
                case "area.clean": return Check(_inspectArea);
                case "area.remove": return Carry(_rag, "guide.rag", "guide.bin");

                // stage 3: electrical
                case "el.mains": return Check(_inspectMains);
                case "el.torch": return Check(_inspectTorch);
                case "el.ground": return Carry(_earthClamp, "guide.clamp", "guide.clamp.to");
                case "pw.switch":
                case "cl.power": return At(_machine.MainSwitch, "guide.switch");
                case "pw.check": return Check(_inspectPanel);

                // stage 4: wire
                case "wire.cover": return At(_spoolCover, "guide.cover");
                case "wire.spool": return Check(_inspectSpool);
                case "wire.replace": return Carry(_newSpool, "guide.spool", "guide.here");
                case "wire.tension":
                    // two rollers, a knob each, and both have to press: one after the other
                    if (!c.rollerTension.Contains(_machine.TensionFront)) return Knob(_machine.TensionFrontKnob, "guide.tension.front", _machine.TensionFront, c.rollerTension, "0.0", string.Empty);
                    if (!c.rollerTension.Contains(_machine.TensionRear)) return Knob(_machine.TensionRearKnob, "guide.tension.rear", _machine.TensionRear, c.rollerTension, "0.0", string.Empty);
                    return null;                // both are right: the step closes in a moment
                case "wire.feed": return At(_machine.WireButton, "guide.feed");
                case "wire.close":
                    return HingeAngle(_spoolCover, _coverRest) >= 8f ? At(_spoolCover, "guide.close") : At(_feederDoor, "guide.close");

                // stage 5: gas
                case "cyl.chain": return Check(_inspectChain);
                case "cyl.fasten": return Carry(_chainHook, "guide.hook", "guide.hook.to");
                case "cyl.reg": return Check(_inspectRegulator);
                case "gas.open": return At(_gas.ValveTransform != null ? _gas.ValveTransform : _gasValve != null ? _gasValve.transform : null, "guide.valve.open");
                case "cl.gas": return At(_gas.ValveTransform != null ? _gas.ValveTransform : _gasValve != null ? _gasValve.transform : null, "guide.valve");
                case "gas.pressure": return Check(_inspectPressure);
                case "gas.swap": return TaskCard();
                case "gas.flow":
                    if (!_gas.IsValveOpen)
                    {
                        return At(_gas.ValveTransform, "guide.valve.open");     // no gas comes with the valve shut, whatever the knob says
                    }

                    return c.gasFlow.Contains(_gas.FlowLitresPerMinute) ? null
                        : Knob(_gas.FlowKnobTransform, "guide.flow", _gas.FlowLitresPerMinute, c.gasFlow, "0.0", " l/min");

                // stage 6: the mode. A value that is already right needs no turning: go on to the next one.
                case "mode.voltage":
                case "mode.current":
                case "mode.speed":
                case "mode.confirm":
                    if (!c.voltage.Contains(_machine.Voltage)) return Knob(_machine.VoltageKnob, "guide.volt", _machine.Voltage, c.voltage, "0.0", " V");
                    if (!c.current.Contains(_machine.Current)) return Knob(_machine.CurrentKnob, "guide.amp", _machine.Current, c.current, "0", " A");
                    if (!c.wireSpeed.Contains(_machine.WireSpeed)) return Knob(_machine.SpeedKnob, "guide.speed", _machine.WireSpeed, c.wireSpeed, "0.0", " m/min");
                    return TaskCard();

                // stage 7: the parts
                case "parts.pick":
                case "parts.place": return Plates();
                case "parts.clamp":
                    foreach (SnapItem clamp in _clamps)
                    {
                        if (!clamp.Snapped)
                        {
                            return Carry(clamp, "guide.gclamp", "guide.here");
                        }
                    }

                    return null;

                // stages 8, 9: measuring and tacks
                case "meas.measure": return Measure(_square, "guide.square", _joint.PointAt(0.5f));
                case "meas.fix":
                    // first the standing plate is taken off and put back; then the fit-up is measured once more
                    if (_webPlate.Held) return Point(_webPlate.Target.position, "guide.fixture");
                    if (_misaligned || !_webPlate.Snapped) return At(_webPlate.transform, "guide.reseat");
                    return Measure(_square, "guide.remeasure", _joint.PointAt(0.5f));
                case "meas.confirm": return TaskCard();
                case "tack.mask":
                case "weld.mask": return TaskCard();
                case "tack.points":
                    if (!_torch.Held)
                    {
                        return At(_torch.transform, "guide.torch");
                    }

                    for (int i = 0; i < _joint.Tacks.Length; i++)
                    {
                        if (!_joint.Tacks[i])
                        {
                            return Point(_joint.TackPoint(i), "guide.tack");
                        }
                    }

                    return null;
                case "tack.measure":
                    return _measuredAfterTacks ? TaskCard() : Measure(_square, "guide.square", _joint.PointAt(0.5f));

                // stage 10: the seam
                case "weld.arc":
                case "weld.seam":
                    return _torch.Held ? Point(_joint.PointAt(FirstUnwelded()), "guide.seam") : At(_torch.transform, "guide.torch");
                case "weld.rest": return Point(_torch.RestPosition, "guide.rest");

                // stage 11: quality
                case "qc.visual":
                    return Point((_qcPanel != null ? _qcPanel.transform.position : _qcPanelAnchor.position) + Vector3.up * 0.42f, "guide.sheet");
                case "qc.measure":
                    if (!_gauge.Held)
                    {
                        return At(_gauge.transform, "guide.gauge");
                    }

                    for (int i = 0; i < _legMeasured.Length && i < _gauge.Places.Length; i++)
                    {
                        if (!_legMeasured[i])
                        {
                            return Point(_gauge.Places[i], "guide.measure");
                        }
                    }

                    return null;
                case "qc.record": return TaskCard();

                // stage 12: tidying up
                case "cl.tools":
                    if (_torch.Held || !_torch.AtRest) return Point(_torch.RestPosition, "guide.rest");
                    if (_square.Held || !_square.AtRest) return Point(_square.RestPosition, "guide.rest");
                    if (_gauge.Held || !_gauge.AtRest) return Point(_gauge.RestPosition, "guide.rest");
                    return null;
                case "cl.waste": return Carry(_waste, "guide.waste", "guide.bin");
                default: return null;
            }
        }

        // ------------------------------------------------------------------ kinds of target

        private static GuideTarget Point(Vector3 position, string labelKey) => new GuideTarget(position, Loc.T(labelKey));

        /// <summary>A thing that stays where it is: a switch, a knob, a cover.</summary>
        private static GuideTarget? At(Component thing, string labelKey)
        {
            return thing == null ? (GuideTarget?)null : new GuideTarget(Top(thing.transform), Loc.T(labelKey), thing.transform);
        }

        /// <summary>A knob that is turned until what it sets is within a range: the caption shows the value now and the range.</summary>
        private static GuideTarget? Knob(Transform knob, string nameKey, float value, ValueRange range, string format, string unit)
        {
            if (knob == null)
            {
                return null;
            }

            string label = Loc.T("guide.turn", Loc.T(nameKey)) + "\n" + Loc.T("guide.value", value.ToString(format) + unit, range + unit);
            return new GuideTarget(Top(knob), label, knob);
        }

        /// <summary>A place that is looked at and judged: its card opens when the learner comes close.</summary>
        private static GuideTarget? Check(InspectPoint point)
        {
            if (point == null || point.Answered)
            {
                return null;
            }

            return Point(point.transform.position + Vector3.up * CardClearance, "guide.check");
        }

        /// <summary>A thing that is carried somewhere: first the thing itself, then, once it is in the hand, its place.</summary>
        private static GuideTarget? Carry(SnapItem item, string nameKey, string placeKey)
        {
            if (item == null || item.Snapped)
            {
                return null;
            }

            if (item.Held && item.Target != null)
            {
                return Point(item.Target.position, placeKey);
            }

            return new GuideTarget(Top(item.transform), Loc.T(nameKey), item.transform);
        }

        /// <summary>A measuring tool: first the tool, then the place it is held against.</summary>
        private static GuideTarget? Measure(MeasureTool tool, string nameKey, Vector3 place)
        {
            if (tool == null)
            {
                return null;
            }

            return tool.Held ? Point(place, "guide.measure") : new GuideTarget(Top(tool.transform), Loc.T(nameKey), tool.transform);
        }

        /// <summary>A step that is finished with a button of the task card.</summary>
        private static GuideTarget? TaskCard()
        {
            TaskPanel panel = TaskPanel.Instance;
            if (panel == null)
            {
                return null;
            }

            // the buttons are at the bottom of the card; the arrow hangs over its lower edge
            Transform t = panel.transform;
            return Point(t.position - t.up * (TaskPanel.Height * UiStyle.MetresPerPixel * 0.30f) - t.forward * 0.03f, "guide.task");
        }

        // The plates: while one is in the hand, where it goes; otherwise the rack they lie on (all of them, so
        // that the arrow does not tell which ones match the drawing).
        private GuideTarget? Plates()
        {
            SnapItem held = null;
            Vector3 sum = Vector3.zero;
            int count = 0;
            float top = float.MinValue;

            void Look(SnapItem plate)
            {
                if (plate == null)
                {
                    return;
                }

                if (plate.Held)
                {
                    held = plate;
                }

                if (!plate.Snapped)
                {
                    Vector3 p = Top(plate.transform);
                    sum += p;
                    top = Mathf.Max(top, p.y);
                    count++;
                }
            }

            Look(_basePlate);
            Look(_webPlate);
            foreach (SnapItem wrong in _wrongPlates)
            {
                Look(wrong);
            }

            if (held != null && held.Target != null)
            {
                return Point(held.Target.position, "guide.fixture");
            }

            if (_basePlate.Snapped && _webPlate.Snapped || count == 0)
            {
                return null;
            }

            Vector3 centre = sum / count;
            centre.y = top;
            return Point(centre, "guide.parts");
        }

        private float FirstUnwelded()
        {
            WeldJoint.Segment[] segments = _joint.Segments;
            if (segments == null)
            {
                return 0f;
            }

            for (int i = 0; i < segments.Length; i++)
            {
                if (!segments[i].Welded)
                {
                    return i / (float)segments.Length;
                }
            }

            return 1f;
        }

        /// <summary>The middle of the top of an object: where an arrow that hangs over it should point.</summary>
        internal static Vector3 Top(Transform thing)
        {
            bool any = false;
            Bounds bounds = default;
            foreach (Renderer r in thing.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return any ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) : thing.position;
        }
    }
}
