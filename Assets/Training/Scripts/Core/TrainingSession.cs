using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConstructionVR.Training
{
    public enum EventKind { Info = 0, Done, StageOpened, Warning, Blocked, Violation, FaultFound }

    [Serializable]
    public class TrainingEvent
    {
        public float time;                  // seconds since the practice started
        public int stage;
        public EventKind kind;
        public string key;                  // text key
        public string[] args;
        public ScoreCategory category;
        public Severity severity;
        public float points;                // taken off the category

        public string Text => Loc.T(key, args);
    }

    /// <summary>
    /// One learner going through the practice: who, which stage, which steps are done, what went wrong, the score.
    /// It lives outside the scenes (static), so it carries from the engineers' room into the welding hall.
    /// Scene objects only report: <see cref="Complete"/> for a finished step, <see cref="Violation"/> for a
    /// mistake, <see cref="Blocked"/> when the learner tries something that is not allowed yet.
    /// </summary>
    public sealed class TrainingSession
    {
        public static TrainingSession Current { get; private set; }

        /// <summary>Anything changed (step done, stage opened, score).</summary>
        public static event Action Changed;

        /// <summary>A new entry in the log; the UI shows warnings and violations as alerts.</summary>
        public static event Action<TrainingEvent> Logged;

        public static event Action<StageId> StageOpened;
        public static event Action<AttemptResult> Finished;

        public UserAccount User { get; private set; }
        public string PracticeId { get; private set; }
        public ScenarioVariant Variant { get; private set; }
        public int StageIndex { get; private set; }
        public bool IsFinished { get; private set; }
        public AttemptResult Result { get; private set; }
        public readonly List<TrainingEvent> Events = new List<TrainingEvent>();

        /// <summary>Numbers the learner has set (volts, amperes, litres per minute, measured angle ...), by name.</summary>
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);

        private readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _required = new HashSet<string>(StringComparer.Ordinal);
        private readonly float[] _lost = new float[4];
        private readonly float[] _stageSeconds = new float[Scenario.Stages.Length];
        private double _startedAt;
        private double _stageStartedAt;
        private bool _critical;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            // numbers look the same on every device ("13.5", not "13,5" on some)
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Current = null;
            Changed = null;
            Logged = null;
            StageOpened = null;
            Finished = null;
        }

        /// <summary>Starts a new attempt for a user who has logged in.</summary>
        public static TrainingSession Begin(UserAccount user)
        {
            var session = new TrainingSession
            {
                User = user,
                Variant = ScenarioVariant.Roll(TrainingConfig.Current),
                _startedAt = Time.realtimeSinceStartupAsDouble,
            };
            session._stageStartedAt = session._startedAt;
            Current = session;
            session.Complete("login.enter");
            Changed?.Invoke();
            return session;
        }

        public static void End()
        {
            Current = null;
            Changed?.Invoke();
        }

        public StageDef Stage => Scenario.Stages[Mathf.Min(StageIndex, Scenario.Stages.Length - 1)];
        public StageId StageId => Stage.Id;
        public float ElapsedSeconds => (float)(Time.realtimeSinceStartupAsDouble - _startedAt);

        public bool IsOpen(StageId stage) => (int)stage <= StageIndex;
        public bool IsCurrent(StageId stage) => !IsFinished && (int)stage == StageIndex;
        public bool IsPast(StageId stage) => (int)stage < StageIndex;
        public bool IsDone(string stepId) => _done.Contains(stepId);

        /// <summary>True when the step counts for its stage (always for normal steps; optional ones once required).</summary>
        public bool IsRequired(StepDef step) => !step.Optional || _required.Contains(step.Id);

        public void SelectPractice(string practiceId)
        {
            PracticeId = practiceId;
            Complete("login.practice");
        }

        /// <summary>Makes an optional step part of this session (e.g. the spool turned out to be empty).</summary>
        public void Require(string stepId)
        {
            if (_required.Add(stepId))
            {
                Changed?.Invoke();
            }
        }

        public void SetValue(string name, float value) => Values[name] = value;

        public float GetValue(string name, float fallback = 0f) => Values.TryGetValue(name, out float v) ? v : fallback;

        /// <summary>
        /// Reports a finished step. Returns false, and tells the learner why, when the step belongs to a stage
        /// that is not open yet (the stages are done in strict order).
        /// </summary>
        public bool Complete(string stepId)
        {
            if (IsFinished || !Scenario.Find(stepId, out int stage, out _))
            {
                return false;
            }

            if (stage > StageIndex)
            {
                Blocked("blocked.order", Loc.T(Stage.TitleKey));
                return false;
            }

            if (!_done.Add(stepId))
            {
                return true;
            }

            Log(new TrainingEvent { kind = EventKind.Done, key = "step." + stepId });
            Advance();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Takes a step back (e.g. a clamp was removed again). Only inside the current stage.</summary>
        public void Undo(string stepId)
        {
            if (!IsFinished && Scenario.Find(stepId, out int stage, out _) && stage == StageIndex && _done.Remove(stepId))
            {
                Changed?.Invoke();
            }
        }

        public void Info(string key, params string[] args) => Log(new TrainingEvent { kind = EventKind.Info, key = key, args = args });

        public void Warning(string key, params string[] args) => Log(new TrainingEvent { kind = EventKind.Warning, key = key, args = args });

        /// <summary>The learner tried something that is not allowed yet: nothing is lost, the reason is shown.</summary>
        public void Blocked(string key, params string[] args) => Log(new TrainingEvent { kind = EventKind.Blocked, key = key, args = args });

        /// <summary>A planted fault was found by the learner.</summary>
        public void FaultFound(string key, params string[] args) => Log(new TrainingEvent { kind = EventKind.FaultFound, key = key, args = args });

        /// <summary>A mistake: points are taken off the category according to its severity.</summary>
        public void Violation(string key, ScoreCategory category, Severity severity, params string[] args)
        {
            float points = TrainingConfig.Current.Penalty(severity);
            _lost[(int)category] += points;
            if (severity == Severity.Critical)
            {
                _critical = true;
            }

            Log(new TrainingEvent { kind = EventKind.Violation, key = key, args = args, category = category, severity = severity, points = points });
            Changed?.Invoke();
        }

        /// <summary>Takes points off a category directly (seam quality, measured in points rather than by severity).</summary>
        public void Deduct(ScoreCategory category, float points, string key, params string[] args)
        {
            if (points < 0.5f)
            {
                return;                                             // too little to show as a mistake
            }

            _lost[(int)category] += points;
            Log(new TrainingEvent { kind = EventKind.Violation, key = key, args = args, category = category, severity = Severity.Low, points = points });
            Changed?.Invoke();
        }

        public float Score(ScoreCategory category)
        {
            if (category == ScoreCategory.Time)
            {
                TrainingConfig config = TrainingConfig.Current;
                float seconds = IsFinished && Result != null ? Result.seconds : ElapsedSeconds;
                float over = seconds / 60f - config.normMinutes;
                return Mathf.Clamp(100f - Mathf.Max(0f, over) * config.timePenaltyPerMinute, 0f, 100f);
            }

            return Mathf.Clamp(100f - _lost[(int)category], 0f, 100f);
        }

        public float Total
        {
            get
            {
                TrainingConfig c = TrainingConfig.Current;
                float weights = c.weightSafety + c.weightTechnique + c.weightQuality + c.weightTime;
                return (Score(ScoreCategory.Safety) * c.weightSafety + Score(ScoreCategory.Technique) * c.weightTechnique +
                        Score(ScoreCategory.Quality) * c.weightQuality + Score(ScoreCategory.Time) * c.weightTime) / Mathf.Max(0.01f, weights);
            }
        }

        public bool HasCritical => _critical;

        /// <summary>Steps of a stage that still have to be done.</summary>
        public int Remaining(StageDef stage)
        {
            int n = 0;
            foreach (StepDef step in stage.Steps)
            {
                if (IsRequired(step) && !_done.Contains(step.Id))
                {
                    n++;
                }
            }

            return n;
        }

        private void Advance()
        {
            while (StageIndex < Scenario.Stages.Length && Remaining(Scenario.Stages[StageIndex]) == 0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                _stageSeconds[StageIndex] = (float)(now - _stageStartedAt);
                _stageStartedAt = now;
                StageIndex++;
                if (StageIndex >= Scenario.Stages.Length)
                {
                    Finish();
                    return;
                }

                Log(new TrainingEvent { kind = EventKind.StageOpened, key = Stage.TitleKey });
                StageOpened?.Invoke(Stage.Id);
            }
        }

        private void Finish()
        {
            StageIndex = Scenario.Stages.Length - 1;
            var result = new AttemptResult
            {
                login = User.login,
                name = User.name,
                practice = PracticeId,
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                seconds = ElapsedSeconds,
                variant = Variant.ToString(),
                stageSeconds = (float[])_stageSeconds.Clone(),
                events = Events.FindAll(e => e.kind == EventKind.Violation || e.kind == EventKind.FaultFound || e.kind == EventKind.Blocked).ToArray(),
            };
            Result = result;
            IsFinished = true;
            result.safety = Score(ScoreCategory.Safety);
            result.technique = Score(ScoreCategory.Technique);
            result.quality = Score(ScoreCategory.Quality);
            result.time = Score(ScoreCategory.Time);
            result.total = Total;
            result.passed = result.total >= TrainingConfig.Current.passPercent && !_critical;
            result.critical = _critical;
            ResultsStore.Add(result);
            Finished?.Invoke(result);
        }

        private void Log(TrainingEvent e)
        {
            e.time = ElapsedSeconds;
            e.stage = StageIndex;
            Events.Add(e);
            Logged?.Invoke(e);
        }
    }
}
