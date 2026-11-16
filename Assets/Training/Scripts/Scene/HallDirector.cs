using System;
using System.Collections.Generic;
using ConstructionVR.Player;
using ConstructionVR.Training.UI;
using NBM500U;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConstructionVR.Training
{
    /// <summary>
    /// The welding hall part of the practice (second half of stage 2 and stages 3.1 .. 12). It plants the
    /// session's faults in the scene, watches the machine, the cylinder, the parts and the torch, and reports
    /// steps, mistakes and blocked attempts to the session. At the end it shows the report.
    /// </summary>
    public partial class HallDirector : MonoBehaviour
    {
        [Header("Machine and gas")]
        [SerializeField] private WeldMachine _machine;
        [SerializeField] private NBMGasCylinder _gas;
        [SerializeField] private NBMKnobTwistTransformer _gasValve;
        [SerializeField] private Transform _spoolCover;
        [SerializeField] private Transform _feederDoor;

        [Header("Stage 2: workplace")]
        [SerializeField] private InspectPoint _inspectFire;
        [SerializeField] private InspectPoint _inspectArea;
        [SerializeField] private TouchSwitch _extractor;
        [SerializeField] private SnapItem _rag;
        [SerializeField] private FireHazard _fire;
        [SerializeField] private Extinguisher _extinguisher;

        [Header("Stages 3 .. 5: checks")]
        [SerializeField] private InspectPoint _inspectMains;
        [SerializeField] private GameObject _cableDamage;
        [SerializeField] private InspectPoint _inspectTorch;
        [SerializeField] private SnapItem _earthClamp;
        [SerializeField] private InspectPoint _inspectPanel;
        [SerializeField] private InspectPoint _inspectSpool;
        [SerializeField] private SnapItem _newSpool;
        [SerializeField] private InspectPoint _inspectChain;
        [SerializeField] private SnapItem _chainHook;
        [SerializeField] private InspectPoint _inspectRegulator;
        [SerializeField] private InspectPoint _inspectPressure;

        [Header("Stages 7 .. 12: the work")]
        [SerializeField] private SnapItem _basePlate;
        [SerializeField] private SnapItem _webPlate;
        [SerializeField] private SnapItem[] _wrongPlates;
        [SerializeField] private SnapItem[] _clamps;
        [SerializeField] private MeasureTool _square;
        [SerializeField] private MeasureTool _gauge;
        [SerializeField] private WeldTorch _torch;
        [SerializeField] private WeldJoint _joint;
        [SerializeField] private WeldingMask _mask;
        [SerializeField] private SnapItem _waste;
        [SerializeField] private Transform _qcPanelAnchor;
        [SerializeField] private Transform _reportAnchor;
        [SerializeField] private string _roomScene = "EngineerRoom";

        public static HallDirector Instance { get; private set; }

        public WeldMachine Machine => _machine;
        public WeldTorch Torch => _torch;
        public WeldJoint Joint => _joint;
        public FireHazard Fire => _fire;
        public Extinguisher Extinguisher => _extinguisher;
        public MeasureTool Square => _square;
        public MeasureTool Gauge => _gauge;
        public float LastAngle { get; private set; } = float.NaN;
        public float LastGap { get; private set; } = float.NaN;
        public WeldDefect MarkedDefects { get; private set; }

        private TrainingSession _session;
        private PlayerOutfit _outfit;
        private Quaternion _coverRest, _doorRest;
        private float _nextPoll;
        private float _tensionOkSince = -1f, _flowOkSince = -1f;
        private float _startVolts, _startAmps, _startSpeed;
        private bool _misaligned;
        private bool _measuredAfterTacks;
        private bool _hazardMissed;
        private bool _seamEvaluated;
        private float _arcSecondsInWeld;
        private float _tackWander;
        private float _lastNoMask = -100f, _lastNoGas = -100f;
        private readonly bool[] _legMeasured = new bool[3];
        private readonly float[] _legValues = new float[3];
        private UiCanvas _qcPanel;
        private RectTransform _qcContent;
        private bool _reported;

        protected virtual void Awake() => Instance = this;

        protected virtual void OnDestroy()
        {
            if (Instance == this) Instance = null;
            TaskPanel.Actions = null;
            TaskPanel.Readout = null;
            if (StepGuide.Source == Guide)
            {
                StepGuide.Source = null;
            }

            TrainingSession.Finished -= OnFinished;
            TrainingSession.Changed -= OnSessionChanged;
        }

        protected virtual void Start()
        {
            _outfit = FindAnyObjectByType<PlayerOutfit>();
            EnsureSession();
            _session = TrainingSession.Current;
            TrainingUi.Ensure();
            TaskPanel.Actions = StageActions;
            TaskPanel.Readout = StageReadout;
            StepGuide.Source = Guide;                       // the arrow that shows where the current step is done
            TrainingSession.Finished += OnFinished;
            TrainingSession.Changed += OnSessionChanged;

            _coverRest = _spoolCover != null ? _spoolCover.localRotation : Quaternion.identity;
            _doorRest = _feederDoor != null ? _feederDoor.localRotation : Quaternion.identity;
            _startVolts = _machine.Voltage;
            _startAmps = _machine.Current;
            _startSpeed = _machine.WireSpeed;

            PlantFaults(_session.Variant);
            Wire();
            TaskPanel.Instance?.Rebuild();
        }

        // The hall can be played on its own (from the Editor): then a session is made up to the hall door.
        private void EnsureSession()
        {
            if (TrainingSession.Current != null && !TrainingSession.Current.IsFinished)
            {
                return;
            }

            UserAccount user = TrainingSession.Current != null ? TrainingSession.Current.User : UserStore.Users[0];
            TrainingSession session = TrainingSession.Begin(user);
            session.SelectPractice(Scenario.PracticeId);
            foreach (string step in new[] { "login.brief", "journal.read", "journal.sign", "ppe.wear", "ppe.hall" })
            {
                session.Complete(step);
            }

            if (_outfit != null)
            {
                _outfit.SetWorn(OutfitPart.All);
            }
        }

        private void PlantFaults(ScenarioVariant v)
        {
            _inspectMains.Faulty = v.cableDamaged;
            if (_cableDamage != null) _cableDamage.SetActive(v.cableDamaged);
            _inspectSpool.Faulty = v.spoolEmpty;
            _machine.SetSpool(v.spoolEmpty);
            _inspectChain.Faulty = v.cylinderUnchained;
            if (!v.cylinderUnchained) _chainHook.SnapNow();
            _inspectPressure.Faulty = v.lowPressure;
            if (v.lowPressure) _gas.CylinderPressureMpa = TrainingConfig.Current.lowCylinderPressure;
            _misaligned = v.misaligned;
        }

        private void Wire()
        {
            _mask.IsWorn = () => _outfit == null || _outfit.IsWorn(OutfitPart.Helmet);
            _mask.WhenChanged += down => TaskPanel.Instance?.Rebuild();

            // stage 2
            _extractor.WhenSwitched += on =>
            {
                if (on) _session.Complete("area.vent");
            };
            _inspectArea.WhenAnswered += p =>
            {
                if (p.Faulty && p.SaidFaulty) _session.Require("area.remove");
                _hazardMissed = p.Missed;
            };
            _rag.WhenSnapped += item =>
            {
                _hazardMissed = false;
                if (_session.IsDone("area.clean")) _session.Complete("area.remove");
            };
            _extinguisher.Fire = _fire;
            _fire.WhenExtinguished += () =>
            {
                if (_fire.BurningSeconds > 30f) _session.Violation("viol.fire.slow", ScoreCategory.Safety, Severity.Medium);
                _session.Info("fire.out");
                AlertPanel.Instance?.Push(EventKind.FaultFound, Loc.T("fire.out"));
            };

            // stage 3.1
            _inspectMains.WhenAnswered += p =>
            {
                if (p.Faulty && p.SaidFaulty && _cableDamage != null) _cableDamage.SetActive(false);    // reported: the cable is replaced
            };
            _earthClamp.WhenSnapped += item => _session.Complete("el.ground");
            _earthClamp.WhenTaken += item => _session.Undo("el.ground");

            // stage 3.2
            _machine.MainSwitch.CanSwitch = on =>
            {
                if (on && !_session.IsOpen(StageId.PowerOn))
                {
                    _session.Violation("viol.power.early", ScoreCategory.Safety, Severity.High);
                    return false;
                }

                return true;
            };
            _machine.MainSwitch.WhenSwitched += on =>
            {
                if (on)
                {
                    if (_inspectMains.Missed && _cableDamage != null && _cableDamage.activeSelf)
                    {
                        _session.Violation("viol.power.cable", ScoreCategory.Safety, Severity.High);
                    }

                    _session.Complete("pw.switch");
                }
                else if (_session.IsCurrent(StageId.Cleanup))
                {
                    _session.Complete("cl.power");
                }
            };

            // stage 4
            _inspectSpool.WhenAnswered += p =>
            {
                if (p.Faulty) _session.Require("wire.replace");
            };
            _newSpool.CanSnap = item =>
            {
                if (_session.IsOpen(StageId.Wire) && _machine.SpoolEmpty) return true;
                _session.Blocked(_machine.SpoolEmpty ? "blocked.order" : "blocked.spool.full", Loc.T(_session.Stage.TitleKey));
                return false;
            };
            _newSpool.WhenSnapped += item =>
            {
                _machine.SetSpool(false);
                _session.Require("wire.replace");
                _session.Complete("wire.replace");
            };
            _machine.WhenFeedRefused += reason => _session.Warning(reason);
            _machine.WhenWireLoaded += () => _session.Complete("wire.feed");

            // stage 5.1
            _inspectChain.WhenAnswered += p =>
            {
                if (p.Faulty) _session.Require("cyl.fasten");
            };
            _chainHook.WhenSnapped += item =>
            {
                if (_session.IsOpen(StageId.CylinderSafety) && _session.IsRequired(Scenario.Stage(StageId.CylinderSafety).Steps[1]))
                {
                    _session.Complete("cyl.fasten");
                }
            };

            // stage 5.2
            _inspectPressure.WhenAnswered += p =>
            {
                if (p.Faulty && p.SaidFaulty) _session.Require("gas.swap");
            };

            // stage 7
            _webPlate.CanSnap = item => PlateMaySnap(_basePlate.Snapped);
            _basePlate.CanSnap = item => PlateMaySnap(true);
            foreach (SnapItem wrong in _wrongPlates)
            {
                wrong.CanSnap = item =>
                {
                    _session.Violation("viol.parts.wrong", ScoreCategory.Technique, Severity.Medium);
                    return false;
                };
            }

            _basePlate.WhenSnapped += item => PlatesChanged();
            _webPlate.WhenSnapped += item =>
            {
                PlatesChanged();
                if (_session.IsCurrent(StageId.Measure) && _misaligned)
                {
                    _misaligned = false;                                    // re-seated: now it stands square
                    _webPlate.Target.localRotation = Quaternion.identity;
                    _webPlate.SnapNow();
                    AlertPanel.Instance?.Hint(Loc.T("meas.reseated"));     // what comes next: the measurement again
                }
                else if (_session.IsCurrent(StageId.Measure) && _session.IsRequired(Scenario.Stage(StageId.Measure).Steps[1]) && !_session.IsDone("meas.fix"))
                {
                    AlertPanel.Instance?.Hint(Loc.T("meas.reseated"));
                }
            };
            _basePlate.WhenTaken += item => PlatesChanged();
            _webPlate.WhenTaken += item => PlatesChanged();
            foreach (SnapItem clamp in _clamps)
            {
                clamp.CanSnap = item =>
                {
                    if (_basePlate.Snapped && _webPlate.Snapped) return true;
                    _session.Blocked("blocked.clamp.parts");
                    return false;
                };
                clamp.WhenSnapped += item => ClampsChanged();
                clamp.WhenTaken += item => ClampsChanged();
            }

            if (_misaligned)
            {
                _webPlate.Target.localRotation = Quaternion.Euler(0f, 0f, 2.6f);    // leans a little: the square will show it
            }

            // stage 8 / 9: the square
            _square.Places = new[] { _joint.PointAt(0.5f) };
            _square.Read = place => _basePlate.Snapped && _webPlate.Snapped ? ReadSquare() : null;
            _square.WhenMeasured += place => OnSquareMeasured();

            // stages 9, 10: the torch
            _torch.IsEarthed = () => _earthClamp.Snapped;
            _torch.Permission = ArcPermission;
            _torch.WhenRefused += reason => _session.Blocked(reason);
            _torch.WhenArcStarted += OnArcStarted;
            _joint.WhenTackDone += index =>
            {
                if (_joint.TacksDone >= _joint.Tacks.Length)
                {
                    _joint.ShowTackMarkers(false);
                    _session.Complete("tack.points");
                }
            };

            // stage 11: the gauge
            _gauge.Places = new[] { _joint.PointAt(0.2f), _joint.PointAt(0.5f), _joint.PointAt(0.8f) };
            _gauge.Read = place =>
            {
                float leg = _joint.LegAt(place == 0 ? 0.2f : place == 1 ? 0.5f : 0.8f);
                return leg > 0f ? leg.ToString("0.0") + " mm" : "—";
            };
            _gauge.WhenMeasured += place =>
            {
                if (!_session.IsOpen(StageId.Quality))
                {
                    return;
                }

                _legMeasured[place] = true;
                _legValues[place] = _joint.LegAt(place == 0 ? 0.2f : place == 1 ? 0.5f : 0.8f);
                if (_legMeasured[0] && _legMeasured[1] && _legMeasured[2] && _session.IsDone("qc.visual"))
                {
                    _session.Complete("qc.measure");
                }
            };

            // stage 12
            _waste.WhenSnapped += item => _session.Complete("cl.waste");
        }

        private bool PlateMaySnap(bool baseReady)
        {
            if (!_session.IsOpen(StageId.Parts))
            {
                _session.Blocked("blocked.order", Loc.T(_session.Stage.TitleKey));
                return false;
            }

            if (!baseReady)
            {
                _session.Blocked("blocked.parts.base");
                return false;
            }

            return true;
        }

        private void PlatesChanged()
        {
            if (_basePlate.Snapped && _webPlate.Snapped)
            {
                _session.Complete("parts.pick");
                _session.Complete("parts.place");
            }
            else
            {
                _session.Undo("parts.place");
            }
        }

        private void ClampsChanged()
        {
            bool all = true;
            foreach (SnapItem clamp in _clamps)
            {
                all &= clamp.Snapped;
            }

            if (all) _session.Complete("parts.clamp");
            else _session.Undo("parts.clamp");
        }

        // ------------------------------------------------------------------ measuring

        private string ReadSquare()
        {
            LastAngle = 90f + (_misaligned ? 2.6f : 0.3f);
            LastGap = _misaligned ? 1.8f : 0.4f;
            return LastAngle.ToString("0.0") + "°  /  " + LastGap.ToString("0.0") + " mm";
        }

        private bool FitInTolerance()
        {
            TrainingConfig c = TrainingConfig.Current;
            return !float.IsNaN(LastAngle) && Mathf.Abs(LastAngle - 90f) <= c.angleToleranceDeg && LastGap <= c.gapMaxMm;
        }

        private void OnSquareMeasured()
        {
            _session.SetValue("fit.angle", LastAngle);
            _session.SetValue("fit.gap", LastGap);
            if (_session.IsCurrent(StageId.Measure))
            {
                _session.Complete("meas.measure");
                if (!FitInTolerance())
                {
                    _session.Require("meas.fix");
                    _session.Warning("meas.out", LastAngle.ToString("0.0"), LastGap.ToString("0.0"));
                }
                else if (_session.IsRequired(Scenario.Stage(StageId.Measure).Steps[1]))
                {
                    _session.Complete("meas.fix");
                }
            }
            else if (_session.IsCurrent(StageId.Tack) && _session.IsDone("tack.points"))
            {
                _measuredAfterTacks = true;
            }
        }

        /// <summary>The "O'lchovni tasdiqlash" button.</summary>
        public void ConfirmMeasurement()
        {
            if (_session.IsCurrent(StageId.Measure))
            {
                if (!_session.IsDone("meas.measure")) _session.Blocked("blocked.meas.first");
                else if (!FitInTolerance()) _session.Blocked("blocked.meas.out");
                else _session.Complete("meas.confirm");
            }
            else if (_session.IsCurrent(StageId.Tack))
            {
                if (!_session.IsDone("tack.points")) _session.Blocked("blocked.weld.tackfirst");
                else if (!_measuredAfterTacks) _session.Blocked("blocked.meas.first");
                else _session.Complete("tack.measure");
            }
        }

        // ------------------------------------------------------------------ mode and gas

        /// <summary>The "Rejimni tasdiqlash" button: the values are taken as they are; wrong ones cost points and seam quality.</summary>
        public void ConfirmMode()
        {
            if (!_session.IsCurrent(StageId.Mode))
            {
                _session.Blocked("blocked.order", Loc.T(_session.Stage.TitleKey));
                return;
            }

            TrainingConfig c = TrainingConfig.Current;
            (string step, string key, float value, ValueRange range, string unit)[] checks =
            {
                ("mode.voltage", "ui.brief.voltage", _machine.Voltage, c.voltage, "V"),
                ("mode.current", "ui.brief.current", _machine.Current, c.current, "A"),
                ("mode.speed", "ui.brief.speed", _machine.WireSpeed, c.wireSpeed, "m/min"),
            };
            foreach (var check in checks)
            {
                _session.SetValue(check.step, check.value);
                if (!check.range.Contains(check.value))
                {
                    _session.Violation("viol.mode", ScoreCategory.Technique, Severity.Medium, Loc.T(check.key), check.value.ToString("0.#") + " " + check.unit,
                        check.range + " " + check.unit);
                }

                _session.Complete(check.step);
            }

            _session.Complete("mode.confirm");
        }

        /// <summary>The "Zaxira balonga o'tish" button (after a nearly empty cylinder was reported).</summary>
        public void SwapCylinder()
        {
            _gas.CylinderPressureMpa = 5.7f;
            _session.Info("gas.swapped");
            _session.Complete("gas.swap");
        }

        // ------------------------------------------------------------------ arc

        private (int mode, string reason) ArcPermission()
        {
            if (_fire.Burning)
            {
                return (0, "blocked.fire");
            }

            if (!_session.IsOpen(StageId.Tack))
            {
                return (0, "blocked.weld.early");
            }

            if (_session.IsCurrent(StageId.Tack))
            {
                if (!_session.IsDone("tack.points"))
                {
                    return (1, null);
                }

                return (0, "blocked.weld.measure");
            }

            return _session.IsCurrent(StageId.Weld) && !_session.IsDone("weld.seam") ? (2, null) : (0, "blocked.weld.done");
        }

        private void OnArcStarted()
        {
            if (!_mask.IsDown && Time.time > _lastNoMask + 8f)
            {
                _lastNoMask = Time.time;
                _mask.Dazzle();
                _session.Violation("viol.nomask", ScoreCategory.Safety, Severity.High);
            }

            if ((!_gas.IsValveOpen || _gas.FlowLitresPerMinute < 1f) && Time.time > _lastNoGas + 8f)
            {
                _lastNoGas = Time.time;
                _session.Violation("viol.nogas", ScoreCategory.Technique, Severity.High);
            }

            if (_session.IsCurrent(StageId.Weld))
            {
                _session.Complete("weld.arc");
            }
        }

        private void EvaluateSeam()
        {
            _seamEvaluated = true;
            float mean = _joint.MeanScore();
            _session.SetValue("seam.score", mean);
            _session.Deduct(ScoreCategory.Quality, (1f - mean) * 80f, "deduct.seam", Mathf.RoundToInt(mean * 100f).ToString());
            float arc = Mathf.Max(0.01f, _joint.ArcSeconds);
            (string key, float bad)[] technique = { ("deduct.speed", _joint.SpeedBadSeconds), ("deduct.stickout", _joint.StickOutBadSeconds), ("deduct.angle", _joint.AngleBadSeconds) };
            foreach ((string key, float bad) in technique)
            {
                float share = bad / arc;
                if (share > 0.15f)
                {
                    _session.Deduct(ScoreCategory.Technique, share * 14f, key, Mathf.RoundToInt(share * 100f).ToString());
                }
            }

            if (_joint.ArcStarts > _joint.Tacks.Length + 2)
            {
                _session.Deduct(ScoreCategory.Quality, 3f * (_joint.ArcStarts - _joint.Tacks.Length - 2), "deduct.restarts");
            }
        }

        // ------------------------------------------------------------------ polling

        protected virtual void Update()
        {
            if (_session == null || _session.IsFinished)
            {
                return;
            }

            if (_torch.ArcOn)
            {
                if (_session.IsCurrent(StageId.Weld))
                {
                    _arcSecondsInWeld += Time.deltaTime;
                    if (_hazardMissed && !_rag.Snapped && !_fire.Burning && !_fire.Extinguished && _arcSecondsInWeld > 10f)
                    {
                        _fire.Ignite();
                        _session.Warning("fire.started");
                    }
                }
                else if (_session.IsCurrent(StageId.Tack) && !_session.IsDone("tack.points") && _joint.TackNear(_torch.Tip.position, 0.02f) < 0)
                {
                    _tackWander += Time.deltaTime;
                    if (_tackWander > 1.5f)
                    {
                        _tackWander = -6f;
                        _session.Blocked("blocked.weld.tackfirst");
                    }
                }
            }

            if (Time.time < _nextPoll)
            {
                return;
            }

            _nextPoll = Time.time + 0.2f;
            TrainingConfig c = TrainingConfig.Current;
            _inspectArea.Faulty = !_rag.Snapped;

            switch (_session.StageId)
            {
                case StageId.Wire:
                    if (HingeAngle(_spoolCover, _coverRest) > 50f) _session.Complete("wire.cover");
                    Hold(c.rollerTension.Contains(_machine.Tension), ref _tensionOkSince, 1f, "wire.tension", true);
                    if (_session.IsDone("wire.feed") && HingeAngle(_spoolCover, _coverRest) < 8f && HingeAngle(_feederDoor, _doorRest) < 8f)
                    {
                        _session.Complete("wire.close");
                    }

                    break;
                case StageId.Gas:
                    if (_gas.IsValveOpen) _session.Complete("gas.open");
                    Hold(_gas.IsValveOpen && c.gasFlow.Contains(_gas.FlowLitresPerMinute), ref _flowOkSince, 1.5f, "gas.flow", true);
                    break;
                case StageId.Mode:
                    if (c.voltage.Contains(_machine.Voltage) && Mathf.Abs(_machine.Voltage - _startVolts) > 0.2f) _session.Complete("mode.voltage");
                    if (c.current.Contains(_machine.Current) && Mathf.Abs(_machine.Current - _startAmps) > 2f) _session.Complete("mode.current");
                    if (c.wireSpeed.Contains(_machine.WireSpeed) && Mathf.Abs(_machine.WireSpeed - _startSpeed) > 0.1f) _session.Complete("mode.speed");
                    break;
                case StageId.Tack:
                    _joint.ShowTackMarkers(!_session.IsDone("tack.points"));
                    if (_mask.IsDown) _session.Complete("tack.mask");
                    break;
                case StageId.Weld:
                    if (_mask.IsDown) _session.Complete("weld.mask");
                    if (!_seamEvaluated && !_torch.ArcOn && _joint.Coverage >= 0.95f && !_fire.Burning)     // the run is over when the arc is let go
                    {
                        EvaluateSeam();
                        _session.Complete("weld.seam");
                    }

                    if (_session.IsDone("weld.seam") && !_torch.Held && _torch.AtRest) _session.Complete("weld.rest");
                    break;
                case StageId.Quality:
                    ShowQcPanel();
                    break;
                case StageId.Cleanup:
                    if (!_machine.PowerOn) _session.Complete("cl.power");
                    if (!_gas.IsValveOpen) _session.Complete("cl.gas");
                    if (!_torch.Held && _torch.AtRest && _square.AtRest && _gauge.AtRest && !_square.Held && !_gauge.Held) _session.Complete("cl.tools");
                    break;
            }

            // opening the gas with the cylinder loose is dangerous whenever it happens
            if (_gas.IsValveOpen && !_chainHook.Snapped && !_warnedLoose)
            {
                _warnedLoose = true;
                _session.Violation("viol.gas.loose", ScoreCategory.Safety, Severity.Critical);
            }
        }

        private bool _warnedLoose;

        private void Hold(bool ok, ref float since, float seconds, string step, bool undo)
        {
            if (ok)
            {
                if (since < 0f) since = Time.time;
                if (Time.time - since >= seconds) _session.Complete(step);
            }
            else
            {
                since = -1f;
                if (undo) _session.Undo(step);
            }
        }

        private static float HingeAngle(Transform hinge, Quaternion rest) => hinge == null ? 0f : Quaternion.Angle(hinge.localRotation, rest);

        // ------------------------------------------------------------------ task card: buttons and live values

        private List<(string key, Action action)> StageActions()
        {
            var list = new List<(string, Action)>();
            if (_session == null || _session.IsFinished)
            {
                return list;
            }

            switch (_session.StageId)
            {
                case StageId.Gas:
                    if (_session.IsRequired(Scenario.Stage(StageId.Gas).Steps[2]) && !_session.IsDone("gas.swap")) list.Add(("act.gas.swap", SwapCylinder));
                    break;
                case StageId.Mode:
                    list.Add(("act.mode.confirm", ConfirmMode));
                    break;
                case StageId.Measure:
                    list.Add(("act.meas.confirm", ConfirmMeasurement));
                    break;
                case StageId.Tack:
                    if (_session.IsDone("tack.points")) list.Add(("act.meas.confirm", ConfirmMeasurement));
                    list.Add((_mask.IsDown ? "act.mask.up" : "act.mask.down", _mask.Toggle));
                    break;
                case StageId.Weld:
                    list.Add((_mask.IsDown ? "act.mask.up" : "act.mask.down", _mask.Toggle));
                    break;
                case StageId.Quality:
                    if (_session.IsDone("qc.measure")) list.Add(("act.qc.record", RecordQuality));
                    break;
            }

            return list;
        }

        private string StageReadout()
        {
            if (_session == null || _session.IsFinished)
            {
                return string.Empty;
            }

            TrainingConfig c = TrainingConfig.Current;
            switch (_session.StageId)
            {
                case StageId.Wire: return Loc.T("read.tension", _machine.Tension.ToString("0.0"), c.rollerTension.ToString());
                case StageId.Gas: return Loc.T("read.gas", (_gas.PressureMpa).ToString("0.0"), _gas.FlowLitresPerMinute.ToString("0.0"), c.gasFlow.ToString());
                case StageId.Mode:
                    return _machine.PowerOn ? Loc.T("read.mode", _machine.Voltage.ToString("0.0"), _machine.Current.ToString("0"), _machine.WireSpeed.ToString("0.0")) : Loc.T("read.off");
                case StageId.Measure: return float.IsNaN(LastAngle) ? string.Empty : Loc.T("read.fit", LastAngle.ToString("0.0"), LastGap.ToString("0.0"));
                case StageId.Tack: return Loc.T("read.tacks", _joint.TacksDone, _joint.Tacks.Length);
                case StageId.Weld:
                    return _torch.ArcOn ? Loc.T("read.weld", Mathf.RoundToInt(_joint.Coverage * 100f), _torch.StickOutMm.ToString("0"), _torch.WorkAngle.ToString("0"))
                        : Loc.T("read.seam", Mathf.RoundToInt(_joint.Coverage * 100f));
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------ stage 11: check sheet

        private static readonly WeldDefect[] DefectList = { WeldDefect.Porosity, WeldDefect.BurnThrough, WeldDefect.LackOfFusion, WeldDefect.Uneven, WeldDefect.Spatter };

        private void ShowQcPanel()
        {
            if (_qcPanel != null || _session.IsDone("qc.visual"))
            {
                return;
            }

            _qcPanel = UiCanvas.Create("Check Sheet", new Vector2(640, 700));
            _qcPanel.transform.SetPositionAndRotation(_qcPanelAnchor.position, _qcPanelAnchor.rotation);
            _qcContent = Ui.Fill("Content", _qcPanel.Root);
            RebuildQcPanel();
        }

        private void RebuildQcPanel()
        {
            Ui.Clear(_qcContent);
            Ui.Glass(_qcContent, 0, 0, 640, 700, 28);
            Ui.Label(_qcContent, Loc.T("qc.title"), 34, 24, 572, 36, 26, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(_qcContent, Loc.T("qc.hint"), 34, 64, 572, 48, 17, UiStyle.TextDim);
            for (int i = 0; i < DefectList.Length; i++)
            {
                WeldDefect defect = DefectList[i];
                float y = 126 + i * 80f;
                Ui.Check(_qcContent, 34, y, 60, (MarkedDefects & defect) != 0, on => MarkDefect(defect, on));
                Ui.Label(_qcContent, Loc.T("defect." + defect), 112, y, 494, 60, 22, UiStyle.Text, TextAlignmentOptions.Left);
            }

            Ui.Button(_qcContent, Loc.T("act.qc.visual"), 34, 606, 572, 68, ButtonKind.Primary, SubmitDefects, 23);
        }

        public void MarkDefect(WeldDefect defect, bool on)
        {
            MarkedDefects = on ? MarkedDefects | defect : MarkedDefects & ~defect;
        }

        /// <summary>The check sheet's button: what the learner marked is compared with what the seam really has.</summary>
        public void SubmitDefects()
        {
            if (!_session.IsCurrent(StageId.Quality))
            {
                return;
            }

            WeldDefect real = _joint.VisibleDefects();
            int wrong = 0;
            foreach (WeldDefect d in DefectList)
            {
                if (((real & d) != 0) != ((MarkedDefects & d) != 0)) wrong++;
            }

            if (wrong > 0)
            {
                _session.Deduct(ScoreCategory.Technique, wrong * 4f, "deduct.qc", wrong.ToString());
            }

            _session.Complete("qc.visual");
            if (_qcPanel != null)
            {
                Destroy(_qcPanel.gameObject);
                _qcPanel = null;
            }

            if (_legMeasured[0] && _legMeasured[1] && _legMeasured[2]) _session.Complete("qc.measure");
        }

        /// <summary>The "Natijani kiritish" button.</summary>
        public void RecordQuality()
        {
            if (!_session.IsDone("qc.measure"))
            {
                _session.Blocked("blocked.qc.measure");
                return;
            }

            float mean = (_legValues[0] + _legValues[1] + _legValues[2]) / 3f;
            _session.SetValue("seam.leg", mean);
            if (!TrainingConfig.Current.legSizeMm.Contains(mean))
            {
                _session.Deduct(ScoreCategory.Quality, 6f, "deduct.leg", mean.ToString("0.0"), TrainingConfig.Current.legSizeMm.ToString());
            }

            _session.Complete("qc.record");
        }

        // ------------------------------------------------------------------ for tests

        /// <summary>Opens or closes the spool cover ("cover") or the feeder door ("door") by an angle.</summary>
        public void TestSetHinge(string which, float angle)
        {
            Transform hinge = which == "cover" ? _spoolCover : _feederDoor;
            hinge.localRotation = (which == "cover" ? _coverRest : _doorRest) * Quaternion.AngleAxis(angle, Vector3.up);
        }

        /// <summary>Runs the torch along the seam from one place to another at a speed, arc on, as a welder would hold it.</summary>
        public void TestWeld(float fromU, float toU, float speedMmS, float stickOutMm, float workAngle, float travelAngle)
        {
            StopAllCoroutines();
            StartCoroutine(WeldRoutine(fromU, toU, speedMmS, stickOutMm, workAngle, travelAngle));
        }

        public bool TestWeldRunning { get; private set; }

        private System.Collections.IEnumerator WeldRoutine(float fromU, float toU, float speedMmS, float stickOutMm, float workAngle, float travelAngle)
        {
            TestWeldRunning = true;
            float u = fromU;
            _torch.SimulatePose(true, u, stickOutMm, workAngle, travelAngle);
            _torch.SimulateTrigger(true);
            float perSecond = speedMmS * 0.001f / _joint.Length;
            while (speedMmS > 0f ? u < toU : Time.time < _testUntil)
            {
                u = Mathf.Min(toU, u + perSecond * Time.deltaTime);
                _torch.SimulatePose(true, u, stickOutMm, workAngle, travelAngle);
                yield return null;
            }

            yield return null;
            _torch.SimulateTrigger(false);
            yield return null;
            _torch.SimulatePose(false);
            TestWeldRunning = false;
        }

        private float _testUntil;

        /// <summary>Holds the arc at one place on the seam for some seconds (a tack).</summary>
        public void TestDwell(float u, float seconds)
        {
            _testUntil = Time.time + seconds;
            StopAllCoroutines();
            StartCoroutine(WeldRoutine(u, u, 0f, 12f, 45f, 0f));
        }

        /// <summary>The state of the hall as one line of text (for tests).</summary>
        public string TestState()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(ci,
                "power={0} V={1:0.0} A={2:0} speed={3:0.0} tension={4:0.0} spoolEmpty={5} wire={6} | valve={7} P={8:0.0} flow={9:0.0} | base={10} web={11} clamps={12}/{13} earth={14} chain={15} rag={16} | " +
                "fit={17:0.0}/{18:0.0} tacks={19} seam={20:0}% score={21:0.00} defects={22} arcs={23} arc={24} mask={25} fire={26}/{27} | cover={28:0} door={29:0}",
                _machine.PowerOn, _machine.Voltage, _machine.Current, _machine.WireSpeed, _machine.Tension, _machine.SpoolEmpty, _machine.WireLoaded,
                _gas.IsValveOpen, _gas.PressureMpa, _gas.FlowLitresPerMinute, _basePlate.Snapped, _webPlate.Snapped, CountSnapped(), _clamps.Length, _earthClamp.Snapped,
                _chainHook.Snapped, _rag.Snapped, LastAngle, LastGap, _joint.TacksDone, _joint.Coverage * 100f, _joint.MeanScore(), _joint.VisibleDefects(), _joint.ArcStarts,
                _torch.ArcOn, _mask.IsDown, _fire.Burning, _fire.Extinguished, HingeAngle(_spoolCover, _coverRest), HingeAngle(_feederDoor, _doorRest));
        }

        private int CountSnapped()
        {
            int n = 0;
            foreach (SnapItem clamp in _clamps)
            {
                if (clamp.Snapped) n++;
            }

            return n;
        }

        // ------------------------------------------------------------------ the end

        private void OnSessionChanged()
        {
            // nothing to rebuild here: the task card listens itself
        }

        private void OnFinished(AttemptResult result)
        {
            if (_reported)
            {
                return;
            }

            _reported = true;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            Vector3 eyes = rig != null ? rig.centerEyeAnchor.position : _reportAnchor.position - _reportAnchor.forward * 2f;
            Vector3 forward = rig != null ? rig.centerEyeAnchor.forward : _reportAnchor.forward;
            forward.y = 0f;
            Vector3 position = eyes + forward.normalized * 1.9f;
            position.y = Mathf.Max(1.35f, eyes.y - 0.1f);
            ReportPanel panel = ReportPanel.Show(result, position, eyes);
            panel.Closed = again =>
            {
                if (again)
                {
                    TrainingSession.Begin(TrainingSession.Current.User);
                }

                if (_outfit != null) _outfit.SetWorn(OutfitPart.None);
                SceneManager.LoadScene(_roomScene, LoadSceneMode.Single);
            };
        }
    }
}
