using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ConstructionVR.Training.UI
{
    /// <summary>
    /// The main window of the design (1920 x 1080): log in, pick a practice, read the task, and the MENU with
    /// the guide, the results and the settings. It stands in the engineers' room where the learner starts.
    /// </summary>
    public class HubPanel : MonoBehaviour
    {
        public enum Screen { Login, Home, Brief, Active, Menu, Guide, Results }

        public const float Width = 1920f;
        public const float Height = 1080f;

        public static HubPanel Instance { get; private set; }

        public Screen Current { get; private set; }
        public string LoginText => _login;
        public string ErrorKey { get; private set; }

        private UiCanvas _canvas;
        private RectTransform _content;
        private TextMeshProUGUI _clock;
        private TextMeshProUGUI _loginField;
        private TextMeshProUGUI _passwordField;
        // the sample account is typed in already: the learner only presses "Kirish"
        private const string DefaultLogin = "stajyor";
        private const string DefaultPassword = "1234";

        private string _login = DefaultLogin;
        private string _password = DefaultPassword;
        private bool _editPassword;
        private float _nextClock;
        private Screen _back = Screen.Home;

        public static HubPanel Create(Vector3 position, Vector3 viewer)
        {
            UiCanvas canvas = UiCanvas.Create("Hub Panel", new Vector2(Width, Height));
            canvas.OnTop = false;                               // fixed in the room and typed on with a finger: the hand stays in front
            canvas.Face(position, viewer);
            var hub = canvas.gameObject.AddComponent<HubPanel>();
            hub._canvas = canvas;
            hub._content = Ui.Fill("Content", canvas.Root);
            Instance = hub;
            TrainingSession session = TrainingSession.Current;
            hub.Show(session == null ? Screen.Login : session.IsDone("login.brief") && !session.IsFinished ? Screen.Active : Screen.Home);
            return hub;
        }

        protected virtual void OnEnable()
        {
            Loc.Changed += Rebuild;
            TrainingSession.Changed += OnSessionChanged;
        }

        protected virtual void OnDisable()
        {
            Loc.Changed -= Rebuild;
            TrainingSession.Changed -= OnSessionChanged;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        protected virtual void Update()
        {
            if (_clock != null && Time.unscaledTime >= _nextClock)
            {
                _nextClock = Time.unscaledTime + 5f;
                _clock.text = DateTime.Now.ToString("dd.MM.yyyy   HH:mm");
            }
        }

        private void OnSessionChanged()
        {
            if (Current == Screen.Active || Current == Screen.Guide)
            {
                Rebuild();
            }
        }

        public void Show(Screen screen)
        {
            Current = screen;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_content == null)
            {
                return;
            }

            Ui.Clear(_content);
            _clock = null;
            _loginField = _passwordField = null;
            Ui.Glass(_content, 0, 0, Width, Height, 40);
            Header();
            switch (Current)
            {
                case Screen.Login: BuildLogin(); break;
                case Screen.Home: BuildHome(); break;
                case Screen.Brief: BuildBrief(); break;
                case Screen.Active: BuildActive(); break;
                case Screen.Menu: BuildMenu(); break;
                case Screen.Guide: BuildGuide(); break;
                case Screen.Results: BuildResults(); break;
            }
        }

        // ------------------------------------------------------------------ header

        private void Header()
        {
            Ui.Box(_content, 64, 62, 46, 46, 23, UiStyle.AccentTop, UiStyle.AccentBottom);
            Ui.Label(_content, "TY", 64, 62, 46, 46, 20, Color.white, TextAlignmentOptions.Center, true);
            Ui.Label(_content, Loc.T("ui.brand.owner"), 124, 56, 600, 30, 21, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(_content, Loc.T("ui.brand.product"), 124, 84, 600, 36, 28, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            _clock = Ui.Label(_content, DateTime.Now.ToString("dd.MM.yyyy   HH:mm"), Width / 2f - 250, 64, 500, 40, 28, UiStyle.Text, TextAlignmentOptions.Center);

            TrainingSession session = TrainingSession.Current;
            if (session != null)
            {
                Ui.Box(_content, Width - 420, 60, 50, 50, 25, UiStyle.CardTop, UiStyle.CardBottom);
                Ui.Label(_content, Initials(session.User.name), Width - 420, 60, 50, 50, 20, Color.white, TextAlignmentOptions.Center, true);
                Ui.Label(_content, session.User.name, Width - 356, 56, 300, 30, 22, UiStyle.Text, TextAlignmentOptions.Left, true);
                Ui.Label(_content, Loc.T("role." + session.User.role), Width - 356, 86, 300, 26, 18, UiStyle.TextDim, TextAlignmentOptions.Left);
            }
        }

        private static string Initials(string name)
        {
            string[] parts = (name ?? "?").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "?" : parts.Length == 1 ? parts[0].Substring(0, 1).ToUpperInvariant()
                : (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
        }

        // ------------------------------------------------------------------ login

        private void BuildLogin()
        {
            const float cardW = 560f, cardH = 470f;
            float x = (Width - cardW) / 2f, y = 150f;
            RectTransform card = Ui.Glass(_content, x, y, cardW, cardH, 30, false, false);
            Ui.Label(card, Loc.T("ui.login.title"), 48, 34, 460, 50, 38, UiStyle.Text, TextAlignmentOptions.Left, true);

            Ui.Label(card, Loc.T("ui.login.user"), 48, 104, 460, 28, 21, UiStyle.TextDim, TextAlignmentOptions.Left, true);
            _loginField = Field(card, 48, 136, false);
            Ui.Label(card, Loc.T("ui.login.password"), 48, 216, 460, 28, 21, UiStyle.TextDim, TextAlignmentOptions.Left, true);
            _passwordField = Field(card, 48, 248, true);
            if (!string.IsNullOrEmpty(ErrorKey))
            {
                Ui.Label(card, Loc.T(ErrorKey), 48, 318, 464, 30, 20, UiStyle.Bad, TextAlignmentOptions.Left);
            }

            Ui.Button(card, Loc.T("ui.login.enter"), 48, 356, 464, 64, ButtonKind.Primary, SubmitLogin, 26);
            Ui.Label(_content, Loc.T("ui.login.hint"), x - 200, y + cardH + 12, cardW + 400, 28, 19, UiStyle.TextFaint, TextAlignmentOptions.Center);
            Keyboard(_content, (Width - 10 * 78f) / 2f, 690f);
            RefreshFields();
        }

        private TextMeshProUGUI Field(Transform parent, float x, float y, bool password)
        {
            var button = Ui.Button(parent, string.Empty, x, y, 464, 60, ButtonKind.Ghost, () =>
            {
                _editPassword = password;
                RefreshFields();
            });
            button.image.color = UiStyle.Field;
            TextMeshProUGUI text = Ui.Label(button.transform, string.Empty, 22, 0, 420, 60, 26, UiStyle.Text, TextAlignmentOptions.Left);
            Ui.Border(button.transform, 16, UiStyle.OutlineBright, 2f).name = "Focus";
            return text;
        }

        private void RefreshFields()
        {
            if (_loginField == null)
            {
                return;
            }

            _loginField.text = _login + (!_editPassword ? "<color=#60ACFF>|</color>" : string.Empty);
            _passwordField.text = new string('•', _password.Length) + (_editPassword ? "<color=#60ACFF>|</color>" : string.Empty);
            _loginField.transform.parent.Find("Focus").gameObject.SetActive(!_editPassword);
            _passwordField.transform.parent.Find("Focus").gameObject.SetActive(_editPassword);
        }

        private void Keyboard(Transform parent, float x, float y)
        {
            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };
            for (int r = 0; r < rows.Length; r++)
            {
                float offset = r >= 2 ? 39f * (r - 1) : 0f;
                for (int i = 0; i < rows[r].Length; i++)
                {
                    string key = rows[r][i].ToString();
                    Ui.Button(parent, key, x + offset + i * 78f, y + r * 78f, 70f, 70f, ButtonKind.Key, () => TypeKey(key), 28f);
                }
            }

            Ui.Button(parent, "<<", x + 10 * 78f + 8f, y, 110f, 70f, ButtonKind.Key, () => TypeKey("\b"), 24f);
            Ui.Button(parent, Loc.T("ui.login.next"), x + 10 * 78f + 8f, y + 78f, 190f, 70f, ButtonKind.Key, () => TypeKey("\t"), 22f);
            Ui.Button(parent, Loc.T("ui.login.enter"), x + 10 * 78f + 8f, y + 156f, 190f, 70f, ButtonKind.Primary, SubmitLogin, 22f);
        }

        /// <summary>One key of the on-panel keyboard: a character, "\b" (delete) or "\t" (other field).</summary>
        public void TypeKey(string key)
        {
            if (Current != Screen.Login)
            {
                return;
            }

            if (key == "\t")
            {
                _editPassword = !_editPassword;
            }
            else if (key == "\b")
            {
                if (_editPassword && _password.Length > 0) _password = _password.Substring(0, _password.Length - 1);
                else if (!_editPassword && _login.Length > 0) _login = _login.Substring(0, _login.Length - 1);
            }
            else if ((_editPassword ? _password.Length : _login.Length) < 20)
            {
                if (_editPassword) _password += key;
                else _login += key;
            }

            RefreshFields();
        }

        public void SubmitLogin()
        {
            if (UserStore.TryLogin(_login, _password, out UserAccount account))
            {
                ErrorKey = null;
                _login = DefaultLogin;                  // ready for the next time the login screen is shown
                _password = DefaultPassword;
                _editPassword = false;
                TrainingSession.Begin(account);
                Show(Screen.Home);
            }
            else
            {
                ErrorKey = "ui.login.wrong";
                _password = string.Empty;
                _editPassword = true;
                Rebuild();
            }
        }

        // ------------------------------------------------------------------ home: profile and practices

        private void BuildHome()
        {
            TrainingSession session = TrainingSession.Current;
            if (session == null)
            {
                Show(Screen.Login);
                return;
            }

            List<AttemptResult> mine = ResultsStore.All.Where(r => r.login == session.User.login).ToList();
            float average = mine.Count > 0 ? mine.Average(r => r.total) : 0f;
            float best = mine.Count > 0 ? mine.Max(r => r.total) : 0f;

            // profile
            RectTransform profile = Ui.Card(_content, 64, 180, 440, 420, 24);
            Ui.Box(profile, 28, 28, 96, 96, 20, UiStyle.AccentTop, UiStyle.AccentBottom);
            Ui.Label(profile, Initials(session.User.name), 28, 28, 96, 96, 38, Color.white, TextAlignmentOptions.Center, true);
            Ui.Label(profile, session.User.name, 144, 30, 280, 60, 26, UiStyle.Text, TextAlignmentOptions.TopLeft, true);
            Ui.Tag(profile, 144, 96, Loc.T("role." + session.User.role), new Color32(150, 196, 255, 255));
            Ui.Label(profile, Loc.T("ui.home.overall"), 28, 156, 380, 26, 19, UiStyle.TextDim);
            Ui.Meter(profile, 28, 190, 250, 30, average / 100f, UiStyle.Good);
            Ui.Label(profile, mine.Count > 0 ? average.ToString("0.0") + "%" : "—", 296, 184, 130, 40, 30, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(profile, Loc.T("ui.home.attempts"), 28, 246, 190, 24, 17, UiStyle.TextDim);
            Ui.Label(profile, mine.Count.ToString(), 28, 272, 190, 34, 26, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(profile, Loc.T("ui.home.last"), 228, 246, 200, 24, 17, UiStyle.TextDim);
            Ui.Label(profile, mine.Count > 0 ? mine[mine.Count - 1].date : "—", 228, 272, 200, 34, 22, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Button(profile, Loc.T("ui.menu.settings"), 28, 336, 384, 58, ButtonKind.Secondary, () => SettingsWindow.Open(transform), 22);

            RectTransform side = Ui.Card(_content, 64, 620, 440, 380, 24);
            Ui.Label(side, Loc.T("ui.home.menu.title"), 28, 24, 380, 30, 22, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Button(side, Loc.T("ui.menu.guide"), 28, 72, 384, 58, ButtonKind.Secondary, () => Open(Screen.Guide), 22);
            Ui.Button(side, Loc.T("ui.menu.analytics"), 28, 142, 384, 58, ButtonKind.Secondary, () => Open(Screen.Results), 22);
            Ui.Button(side, Loc.T("ui.menu.title"), 28, 212, 384, 58, ButtonKind.Secondary, () => Open(Screen.Menu), 22);
            Ui.Button(side, Loc.T("ui.home.logout"), 28, 296, 384, 58, ButtonKind.Ghost, Logout, 22);

            // tabs
            string[] tabs = { "ui.home.tab.practice", "ui.home.tab.prepare", "ui.home.tab.exam" };
            for (int i = 0; i < tabs.Length; i++)
            {
                float tx = 560 + i * 432f;
                Ui.Label(_content, Loc.T(tabs[i]), tx, 186, 420, 36, 23, i == 0 ? UiStyle.AccentText : UiStyle.TextDim, TextAlignmentOptions.Center, i == 0);
                Color line = i == 0 ? UiStyle.Accent : new Color32(120, 150, 200, 90);
                Ui.Box(_content, tx, 230, 420, i == 0 ? 3 : 1.5f, 1, line, line);
            }

            // practice cards
            PracticeCard(580, 280, true, best, mine.Count);
            PracticeCard(1020, 280, false, 0f, 0);
            PracticeCard(1460, 280, false, 0f, 0);
        }

        private void PracticeCard(float x, float y, bool available, float best, int attempts)
        {
            const float w = 410f, h = 700f;
            RectTransform card = Ui.Card(_content, x, y, w, h, 22);
            UnityEngine.UI.Image picture = Ui.Box(card, 0, 0, w, 330, 22, new Color32(40, 92, 170, 255), new Color32(10, 30, 70, 255));
            var texture = available ? Resources.Load<Texture2D>("practice_mag_co2") : null;
            if (texture != null)
            {
                RectTransform photo = Ui.Node("Photo", picture.transform, 6, 6, w - 12, 318);
                var raw = photo.gameObject.AddComponent<UnityEngine.UI.RawImage>();
                raw.texture = texture;
                raw.material = UiStyle.ImageMaterial;       // the default UI material is drawn in one eye only
                raw.raycastTarget = false;
            }
            else
            {
                Ui.Label(picture.transform, available ? "CO2 / MAG" : Loc.T("ui.home.soon"), 0, 130, w, 70, 44, new Color32(255, 255, 255, 70), TextAlignmentOptions.Center, true);
            }

            if (available)
            {
                Ui.Tag(card, 18, 18, Loc.T(attempts > 0 ? "ui.home.tag.done" : "ui.home.tag.new"), attempts > 0 ? UiStyle.Warn : new Color32(170, 210, 255, 255));
            }

            Ui.Label(card, Loc.T(available ? "practice.mag.title" : "ui.home.soon.title"), 24, 350, w - 48, 104, 27, available ? UiStyle.Text : UiStyle.TextFaint,
                TextAlignmentOptions.TopLeft, true);
            Ui.Label(card, Loc.T("ui.home.kind", Mathf.RoundToInt(TrainingConfig.Current.normMinutes)), 24, 462, w - 48, 28, 19, UiStyle.AccentText);
            Ui.Box(card, 24, 506, w - 48, 1.5f, 1, UiStyle.Outline, UiStyle.Outline);
            if (available)
            {
                Ui.Label(card, Loc.T("ui.home.best"), 24, 524, 200, 24, 17, UiStyle.TextDim);
                Ui.Label(card, attempts > 0 ? best.ToString("0") + "%" : "—", 24, 548, 200, 38, 28, UiStyle.Good, TextAlignmentOptions.Left, true);
                Ui.Label(card, Loc.T("ui.home.attempts"), 220, 524, 170, 24, 17, UiStyle.TextDim);
                Ui.Label(card, attempts.ToString(), 220, 548, 170, 38, 28, UiStyle.Text, TextAlignmentOptions.Left, true);
                Ui.Button(card, Loc.T(attempts > 0 ? "ui.home.retry" : "ui.home.start"), 24, 612, w - 48, 64,
                    attempts > 0 ? ButtonKind.Secondary : ButtonKind.Primary, PickPractice, 25);
            }
            else
            {
                Ui.Label(card, Loc.T("ui.home.soon.text"), 24, 530, w - 48, 90, 19, UiStyle.TextFaint);
            }
        }

        public void PickPractice()
        {
            TrainingSession session = TrainingSession.Current;
            if (session == null)
            {
                return;
            }

            if (session.IsFinished || session.IsDone("login.brief"))
            {
                session = TrainingSession.Begin(session.User);          // a new attempt
            }

            session.SelectPractice(Scenario.PracticeId);
            Show(Screen.Brief);
        }

        private void Logout()
        {
            TrainingSession.End();
            Show(Screen.Login);
        }

        private void Open(Screen screen)
        {
            _back = Current == Screen.Menu ? _back : Current;
            Show(screen);
        }

        // ------------------------------------------------------------------ the task: drawing, parameters, marking

        private void BuildBrief()
        {
            TrainingConfig c = TrainingConfig.Current;
            Ui.Label(_content, Loc.T("practice.mag.title"), 64, 164, 1500, 50, 36, UiStyle.Text, TextAlignmentOptions.Left, true);

            // drawing of the T joint
            RectTransform drawing = Ui.Card(_content, 64, 240, 620, 640, 24);
            Ui.Label(drawing, Loc.T("ui.brief.drawing"), 28, 20, 560, 30, 22, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            Color steel = new Color32(170, 188, 214, 255);
            Ui.Box(drawing, 130, 430, 360, 44, 3, steel, steel * 0.8f);                 // base plate
            Ui.Box(drawing, 288, 150, 44, 280, 3, steel, steel * 0.8f);                 // standing plate
            foreach (float side in new[] { -1f, 1f })                                   // the two fillets
            {
                RectTransform fillet = Ui.Node("Fillet", drawing, 310 + side * 22 + (side < 0 ? -30 : 0), 400, 30, 30);
                Ui.Box(fillet, 0, 0, 30, 30, 4, UiStyle.Warn, UiStyle.Warn);
            }

            Ui.Label(drawing, "8", 344, 270, 60, 30, 22, UiStyle.Text);
            Ui.Label(drawing, "8", 500, 436, 60, 30, 22, UiStyle.Text);
            Ui.Label(drawing, "90°", 210, 380, 80, 30, 22, UiStyle.Text);
            Ui.Label(drawing, Loc.T("ui.brief.leg", c.legSizeMm.min, c.legSizeMm.max), 360, 386, 240, 30, 21, UiStyle.Warn);
            Ui.Label(drawing, Loc.T("ui.brief.length", c.seamLengthMm), 130, 490, 360, 30, 21, UiStyle.TextDim, TextAlignmentOptions.Center);
            Ui.Label(drawing, Loc.T("ui.brief.material"), 28, 548, 560, 80, 20, UiStyle.TextDim);

            // technical task
            RectTransform task = Ui.Card(_content, 708, 240, 590, 640, 24);
            Ui.Label(task, Loc.T("ui.brief.task"), 28, 20, 530, 30, 22, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            (string, string)[] rows =
            {
                ("ui.brief.voltage", c.voltage + " V"), ("ui.brief.current", c.current + " A"), ("ui.brief.speed", c.wireSpeed + " m/min"),
                ("ui.brief.flow", c.gasFlow + " l/min"), ("ui.brief.stickout", c.stickOutMm + " mm"), ("ui.brief.angle", c.workAngleDeg + "°"),
                ("ui.brief.travel", c.travelSpeedMmS + " mm/s"), ("ui.brief.tacks", c.tackCount.ToString()),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float ry = 70 + i * 62f;
                Ui.Label(task, Loc.T(rows[i].Item1), 28, ry, 330, 50, 21, UiStyle.TextDim, TextAlignmentOptions.Left);
                Ui.Label(task, rows[i].Item2, 360, ry, 200, 50, 24, UiStyle.Text, TextAlignmentOptions.Right, true);
                Ui.Box(task, 28, ry + 54, 534, 1, 1, UiStyle.Outline, UiStyle.Outline);
            }

            Ui.Label(task, Loc.T("ui.brief.note"), 28, 574, 534, 56, 17, UiStyle.TextFaint);

            // marking
            RectTransform marks = Ui.Card(_content, 1322, 240, 534, 640, 24);
            Ui.Label(marks, Loc.T("ui.brief.marks"), 28, 20, 480, 30, 22, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            (string, float)[] weights =
            {
                ("score.Safety", c.weightSafety), ("score.Technique", c.weightTechnique), ("score.Quality", c.weightQuality), ("score.Time", c.weightTime),
            };
            for (int i = 0; i < weights.Length; i++)
            {
                float ry = 74 + i * 86f;
                Ui.Label(marks, Loc.T(weights[i].Item1), 28, ry, 330, 30, 23, UiStyle.Text, TextAlignmentOptions.Left, true);
                Ui.Label(marks, (weights[i].Item2 * 100f).ToString("0") + "%", 380, ry, 126, 30, 23, UiStyle.Text, TextAlignmentOptions.Right, true);
                Ui.Label(marks, Loc.T(weights[i].Item1 + ".hint"), 28, ry + 32, 478, 46, 17, UiStyle.TextDim);
            }

            Ui.Label(marks, Loc.T("ui.brief.pass", c.passPercent, c.normMinutes), 28, 430, 478, 190, 20, UiStyle.TextDim);

            Ui.Button(_content, Loc.T("ui.back"), 64, 920, 300, 68, ButtonKind.Secondary, () => Show(Screen.Home), 24);
            Ui.Button(_content, Loc.T("ui.brief.begin"), Width - 64 - 520, 920, 520, 68, ButtonKind.Primary, BeginPractice, 26);
        }

        public void BeginPractice()
        {
            TrainingSession session = TrainingSession.Current;
            if (session != null && session.Complete("login.brief"))
            {
                Show(Screen.Active);
            }
        }

        // ------------------------------------------------------------------ while the practice runs

        private void BuildActive()
        {
            TrainingSession session = TrainingSession.Current;
            if (session == null)
            {
                Show(Screen.Login);
                return;
            }

            Ui.Label(_content, Loc.T("practice.mag.title"), 64, 164, 1500, 50, 34, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(_content, Loc.T(session.IsFinished ? "ui.active.finished" : "ui.active.running"), 64, 218, 1500, 34, 23, UiStyle.AccentText);
            StageList(_content, 64, 280, 1130, session);

            RectTransform side = Ui.Card(_content, 1240, 280, 616, 700, 24);
            Ui.Label(side, Loc.T("ui.active.now"), 28, 22, 560, 28, 20, UiStyle.TextDim);
            Ui.Label(side, session.Stage.Number + ". " + Loc.T(session.Stage.TitleKey), 28, 54, 560, 80, 28, UiStyle.Text, TextAlignmentOptions.TopLeft, true);
            Ui.Label(side, Loc.T(session.Stage.GoalKey), 28, 140, 560, 110, 20, UiStyle.TextDim);
            Ui.Label(side, Loc.T("ui.active.where." + (session.Stage.InHall || session.IsDone("ppe.wear") ? "hall" : "room")), 28, 262, 560, 90, 21, UiStyle.Warn);
            Ui.Button(side, Loc.T("ui.menu.title"), 28, 470, 560, 62, ButtonKind.Secondary, () => Open(Screen.Menu), 23);
            Ui.Button(side, Loc.T("ui.menu.settings"), 28, 546, 560, 62, ButtonKind.Secondary, () => SettingsWindow.Open(transform), 23);
            Ui.Button(side, Loc.T("ui.active.home"), 28, 622, 560, 62, ButtonKind.Ghost, () => Show(Screen.Home), 23);
        }

        /// <summary>All stages in two columns, with what is done.</summary>
        private static void StageList(Transform parent, float x, float y, float w, TrainingSession session)
        {
            int half = (Scenario.Stages.Length + 1) / 2;
            float columnW = w / 2f;
            for (int i = 0; i < Scenario.Stages.Length; i++)
            {
                StageDef stage = Scenario.Stages[i];
                float cx = x + (i / half) * columnW, cy = y + (i % half) * 88f;
                bool done = session != null && (session.StageIndex > i || session.IsFinished);
                bool current = session != null && session.StageIndex == i && !session.IsFinished;
                Ui.StepBadge(parent, cx, cy, 60, stage.Number, done, current);
                Ui.Label(parent, Loc.T(stage.TitleKey), cx + 78, cy, columnW - 96, 60, 21, done ? UiStyle.TextDim : current ? UiStyle.Text : UiStyle.TextFaint,
                    TextAlignmentOptions.Left, current);
            }
        }

        // ------------------------------------------------------------------ menu

        private void BuildMenu()
        {
            RectTransform box = Ui.Glass(_content, 410, 190, 1100, 720, 36, false, false);
            Ui.Label(box, Loc.T("ui.menu.title"), 0, 40, 1100, 60, 44, UiStyle.Text, TextAlignmentOptions.Center, true);
            (string title, string hint, Action action)[] tiles =
            {
                ("ui.menu.guide", "ui.menu.guide.hint", () => Show(Screen.Guide)),
                ("ui.menu.analytics", "ui.menu.analytics.hint", () => Show(Screen.Results)),
                ("ui.menu.info", "ui.menu.info.hint", () => Show(TrainingSession.Current != null && TrainingSession.Current.PracticeId != null ? Screen.Brief : Screen.Home)),
                ("ui.menu.settings", "ui.menu.settings.hint", () => SettingsWindow.Open(transform)),
            };
            for (int i = 0; i < tiles.Length; i++)
            {
                Action action = tiles[i].action;
                float tx = 44 + (i % 2) * 516f, ty = 140 + (i / 2) * 228f;
                var tile = Ui.Button(box, tiles[i].title, tx, ty, 496, 208, ButtonKind.Tile, () => action());
                Ui.Label(tile.transform, Loc.T(tiles[i].title), 24, 96, 450, 50, 36, UiStyle.Text, TextAlignmentOptions.Left, true);
                Ui.Label(tile.transform, Loc.T(tiles[i].hint), 24, 146, 450, 50, 20, UiStyle.TextDim);
                Ui.Box(tile.transform, 24, 26, 52, 52, 12, UiStyle.AccentTop, UiStyle.AccentBottom);
                Ui.Label(tile.transform, (i + 1).ToString(), 24, 26, 52, 52, 26, Color.white, TextAlignmentOptions.Center, true);
            }

            Ui.Button(box, Loc.T("ui.back"), 400, 620, 300, 64, ButtonKind.Secondary, () => Show(Back()), 24);
        }

        private Screen Back()
        {
            if (TrainingSession.Current == null)
            {
                return Screen.Login;
            }

            return _back == Screen.Menu || _back == Screen.Guide || _back == Screen.Results ? Screen.Home : _back;
        }

        // ------------------------------------------------------------------ guide: the stages one by one

        private void BuildGuide()
        {
            RectTransform box = Ui.Glass(_content, 200, 150, 1520, 880, 36, false, false);
            Ui.Label(box, Loc.T("ui.menu.title") + "  >  " + Loc.T("ui.menu.guide").ToUpperInvariant(), 44, 30, 1400, 50, 34, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(box, Loc.T("ui.guide.title"), 44, 84, 1400, 44, 28, UiStyle.TextDim);
            StageList(box, 44, 150, 1430, TrainingSession.Current);
            Ui.Button(box, Loc.T("ui.back"), 610, 796, 300, 60, ButtonKind.Secondary, () => Show(Back()), 24);
        }

        // ------------------------------------------------------------------ results (the instructor sees everyone's)

        private void BuildResults()
        {
            TrainingSession session = TrainingSession.Current;
            bool all = session != null && session.User.role != UserRole.Learner;
            List<AttemptResult> list = ResultsStore.All.Where(r => all || (session != null && r.login == session.User.login)).Reverse().Take(9).ToList();

            RectTransform box = Ui.Glass(_content, 120, 150, 1680, 880, 36, false, false);
            Ui.Label(box, Loc.T("ui.menu.title") + "  >  " + Loc.T("ui.menu.analytics").ToUpperInvariant(), 44, 30, 1500, 50, 34, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(box, Loc.T(all ? "ui.results.all" : "ui.results.mine"), 44, 84, 1500, 40, 24, UiStyle.TextDim);

            float[] columns = { 44, 300, 640, 830, 1010, 1180, 1340, 1480 };
            string[] heads = { "ui.results.date", "ui.results.user", "score.Safety", "score.Technique", "score.Quality", "score.Time", "ui.results.total", "ui.results.verdict" };
            for (int i = 0; i < heads.Length; i++)
            {
                Ui.Label(box, Loc.T(heads[i]), columns[i], 146, 240, 30, 19, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            }

            if (list.Count == 0)
            {
                Ui.Label(box, Loc.T("ui.results.none"), 44, 330, 1590, 60, 26, UiStyle.TextFaint, TextAlignmentOptions.Center);
            }

            for (int r = 0; r < list.Count; r++)
            {
                AttemptResult a = list[r];
                float y = 190 + r * 62f;
                Ui.Box(box, 30, y, 1620, 54, 12, new Color32(255, 255, 255, (byte)(r % 2 == 0 ? 16 : 6)), new Color32(255, 255, 255, (byte)(r % 2 == 0 ? 16 : 6)));
                string[] cells =
                {
                    a.date, a.name, a.safety.ToString("0"), a.technique.ToString("0"), a.quality.ToString("0"), a.time.ToString("0"), a.total.ToString("0.0") + "%",
                    Loc.T(a.passed ? "ui.results.passed" : "ui.results.failed"),
                };
                for (int i = 0; i < cells.Length; i++)
                {
                    Color color = i == 7 ? (a.passed ? UiStyle.Good : UiStyle.Bad) : UiStyle.Text;
                    Ui.Label(box, cells[i], columns[i], y, i == 1 ? 330 : 240, 54, 21, color, TextAlignmentOptions.Left, i >= 6);
                }
            }

            Ui.Button(box, Loc.T("ui.back"), 690, 796, 300, 60, ButtonKind.Secondary, () => Show(Back()), 24);
        }
    }
}
