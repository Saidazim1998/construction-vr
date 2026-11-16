using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ConstructionVR.Training
{
    /// <summary>Languages of the on-screen text (Sozlamalar → Til: UZB, RUS, ЎЗБ, ENG).</summary>
    public enum Language { Uzbek = 0, Russian = 1, UzbekCyrillic = 2, English = 3 }

    /// <summary>
    /// On-screen text. The strings live in Resources/training_strings.txt (key, Uzbek, Russian, English), so
    /// they can be corrected without touching code. Uzbek in Cyrillic letters is made from the Latin text.
    /// A key that is not in the table is shown as it is, which makes a missing string easy to spot.
    /// </summary>
    public static class Loc
    {
        private static Dictionary<string, string[]> s_table;
        private static Language s_language = Language.Uzbek;

        public static event Action Changed;

        public static Language Language
        {
            get => s_language;
            set
            {
                if (s_language != value)
                {
                    s_language = value;
                    Changed?.Invoke();
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_table = null;
            Changed = null;
        }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            Load();
            if (!s_table.TryGetValue(key, out string[] row))
            {
                return key;
            }

            switch (s_language)
            {
                case Language.Russian: return Pick(row, 1);
                case Language.English: return Pick(row, 2);
                case Language.UzbekCyrillic: return ToCyrillic(row[0]);
                default: return row[0];
            }
        }

        public static string T(string key, params object[] args)
        {
            string text = T(key);
            if (args == null || args.Length == 0)
            {
                return text;
            }

            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                return text;
            }
        }

        public static bool Has(string key)
        {
            Load();
            return key != null && s_table.ContainsKey(key);
        }

        private static string Pick(string[] row, int column)
        {
            return column < row.Length && !string.IsNullOrEmpty(row[column]) ? row[column] : row[0];
        }

        private static void Load()
        {
            if (s_table != null)
            {
                return;
            }

            s_table = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var asset = Resources.Load<TextAsset>("training_strings");
            if (asset == null)
            {
                Debug.LogError("[Training] Resources/training_strings.txt not found: text keys are shown instead of text.");
                return;
            }

            foreach (string rawLine in asset.text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] cells = line.Split('\t');
                if (cells.Length < 2)
                {
                    continue;
                }

                var row = new string[cells.Length - 1];
                for (int i = 1; i < cells.Length; i++)
                {
                    row[i - 1] = cells[i].Replace("\\n", "\n");
                }

                s_table[cells[0]] = row;
            }
        }

        // ------------------------------------------------------- Uzbek Latin -> Cyrillic

        private static readonly (string latin, string cyrillic)[] Pairs =
        {
            ("o'", "ў"), ("g'", "ғ"), ("sh", "ш"), ("ch", "ч"), ("ng", "нг"), ("yo", "ё"), ("yu", "ю"), ("ya", "я"), ("ye", "е"), ("ts", "ц"),
            ("a", "а"), ("b", "б"), ("d", "д"), ("e", "е"), ("f", "ф"), ("g", "г"), ("h", "ҳ"), ("i", "и"), ("j", "ж"), ("k", "к"),
            ("l", "л"), ("m", "м"), ("n", "н"), ("o", "о"), ("p", "п"), ("q", "қ"), ("r", "р"), ("s", "с"), ("t", "т"), ("u", "у"),
            ("v", "в"), ("x", "х"), ("y", "й"), ("z", "з"), ("w", "в"), ("c", "с"),
        };

        /// <summary>Uzbek Latin text in Cyrillic letters. Text inside {braces} and &lt;tags&gt; is left alone.</summary>
        public static string ToCyrillic(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            int i = 0;
            bool wordStart = true;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{' || c == '<')
                {
                    int end = text.IndexOf(c == '{' ? '}' : '>', i);
                    if (end > i)
                    {
                        sb.Append(text, i, end - i + 1);
                        i = end + 1;
                        continue;
                    }
                }

                if (!char.IsLetter(c) && c != '\'')
                {
                    sb.Append(c);
                    wordStart = true;
                    i++;
                    continue;
                }

                bool matched = false;
                foreach ((string latin, string cyrillic) in Pairs)
                {
                    if (i + latin.Length > text.Length || string.Compare(text, i, latin, 0, latin.Length, StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        continue;
                    }

                    string output = cyrillic;
                    if (latin == "e" && wordStart)
                    {
                        output = "э";                                   // "elektr" -> "электр"
                    }

                    bool upper = char.IsUpper(c);
                    bool allUpper = upper && (latin.Length == 1 ? i + 1 < text.Length && char.IsUpper(text[i + 1]) : char.IsUpper(text[i + 1]));
                    if (allUpper)
                    {
                        output = output.ToUpperInvariant();
                    }
                    else if (upper)
                    {
                        output = char.ToUpperInvariant(output[0]) + output.Substring(1);
                    }

                    sb.Append(output);
                    i += latin.Length;
                    matched = true;
                    break;
                }

                if (!matched)
                {
                    if (c != '\'')
                    {
                        sb.Append(c);                                   // a letter of another alphabet
                    }
                    else
                    {
                        sb.Append('ъ');                                 // a'lo -> аъло
                    }

                    i++;
                }

                wordStart = false;
            }

            return sb.ToString();
        }
    }
}
