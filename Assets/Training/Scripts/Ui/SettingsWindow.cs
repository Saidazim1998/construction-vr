using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConstructionVR.Training.UI
{
    /// <summary>
    /// "Sozlamalar" of the design (drawings UI-03 .. UI-03F): seven groups on the left, the controls of the chosen
    /// group on the right, "Standart holatga qaytarish" and "Saqlash" along the bottom. Changes are made on a
    /// working copy and take effect with "Saqlash".
    /// </summary>
    public class SettingsWindow : MonoBehaviour
    {
        public enum Group { Language, Sound, Graphics, Comfort, Controller, Account, About }

        public const float Width = 1680f;
        public const float Height = 920f;

        public static SettingsWindow Instance { get; private set; }

        public Group Current { get; private set; } = Group.Comfort;
        public AppSettings Draft { get; private set; }

        private RectTransform _content;
        private bool _focusRightEye;

        /// <summary>Opens the window just in front of another panel (or in front of the player when there is none).</summary>
        public static SettingsWindow Open(Transform anchor)
        {
            if (Instance != null)
            {
                return Instance;
            }

            UiCanvas canvas = UiCanvas.Create("Settings Window", new Vector2(Width, Height));
            var rig = FindAnyObjectByType<OVRCameraRig>();
            Vector3 eyes = rig != null ? rig.centerEyeAnchor.position : Vector3.up * 1.6f;
            if (anchor != null)
            {
                Vector3 towards = (eyes - anchor.position);
                towards.y = 0f;
                canvas.transform.SetPositionAndRotation(anchor.position + towards.normalized * 0.12f, anchor.rotation);
            }
            else
            {
                Vector3 forward = rig != null ? rig.centerEyeAnchor.forward : Vector3.forward;
                forward.y = 0f;
                canvas.Face(eyes + forward.normalized * 1.9f + Vector3.down * 0.1f, eyes);
            }

            var window = canvas.gameObject.AddComponent<SettingsWindow>();
            window._content = Ui.Fill("Content", canvas.Root);
            window.Draft = AppSettings.Current.Clone();
            Instance = window;
            window.Rebuild();
            return window;
        }

        public void Close()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Destroy(gameObject);
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

        public void Select(Group group)
        {
            Current = group;
            Rebuild();
        }

        public void Save()
        {
            AppSettings.Commit(Draft);
            Draft = AppSettings.Current.Clone();
            Rebuild();
        }

        public void ResetToDefaults()
        {
            Draft = new AppSettings();
            Rebuild();
        }

        private void Rebuild()
        {
            if (_content == null)
            {
                return;
            }

            Ui.Clear(_content);
            Ui.Glass(_content, 0, 0, Width, Height, 36);
            Ui.Box(_content, 40, 34, 40, 40, 20, UiStyle.AccentTop, UiStyle.AccentBottom);
            Ui.Label(_content, "TY", 40, 34, 40, 40, 17, Color.white, TextAlignmentOptions.Center, true);
            Ui.Label(_content, Loc.T("ui.brand.owner"), 94, 28, 600, 26, 18, UiStyle.Text, TextAlignmentOptions.Left, true);
            Ui.Label(_content, Loc.T("ui.brand.product"), 94, 52, 600, 30, 24, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            Ui.Label(_content, Loc.T("ui.settings.crumb"), Width - 760, 40, 640, 30, 17, UiStyle.TextDim, TextAlignmentOptions.Right, true);
            Ui.Button(_content, "X", Width - 100, 28, 60, 60, ButtonKind.Secondary, Close, 24);

            // groups
            RectTransform side = Ui.Card(_content, 40, 110, 420, 790, 22);
            Ui.Label(side, Loc.T("ui.settings.groups"), 30, 22, 360, 26, 16, UiStyle.TextDim, TextAlignmentOptions.Left, true);
            Array groups = Enum.GetValues(typeof(Group));
            for (int i = 0; i < groups.Length; i++)
            {
                var group = (Group)groups.GetValue(i);
                bool on = group == Current;
                Button row = Ui.Button(side, string.Empty, 10, 62 + i * 70f, 400, 64, on ? ButtonKind.Primary : ButtonKind.Ghost, () => Select(group));
                if (!on)
                {
                    row.image.color = new Color(1f, 1f, 1f, 0.02f);
                }

                Ui.Box(row.transform, 22, 21, 22, 22, 11, on ? Color.white : (Color)UiStyle.Track, on ? Color.white : (Color)UiStyle.Track);
                Ui.Border(row.transform.GetChild(row.transform.childCount - 1), 11, on ? Color.white : (Color)UiStyle.TextFaint, 2f);
                Ui.Label(row.transform, Loc.T("ui.settings.group." + group), 62, 0, 300, 64, 22, on ? UiStyle.Text : UiStyle.TextDim, TextAlignmentOptions.Left, on);
                if (on)
                {
                    Ui.Label(row.transform, ">", 350, 0, 30, 64, 22, UiStyle.Text, TextAlignmentOptions.Center, true);
                }
            }

            // details
            RectTransform detail = Ui.Glass(_content, 480, 110, 1160, 680, 22, true, false);
            Ui.Label(detail, Loc.T("ui.settings.detail"), 32, 24, 600, 22, 14, UiStyle.TextDim, TextAlignmentOptions.Left, true);
            Ui.Label(detail, Loc.T("ui.settings.group." + Current).ToUpperInvariant(), 32, 48, 900, 40, 28, UiStyle.Text, TextAlignmentOptions.Left, true);
            switch (Current)
            {
                case Group.Language: BuildLanguage(detail); break;
                case Group.Sound: BuildSound(detail); break;
                case Group.Graphics: BuildGraphics(detail); break;
                case Group.Comfort: BuildComfort(detail); break;
                case Group.Controller: BuildController(detail); break;
                case Group.Account: BuildAccount(detail); break;
                case Group.About: BuildAbout(detail); break;
            }

            Ui.Button(_content, Loc.T("ui.settings.reset"), 480, 812, 360, 64, ButtonKind.Secondary, ResetToDefaults, 21);
            Ui.Button(_content, Loc.T("ui.settings.save"), Width - 40 - 260, 812, 260, 64, ButtonKind.Primary, Save, 24);
        }

        private static float Row(int index) => 118f + index * 78f;

        private static void RowLabel(Transform parent, int row, string key, string hintKey = null)
        {
            Ui.Label(parent, Loc.T(key), 32, Row(row), 520, hintKey == null ? 56 : 32, 22, UiStyle.Text, TextAlignmentOptions.Left, true);
            if (hintKey != null)
            {
                Ui.Label(parent, Loc.T(hintKey), 32, Row(row) + 30, 520, 24, 15, UiStyle.TextFaint, TextAlignmentOptions.Left);
            }
        }

        private static TextMeshProUGUI SliderRow(Transform parent, int row, string key, float min, float max, float value, Func<float, string> show, Action<float> set,
            string hintKey = null, bool whole = false)
        {
            RowLabel(parent, row, key, hintKey);
            TextMeshProUGUI text = Ui.Label(parent, show(value), 1010, Row(row), 120, 56, 22, UiStyle.Text, TextAlignmentOptions.Right);
            Ui.Slider(parent, 560, Row(row), 430, min, max, value, v =>
            {
                set(v);
                text.text = show(v);
            }, whole);
            return text;
        }

        // ------------------------------------------------------------------ groups

        private void BuildLanguage(Transform p)
        {
            Language[] textOrder = { Language.Uzbek, Language.UzbekCyrillic, Language.Russian, Language.English };
            RowLabel(p, 0, "ui.settings.lang.text");
            Ui.Segmented(p, 490, Row(0), 640, 56, new[] { "O'zbekcha", "Ўзбекча", "Русский", "English" }, Array.IndexOf(textOrder, Draft.textLanguage),
                i => Draft.textLanguage = textOrder[i]);
            Language[] voiceOrder = { Language.Uzbek, Language.Russian, Language.English };
            RowLabel(p, 1, "ui.settings.lang.voice");
            Ui.Segmented(p, 650, Row(1), 480, 56, new[] { "O'zbekcha", "Русский", "English" }, Mathf.Max(0, Array.IndexOf(voiceOrder, Draft.voiceLanguage)),
                i => Draft.voiceLanguage = voiceOrder[i]);
            Ui.Label(p, Loc.T("ui.settings.lang.note"), 32, Row(2) + 10, 1090, 60, 17, UiStyle.TextFaint);
        }

        private void BuildSound(Transform p)
        {
            SliderRow(p, 0, "ui.settings.sound.master", 0f, 1f, Draft.masterVolume, Percent, v => Draft.masterVolume = v);
            RowLabel(p, 1, "ui.settings.sound.voice");
            Ui.Segmented(p, 890, Row(1), 240, 56, new[] { Loc.T("ui.settings.sound.male"), Loc.T("ui.settings.sound.female") }, Draft.assistantFemale ? 1 : 0,
                i => Draft.assistantFemale = i == 1);
            SliderRow(p, 2, "ui.settings.sound.assistant", 0f, 1f, Draft.assistantVolume, Percent, v => Draft.assistantVolume = v);
            SliderRow(p, 3, "ui.settings.sound.effects", 0f, 1f, Draft.effectsVolume, Percent, v => Draft.effectsVolume = v);
        }

        private void BuildGraphics(Transform p)
        {
            RowLabel(p, 0, "ui.settings.gfx.quality", "ui.settings.gfx.quality.hint");
            Ui.Segmented(p, 890, Row(0), 240, 56, new[] { "HD", "4K" }, Draft.highResolution ? 1 : 0, i => Draft.highResolution = i == 1);
            SliderRow(p, 1, "ui.settings.gfx.brightness", 0.5f, 1f, Draft.brightness, Percent, v => Draft.brightness = v);
            RowLabel(p, 2, "ui.settings.gfx.hdr");
            Ui.Check(p, 1066, Row(2) - 4, 64, Draft.hdr, v => Draft.hdr = v);
        }

        private void BuildComfort(Transform p)
        {
            RowLabel(p, 0, "ui.settings.vr.move");
            Ui.Segmented(p, 790, Row(0), 340, 56, new[] { Loc.T("ui.settings.vr.teleport"), Loc.T("ui.settings.vr.smooth") }, (int)Draft.moveMode,
                i => Draft.moveMode = (MoveMode)i);
            RowLabel(p, 1, "ui.settings.vr.turn");
            Ui.Segmented(p, 790, Row(1), 340, 56, new[] { "SNAP", Loc.T("ui.settings.vr.smooth") }, (int)Draft.turnMode, i =>
            {
                Draft.turnMode = (TurnMode)i;
                Rebuild();
            });
            TextMeshProUGUI step = SliderRow(p, 2, "ui.settings.vr.step", 15f, 90f, Draft.turnStep, v => v.ToString("0") + "°", v => Draft.turnStep = Mathf.Round(v / 15f) * 15f,
                "ui.settings.vr.step.hint");
            if (Draft.turnMode != TurnMode.Snap)
            {
                step.color = UiStyle.TextFaint;
            }

            SliderRow(p, 3, "ui.settings.vr.vignette", 0f, 1f, Draft.vignette, Percent, v => Draft.vignette = v);
            RowLabel(p, 4, "ui.settings.vr.hand");
            Ui.Segmented(p, 890, Row(4), 240, 56, new[] { Loc.T("ui.settings.vr.left"), Loc.T("ui.settings.vr.right") }, (int)Draft.dominantHand,
                i => Draft.dominantHand = (Hand)i);
            RowLabel(p, 5, "ui.settings.vr.distance");
            Ui.Stepper(p, 910, Row(5), () => Draft.controllerDistance.ToString("0.0"), d => Draft.controllerDistance = Mathf.Clamp(Draft.controllerDistance + d * 0.5f, 0f, 10f));
            RowLabel(p, 6, "ui.settings.vr.focus", "ui.settings.vr.focus.hint");
            Ui.Segmented(p, 640, Row(6), 240, 56, new[] { Loc.T("ui.settings.vr.left"), Loc.T("ui.settings.vr.right") }, _focusRightEye ? 1 : 0, i =>
            {
                _focusRightEye = i == 1;
                Rebuild();
            });
            Ui.Stepper(p, 910, Row(6), () => (_focusRightEye ? Draft.eyeFocusRight : Draft.eyeFocusLeft).ToString("0.0"), d =>
            {
                if (_focusRightEye) Draft.eyeFocusRight = Mathf.Clamp(Draft.eyeFocusRight + d * 0.5f, -5f, 5f);
                else Draft.eyeFocusLeft = Mathf.Clamp(Draft.eyeFocusLeft + d * 0.5f, -5f, 5f);
            });
        }

        private void BuildController(Transform p)
        {
            Ui.Label(p, Loc.T("ui.settings.ctl.left"), 380, 104, 360, 30, 19, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            Ui.Label(p, Loc.T("ui.settings.ctl.right"), 760, 104, 360, 30, 19, UiStyle.AccentText, TextAlignmentOptions.Left, true);
            for (int i = 1; i <= 6; i++)
            {
                float y = 100 + i * 74f;
                Ui.Box(p, 24, y, 1112, 66, 14, new Color32(255, 255, 255, (byte)(i % 2 == 0 ? 6 : 16)), new Color32(255, 255, 255, (byte)(i % 2 == 0 ? 6 : 16)));
                Ui.StepBadge(p, 36, y + 9, 48, i.ToString(), false, true);
                Ui.Label(p, Loc.T("ui.settings.ctl." + i), 100, y, 270, 66, 20, UiStyle.Text, TextAlignmentOptions.Left, true);
                Ui.Label(p, Loc.T("ui.settings.ctl." + i + ".left"), 380, y, 370, 66, 19, UiStyle.TextDim, TextAlignmentOptions.Left);
                Ui.Label(p, Loc.T("ui.settings.ctl." + i + ".right"), 760, y, 370, 66, 19, UiStyle.TextDim, TextAlignmentOptions.Left);
            }
        }

        private void BuildAccount(Transform p)
        {
            TrainingSession session = TrainingSession.Current;
            if (session == null)
            {
                Ui.Label(p, Loc.T("ui.settings.account.none"), 32, 130, 1090, 60, 24, UiStyle.TextDim);
                return;
            }

            (string, string)[] rows =
            {
                ("ui.settings.account.name", session.User.name), ("ui.login.user", session.User.login), ("ui.settings.account.role", Loc.T("role." + session.User.role)),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                RowLabel(p, i, rows[i].Item1);
                Ui.Label(p, rows[i].Item2, 560, Row(i), 570, 56, 24, UiStyle.TextDim, TextAlignmentOptions.Right);
            }
        }

        private void BuildAbout(Transform p)
        {
            (string, string)[] rows =
            {
                ("ui.settings.about.app", Loc.T("ui.brand.product") + " — " + Loc.T("practice.mag.short")), ("ui.settings.about.version", Application.version),
                ("ui.settings.about.engine", "Unity " + Application.unityVersion), ("ui.settings.about.device", SystemInfo.deviceModel),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                RowLabel(p, i, rows[i].Item1);
                Ui.Label(p, rows[i].Item2, 480, Row(i), 650, 56, 22, UiStyle.TextDim, TextAlignmentOptions.Right);
            }
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";
    }
}
