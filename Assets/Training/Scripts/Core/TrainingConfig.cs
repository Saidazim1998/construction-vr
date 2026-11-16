using System;
using System.IO;
using UnityEngine;

namespace ConstructionVR.Training
{
    [Serializable]
    public struct ValueRange
    {
        public float min;
        public float max;

        public ValueRange(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Contains(float value) => value >= min && value <= max;

        public float Middle => (min + max) * 0.5f;

        /// <summary>0 inside the range, grows with the distance outside it (in units of the range width).</summary>
        public float Outside(float value)
        {
            float width = Mathf.Max(1e-4f, max - min);
            return value < min ? (min - value) / width : value > max ? (value - max) / width : 0f;
        }

        public override string ToString() => $"{min:0.#}–{max:0.#}";
    }

    /// <summary>
    /// Every number of the scenario that the customer has to confirm: welding mode, gas, technique, tolerances,
    /// penalties, time norms. The values below are working placeholders for a T joint of 8 mm St3 plates welded
    /// with 1.2 mm Sv-08G2S wire in CO2 (the customer's questionnaire, "Savolnoma", is still open).
    /// They are read from Resources/training_config.json when that file exists, and a file of the same name in
    /// the application's persistent data folder overrides both (for tuning on the headset without a new build).
    /// </summary>
    [Serializable]
    public class TrainingConfig
    {
        [Header("Welding mode")]
        public ValueRange voltage = new ValueRange(22f, 26f);              // V
        public ValueRange current = new ValueRange(180f, 220f);            // A
        public ValueRange wireSpeed = new ValueRange(5f, 7f);              // m/min
        public ValueRange voltageKnob = new ValueRange(14f, 40f);          // what the knob can set
        public ValueRange currentKnob = new ValueRange(60f, 500f);
        public ValueRange wireSpeedKnob = new ValueRange(1.5f, 18f);

        [Header("Gas")]
        public ValueRange gasFlow = new ValueRange(12f, 15f);              // l/min
        public float cylinderPressureMin = 1.0f;                           // MPa: below this the cylinder is not used
        public float lowCylinderPressure = 0.6f;                           // MPa in the "nearly empty cylinder" variant

        [Header("Wire feeder")]
        public ValueRange rollerTension = new ValueRange(2f, 3f);          // marks on the tension knob (0..5)

        [Header("Technique while welding")]
        public ValueRange stickOutMm = new ValueRange(10f, 15f);
        public ValueRange workAngleDeg = new ValueRange(35f, 55f);         // between the torch and the base plate
        public ValueRange travelAngleDeg = new ValueRange(5f, 20f);        // lean of the torch along the seam
        public ValueRange travelSpeedMmS = new ValueRange(4f, 7f);

        [Header("Fit-up and seam")]
        public float angleToleranceDeg = 1f;                               // allowed deviation from 90 degrees
        public float gapMaxMm = 1f;
        public ValueRange legSizeMm = new ValueRange(4.5f, 6f);            // fillet leg (katet 5)
        public float seamLengthMm = 300f;
        public int tackCount = 3;

        [Header("Scoring")]
        public float weightSafety = 0.35f;
        public float weightTechnique = 0.25f;
        public float weightQuality = 0.30f;
        public float weightTime = 0.10f;
        public float passPercent = 70f;
        public float penaltyLow = 3f;
        public float penaltyMedium = 7f;
        public float penaltyHigh = 15f;
        public float penaltyCritical = 30f;
        public float normMinutes = 35f;                                    // the whole practice
        public float timePenaltyPerMinute = 4f;                            // time points lost per minute over the norm

        [Header("Faults that may be planted in a session (probability 0..1)")]
        public float chanceCableDamaged = 0.5f;
        public float chanceCylinderUnchained = 0.5f;
        public float chanceSpoolEmpty = 0.4f;
        public float chanceLowPressure = 0.3f;
        public float chanceMisaligned = 0.5f;
        public int fixedSeed = 0;                                          // not 0: the same faults every time

        private static TrainingConfig s_current;

        public static TrainingConfig Current
        {
            get
            {
                if (s_current == null)
                {
                    s_current = Load();
                }

                return s_current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_current = null;

        public float Penalty(Severity severity)
        {
            switch (severity)
            {
                case Severity.Low: return penaltyLow;
                case Severity.Medium: return penaltyMedium;
                case Severity.High: return penaltyHigh;
                case Severity.Critical: return penaltyCritical;
                default: return 0f;
            }
        }

        private static TrainingConfig Load()
        {
            var config = new TrainingConfig();
            var asset = Resources.Load<TextAsset>("training_config");
            if (asset != null)
            {
                Overwrite(config, asset.text, "Resources/training_config.json");
            }

            string file = Path.Combine(Application.persistentDataPath, "training_config.json");
            if (File.Exists(file))
            {
                Overwrite(config, File.ReadAllText(file), file);
            }

            return config;
        }

        private static void Overwrite(TrainingConfig config, string json, string source)
        {
            try
            {
                JsonUtility.FromJsonOverwrite(json, config);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Training] {source} could not be read: {e.Message}");
            }
        }
    }
}
