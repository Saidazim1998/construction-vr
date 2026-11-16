using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training.UI
{
    /// <summary>
    /// The task card that stays with the learner (the "QO'LLANMA" list of the design): the current stage, its
    /// goal, the steps with what is done, time and score, and the buttons the stage needs.
    /// </summary>
    public class TaskPanel : MonoBehaviour
    {
        public const float Width = 620f;
        public const float Height = 800f;

        public static TaskPanel Instance { get; private set; }

        /// <summary>Extra buttons for the current stage ("Rejimni tasdiqlash" ...): text key and what it does.</summary>
        public static Func<List<(string key, Action action)>> Actions;

        /// <summary>A live line under the goal ("Gaz sarfi: 9 l/min"), asked for twice a second.</summary>
        public static Func<string> Readout;

        public LazyFollow Follow { get; private set; }

        private RectTransform _content;
        private TextMeshProUGUI _time;
        private TextMeshProUGUI _readout;
        private Canvas _canvas;
        private float _nextTick;
        private float _nextReadout;

        public static TaskPanel Create()
        {
            UiCanvas canvas = UiCanvas.Create("Task Panel", new Vector2(Width, Height));
            var panel = canvas.gameObject.AddComponent<TaskPanel>();
            panel._content = Ui.Fill("Content", canvas.Root);
            panel._canvas = canvas.Canvas;
            panel.Follow = canvas.gameObject.AddComponent<LazyFollow>();
            panel.Follow.Distance = 1.05f;
            panel.Follow.Side = -0.62f;
            panel.Follow.Height = -0.22f;
            Instance = panel;
            panel.Rebuild();
            panel.Follow.SnapNow();
            return panel;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Actions = null;
            Readout = null;
        }

        protected virtual void OnEnable()
        {
            Loc.Changed += Rebuild;
            TrainingSession.Changed += Rebuild;
        }

        protected virtual void OnDisable()
        {
            Loc.Changed -= Rebuild;
            TrainingSession.Changed -= Rebuild;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        protected virtual void Update()
        {
            TrainingSession session = TrainingSession.Current;
            if (_time != null && session != null && Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + 1f;
                _time.text = Clock(session.IsFinished && session.Result != null ? session.Result.seconds : session.ElapsedSeconds);
            }

            // the settings window opens where the learner looks: the task card steps aside meanwhile
            bool hidden = SettingsWindow.Instance != null;
            if (_canvas != null && _canvas.enabled == hidden)
            {
                _canvas.enabled = !hidden;
            }

            if (_readout != null && Time.unscaledTime >= _nextReadout)
            {
                _nextReadout = Time.unscaledTime + 0.5f;
                string text = Readout?.Invoke() ?? string.Empty;
                if (_readout.text != text)
                {
                    _readout.text = text;
                }
            }
        }

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        public void Rebuild()
        {
            if (_content == null)
            {
                return;
            }

            Ui.Clear(_content);
            _time = null;
            _readout = null;
            TrainingSession session = TrainingSession.Current;
            bool show = session != null && session.IsDone("login.brief") && !session.IsFinished;
            if (!show)
            {
                return;                                             // nothing to do yet: the panel is empty and invisible
            }

            Ui.Glass(_content, 0, 0, Width, Height, 30);
            StageDef stage = session.Stage;
            Ui.Label(_content, Loc.T("ui.task.stage", stage.Number, session.StageIndex, Scenario.Stages.Length - 1), 34, 26, 360, 26, 18, UiStyle.AccentText,
                TextAlignmentOptions.Left, true);
            _time = Ui.Label(_content, Clock(session.ElapsedSeconds), 400, 22, 186, 34, 24, UiStyle.Text, TextAlignmentOptions.Right, true);
            Ui.Label(_content, Loc.T(stage.TitleKey), 34, 58, 552, 72, 27, UiStyle.Text, TextAlignmentOptions.TopLeft, true);
            Ui.Label(_content, Loc.T(stage.GoalKey), 34, 130, 552, 44, 16, UiStyle.TextDim);
            _readout = Ui.Label(_content, Readout?.Invoke() ?? string.Empty, 34, 174, 552, 28, 19, UiStyle.Warn, TextAlignmentOptions.Left, true);
            Ui.Box(_content, 34, 206, 552, 1.5f, 1, UiStyle.Outline, UiStyle.Outline);

            float y = 222f;
            int number = 1;
            bool currentFound = false;
            foreach (StepDef step in stage.Steps)
            {
                if (!session.IsRequired(step))
                {
                    continue;
                }

                bool done = session.IsDone(step.Id);
                bool current = !done && !currentFound;
                currentFound |= current;
                Ui.StepBadge(_content, 34, y, 48, number.ToString("00"), done, current);
                Ui.Label(_content, Loc.T(step.TextKey), 98, y - 4, 488, 58, 19, done ? UiStyle.TextDim : current ? UiStyle.Text : UiStyle.TextFaint,
                    TextAlignmentOptions.Left, current);
                y += 66f;
                number++;
            }

            // what the stage needs to be pressed
            List<(string key, Action action)> actions = Actions?.Invoke();
            float by = Height - 150f;
            if (actions != null)
            {
                for (int i = actions.Count - 1; i >= 0; i--)
                {
                    Action action = actions[i].action;
                    by -= 66f;
                    Ui.Button(_content, Loc.T(actions[i].key), 34, by, 552, 58, i == 0 ? ButtonKind.Primary : ButtonKind.Secondary, () => action(), 21);
                }
            }

            Ui.Box(_content, 34, Height - 142, 552, 1.5f, 1, UiStyle.Outline, UiStyle.Outline);
            (ScoreCategory, string)[] scores = { (ScoreCategory.Safety, "score.Safety"), (ScoreCategory.Technique, "score.Technique"), (ScoreCategory.Quality, "score.Quality") };
            for (int i = 0; i < scores.Length; i++)
            {
                float sx = 34 + i * 184f;
                float value = session.Score(scores[i].Item1);
                Ui.Label(_content, Loc.T(scores[i].Item2), sx, Height - 130, 180, 22, 15, UiStyle.TextDim);
                Ui.Label(_content, value.ToString("0"), sx, Height - 108, 180, 34, 26, value >= 85f ? UiStyle.Good : value >= 60f ? UiStyle.Warn : UiStyle.Bad,
                    TextAlignmentOptions.Left, true);
            }

            Ui.Button(_content, Loc.T(Follow != null && Follow.Pinned ? "ui.task.follow" : "ui.task.pin"), 34, Height - 68, 270, 50, ButtonKind.Ghost, TogglePin, 18);
            Ui.Button(_content, Loc.T("ui.menu.settings"), 316, Height - 68, 270, 50, ButtonKind.Ghost, () => SettingsWindow.Open(null), 18);
        }

        private void TogglePin()
        {
            Follow.Pinned = !Follow.Pinned;
            Rebuild();
        }
    }

    /// <summary>
    /// Short messages in front of the learner: a step is blocked and why, a mistake and what it cost, a fault
    /// that was found. One at a time, a few seconds each.
    /// </summary>
    public class AlertPanel : MonoBehaviour
    {
        public const float Width = 860f;
        public const float Height = 210f;

        public static AlertPanel Instance { get; private set; }

        /// <summary>The last message shown (for tests and for the log).</summary>
        public string LastText { get; private set; }
        public int Shown { get; private set; }

        private readonly Queue<(EventKind kind, string text)> _queue = new Queue<(EventKind, string)>();
        private RectTransform _content;
        private CanvasGroup _group;
        private float _hideAt;
        private AudioSource _audio;
        private AudioClip _beepLow;
        private AudioClip _beepHigh;

        public static AlertPanel Create()
        {
            UiCanvas canvas = UiCanvas.Create("Alert Panel", new Vector2(Width, Height));
            var panel = canvas.gameObject.AddComponent<AlertPanel>();
            panel._content = Ui.Fill("Content", canvas.Root);
            panel._group = canvas.gameObject.AddComponent<CanvasGroup>();
            panel._group.alpha = 0f;
            panel._group.blocksRaycasts = false;

            // The alerts are only read. Their panel floats in front of the eyes all the time, mostly invisible:
            // left pointable, it would catch every ray aimed at something behind it.
            canvas.MakeReadOnly();

            var follow = canvas.gameObject.AddComponent<LazyFollow>();
            follow.Distance = 0.95f;
            follow.Side = 0f;
            follow.Height = 0.16f;
            follow.MaxAngle = 12f;
            follow.MaxDrift = 0.15f;
            follow.Smooth = 0.12f;
            panel._audio = canvas.gameObject.AddComponent<AudioSource>();
            panel._audio.spatialBlend = 0f;
            panel._audio.playOnAwake = false;
            panel._beepLow = Tone(330f, 0.22f);
            panel._beepHigh = Tone(880f, 0.12f);
            Instance = panel;
            return panel;
        }

        protected virtual void OnEnable() => TrainingSession.Logged += OnLogged;

        protected virtual void OnDisable()
        {
            TrainingSession.Logged -= OnLogged;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnLogged(TrainingEvent e)
        {
            switch (e.kind)
            {
                case EventKind.Blocked:
                case EventKind.Warning:
                case EventKind.FaultFound:
                case EventKind.StageOpened:
                    Push(e.kind, e.kind == EventKind.StageOpened ? Loc.T("ui.alert.stage", e.Text) : e.Text);
                    break;
                case EventKind.Violation:
                    Push(e.kind, e.Text + "\n<size=80%>" + Loc.T("ui.alert.lost", Loc.T("score." + e.category), e.points.ToString("0")) + "</size>");
                    break;
            }
        }

        public void Push(EventKind kind, string text)
        {
            if (_queue.Count < 6)
            {
                _queue.Enqueue((kind, text));
            }
        }

        /// <summary>
        /// Shows a how-to hint right away, in place of whatever is on the panel: it answers something the
        /// player is doing this moment (holding an item), so it must not wait in the queue.
        /// </summary>
        public void Hint(string text) => Present(EventKind.Info, text);

        protected virtual void Update()
        {
            if (Time.unscaledTime >= _hideAt)
            {
                if (_queue.Count > 0)
                {
                    (EventKind kind, string text) = _queue.Dequeue();
                    Present(kind, text);
                }
                else if (_group.alpha > 0f)
                {
                    _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, Time.unscaledDeltaTime * 4f);
                }
            }
        }

        private void Present(EventKind kind, string text)
        {
            Ui.Clear(_content);
            Color color = kind == EventKind.Violation ? UiStyle.Bad : kind == EventKind.Blocked || kind == EventKind.Warning ? UiStyle.Warn
                : kind == EventKind.FaultFound ? UiStyle.Good : UiStyle.AccentText;
            Ui.Glass(_content, 0, 0, Width, Height, 28, false, false);
            Ui.Box(_content, 0, 0, 14, Height, 7, color, color);
            Ui.Label(_content, Loc.T("ui.alert." + kind), 44, 22, 760, 28, 19, color, TextAlignmentOptions.Left, true);
            Ui.Label(_content, text, 44, 56, 780, 140, 25, UiStyle.Text, TextAlignmentOptions.TopLeft);
            _group.alpha = 1f;
            _hideAt = Time.unscaledTime + Mathf.Clamp(2.5f + text.Length * 0.045f, 3f, 8f);
            LastText = text;
            Shown++;
            float volume = AppSettings.Current.effectsVolume;
            _audio.PlayOneShot(kind == EventKind.Violation || kind == EventKind.Blocked ? _beepLow : _beepHigh, volume);
        }

        private static AudioClip Tone(float frequency, float seconds)
        {
            const int rate = 24000;
            int count = Mathf.RoundToInt(rate * seconds);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Clamp01(t * 60f) * Mathf.Clamp01((seconds - t) * 12f);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.35f * envelope;
            }

            AudioClip clip = AudioClip.Create("tone" + frequency, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>The final report: time, the four scores, the total and the verdict, the list of mistakes, advice.</summary>
    public class ReportPanel : MonoBehaviour
    {
        public const float Width = 1500f;
        public const float Height = 960f;

        public static ReportPanel Instance { get; private set; }

        /// <summary>Called by the two buttons: true = try again, false = back to the main window.</summary>
        public Action<bool> Closed;

        private RectTransform _content;
        private AttemptResult _result;

        public static ReportPanel Show(AttemptResult result, Vector3 position, Vector3 viewer)
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
            }

            UiCanvas canvas = UiCanvas.Create("Report Panel", new Vector2(Width, Height));
            canvas.Face(position, viewer);
            var panel = canvas.gameObject.AddComponent<ReportPanel>();
            panel._content = Ui.Fill("Content", canvas.Root);
            panel._result = result;
            Instance = panel;
            panel.Rebuild();
            return panel;
        }

        protected virtual void OnEnable() => Loc.Changed += Rebuild;

        protected virtual void OnDisable()
        {
            Loc.Changed -= Rebuild;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Rebuild()
        {
            if (_content == null || _result == null)
            {
                return;
            }

            AttemptResult r = _result;
            Ui.Clear(_content);
            Ui.Glass(_content, 0, 0, Width, Height, 36);
            Ui.Label(_content, Loc.T("ui.report.title"), 56, 40, 900, 50, 38, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(_content, r.name + "   •   " + r.date + "   •   " + Loc.T("ui.report.time", TaskPanel.Clock(r.seconds)), 56, 96, 1100, 32, 22, UiStyle.TextDim);

            RectTransform verdict = Ui.Card(_content, Width - 56 - 400, 40, 400, 150, 22);
            Ui.Label(verdict, r.total.ToString("0.0") + "%", 24, 14, 352, 70, 56, r.passed ? UiStyle.Good : UiStyle.Bad, TextAlignmentOptions.Center, true);
            Ui.Label(verdict, Loc.T(r.passed ? "ui.report.passed" : r.critical ? "ui.report.critical" : "ui.report.failed"), 16, 90, 368, 44, 22,
                r.passed ? UiStyle.Good : UiStyle.Bad, TextAlignmentOptions.Center, true);

            (string key, float value)[] scores = { ("score.Safety", r.safety), ("score.Technique", r.technique), ("score.Quality", r.quality), ("score.Time", r.time) };
            for (int i = 0; i < scores.Length; i++)
            {
                RectTransform card = Ui.Card(_content, 56 + i * 262f, 150, 246, 150, 20);
                Ui.Label(card, Loc.T(scores[i].key), 20, 16, 206, 26, 19, UiStyle.TextDim);
                Color color = scores[i].value >= 85f ? UiStyle.Good : scores[i].value >= 60f ? UiStyle.Warn : UiStyle.Bad;
                Ui.Label(card, scores[i].value.ToString("0"), 20, 44, 206, 56, 46, color, TextAlignmentOptions.Left, true);
                Ui.Meter(card, 20, 112, 206, 16, scores[i].value / 100f, color);
            }

            // mistakes
            RectTransform list = Ui.Card(_content, 56, 324, 900, 520, 22);
            Ui.Label(list, Loc.T("ui.report.events"), 28, 18, 840, 30, 22, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            List<TrainingEvent> events = (r.events ?? new TrainingEvent[0]).Where(e => e.kind == EventKind.Violation || e.kind == EventKind.FaultFound).ToList();
            if (events.Count == 0)
            {
                Ui.Label(list, Loc.T("ui.report.clean"), 28, 70, 840, 40, 22, UiStyle.Good);
            }

            for (int i = 0; i < Mathf.Min(events.Count, 9); i++)
            {
                TrainingEvent e = events[i];
                float y = 62 + i * 50f;
                bool good = e.kind == EventKind.FaultFound;
                Ui.Box(list, 28, y + 14, 14, 14, 7, good ? UiStyle.Good : UiStyle.Bad, good ? UiStyle.Good : UiStyle.Bad);
                Ui.Label(list, Scenario.Stages[Mathf.Clamp(e.stage, 0, Scenario.Stages.Length - 1)].Number, 54, y, 60, 44, 18, UiStyle.TextDim, TextAlignmentOptions.Left);
                Ui.Label(list, e.Text, 112, y, 650, 44, 18, UiStyle.Text, TextAlignmentOptions.Left);
                Ui.Label(list, good ? "+" : "−" + e.points.ToString("0"), 770, y, 100, 44, 20, good ? UiStyle.Good : UiStyle.Bad, TextAlignmentOptions.Right, true);
            }

            if (events.Count > 9)
            {
                Ui.Label(list, Loc.T("ui.report.more", events.Count - 9), 28, 480, 840, 30, 17, UiStyle.TextFaint);
            }

            // advice: the weakest part first
            RectTransform advice = Ui.Card(_content, 980, 324, 464, 520, 22);
            Ui.Label(advice, Loc.T("ui.report.advice"), 28, 18, 400, 30, 22, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            float ay = 64f;
            foreach ((string key, float value) in scores.OrderBy(s => s.value))
            {
                if (value >= 90f && ay > 64f)
                {
                    continue;
                }

                Ui.Label(advice, Loc.T(value >= 90f ? "ui.report.advice.good" : key + ".advice"), 28, ay, 408, 104, 18, UiStyle.TextDim);
                ay += 112f;
            }

            Ui.Button(_content, Loc.T("ui.report.home"), 56, 868, 420, 64, ButtonKind.Secondary, () => Closed?.Invoke(false), 23);
            Ui.Button(_content, Loc.T("ui.report.again"), Width - 56 - 420, 868, 420, 64, ButtonKind.Primary, () => Closed?.Invoke(true), 24);
        }
    }
}
