using System;
using UnityEngine;

namespace ConstructionVR.Training
{
    public enum MoveMode { Teleport = 0, Smooth = 1 }
    public enum TurnMode { Snap = 0, Smooth = 1 }
    public enum Hand { Left = 0, Right = 1 }

    /// <summary>
    /// What the "Sozlamalar" window edits (groups Til, Ovoz, Grafika, VR-qulaylik of the design). The values are
    /// kept on the device and applied through <see cref="Changed"/>: the window changes a working copy and
    /// "Saqlash" commits it.
    /// </summary>
    [Serializable]
    public class AppSettings
    {
        // Til
        public Language textLanguage = Language.Uzbek;
        public Language voiceLanguage = Language.Uzbek;

        // Ovoz (0..1)
        public float masterVolume = 0.8f;
        public float assistantVolume = 0.7f;
        public float effectsVolume = 0.65f;
        public bool assistantFemale = false;

        // Grafika
        public bool highResolution = false;         // "4K": a larger eye texture, costs GPU time on the headset
        public float brightness = 0.8f;             // 0.5 .. 1
        public bool hdr = true;

        // VR-qulaylik
        public MoveMode moveMode = MoveMode.Smooth;
        public TurnMode turnMode = TurnMode.Snap;
        public float turnStep = 45f;                // degrees, 15 .. 90 (snap turning)
        public float vignette = 0.6f;               // 0 .. 1
        public Hand dominantHand = Hand.Right;
        public float controllerDistance = 3.0f;
        public float eyeFocusLeft = 0f;
        public float eyeFocusRight = 0f;

        private const string Key = "ConstructionVR.Settings";
        private static AppSettings s_current;

        public static event Action Changed;

        public static AppSettings Current
        {
            get
            {
                if (s_current == null)
                {
                    s_current = new AppSettings();
                    string json = PlayerPrefs.GetString(Key, string.Empty);
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            JsonUtility.FromJsonOverwrite(json, s_current);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[Training] saved settings could not be read: " + e.Message);
                        }
                    }

                    Loc.Language = s_current.textLanguage;
                }

                return s_current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_current = null;
            Changed = null;
        }

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        /// <summary>Makes these settings the current ones, keeps them on the device and tells everyone.</summary>
        public static void Commit(AppSettings settings)
        {
            s_current = settings.Clone();
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(s_current));
            PlayerPrefs.Save();
            Loc.Language = s_current.textLanguage;
            AudioListener.volume = s_current.masterVolume;
            Changed?.Invoke();
        }
    }
}
