using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ConstructionVR.Training.UI
{
    public enum ButtonKind { Primary, Secondary, Ghost, Tile, Key }

    /// <summary>
    /// Builds the interface elements of the design in code. Positions are given like in the design file:
    /// x and y from the top-left corner of the parent, in design pixels.
    /// </summary>
    public static class Ui
    {
        // ------------------------------------------------------------------ layout

        public static RectTransform Node(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, x, y, w, h);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static RectTransform Fill(string name, Transform parent, float inset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        // ------------------------------------------------------------------ surfaces

        /// <summary>
        /// Adds an image that is drawn with the interface's own material. Every image must be made here:
        /// the default UI material shows up in one eye only (see <see cref="UiStyle.ImageMaterial"/>).
        /// </summary>
        public static Image NewImage(GameObject go)
        {
            var image = go.AddComponent<Image>();
            image.material = UiStyle.ImageMaterial;
            return image;
        }

        /// <summary>A rounded box, optionally shaded from top to bottom.</summary>
        public static Image Box(Transform parent, float x, float y, float w, float h, int radius, Color top, Color bottom, bool raycast = false)
        {
            RectTransform rt = Node("Box", parent, x, y, w, h);
            return Shade(rt, radius, top, bottom, raycast);
        }

        private static Image Shade(RectTransform rt, int radius, Color top, Color bottom, bool raycast)
        {
            var image = NewImage(rt.gameObject);
            image.sprite = UiStyle.Rounded(radius);
            image.type = Image.Type.Sliced;
            image.raycastTarget = raycast;
            if (top == bottom)
            {
                image.color = top;
            }
            else
            {
                rt.gameObject.AddComponent<UiGradient>().Set(top, bottom);
            }

            return image;
        }

        public static Image Border(Transform parent, int radius, Color color, float thickness = 2f)
        {
            RectTransform rt = Fill("Outline", parent);
            var image = NewImage(rt.gameObject);
            image.sprite = UiStyle.Ring(radius, thickness);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A glass panel of the design: dark fill, blue glow along the bottom, light outline.</summary>
        public static RectTransform Glass(Transform parent, float x, float y, float w, float h, int radius = 28, bool glow = true, bool raycast = true)
        {
            Image fill = Box(parent, x, y, w, h, radius, UiStyle.PanelTop, UiStyle.PanelBottom, raycast);
            fill.name = "Glass";
            var rt = (RectTransform)fill.transform;
            if (glow)
            {
                RectTransform g = Fill("Glow", rt);
                g.anchorMin = new Vector2(0f, 0f);
                g.anchorMax = new Vector2(1f, 0.45f);
                g.offsetMin = g.offsetMax = Vector2.zero;
                Color clear = UiStyle.PanelGlow;
                clear.a = 0f;
                Shade(g, radius, clear, UiStyle.PanelGlow, false);
            }

            Border(rt, radius, UiStyle.Outline);
            return rt;
        }

        /// <summary>A lighter panel inside a glass panel (cards, list rows, input fields).</summary>
        public static RectTransform Card(Transform parent, float x, float y, float w, float h, int radius = 18, bool outline = true)
        {
            Image fill = Box(parent, x, y, w, h, radius, UiStyle.CardTop, UiStyle.CardBottom);
            fill.name = "Card";
            if (outline)
            {
                Border(fill.transform, radius, UiStyle.Outline, 1.5f);
            }

            return (RectTransform)fill.transform;
        }

        // ------------------------------------------------------------------ text

        public static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool bold = false)
        {
            RectTransform rt = Node("Text", parent, x, y, w, h);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = true;
            return label;
        }

        // ------------------------------------------------------------------ buttons

        public static Button Button(Transform parent, string label, float x, float y, float w, float h, ButtonKind kind, UnityAction onClick, float fontSize = 24f)
        {
            RectTransform rt = Node("Button " + label, parent, x, y, w, h);
            int radius = kind == ButtonKind.Tile ? 22 : kind == ButtonKind.Ghost ? 16 : kind == ButtonKind.Key ? 14 : 32;
            Image image;
            Color textColor = UiStyle.Text;
            switch (kind)
            {
                case ButtonKind.Primary:
                    image = Shade(rt, radius, UiStyle.AccentTop, UiStyle.AccentBottom, true);
                    Border(rt, radius, UiStyle.OutlineBright, 2f);
                    break;
                case ButtonKind.Secondary:
                    image = Shade(rt, radius, new Color32(18, 44, 92, 190), new Color32(10, 28, 64, 200), true);
                    Border(rt, radius, UiStyle.OutlineBright, 1.5f);
                    textColor = UiStyle.AccentText;
                    break;
                case ButtonKind.Tile:
                    image = Shade(rt, radius, new Color32(120, 150, 190, 90), new Color32(30, 110, 235, 200), true);
                    Border(rt, radius, UiStyle.Outline, 1.5f);
                    break;
                case ButtonKind.Key:
                    image = Shade(rt, radius, new Color32(34, 66, 122, 235), new Color32(18, 42, 90, 240), true);
                    Border(rt, radius, UiStyle.Outline, 1f);
                    break;
                default:
                    image = Shade(rt, radius, new Color32(255, 255, 255, 12), new Color32(255, 255, 255, 12), true);
                    textColor = UiStyle.TextDim;
                    break;
            }

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.pressedColor = new Color(0.62f, 0.72f, 0.9f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            if (kind != ButtonKind.Tile && !string.IsNullOrEmpty(label))
            {
                TextMeshProUGUI text = Label(rt, label, 0, 0, w, h, fontSize, textColor, TextAlignmentOptions.Center, kind == ButtonKind.Primary);
                Stretch(text.rectTransform, 12f);
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }

            return button;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, 0f);
            rt.offsetMax = new Vector2(-inset, 0f);
        }

        // ------------------------------------------------------------------ segmented choice

        public sealed class SegmentedChoice
        {
            public int Index { get; private set; }
            internal readonly List<(Image fill, GameObject outline, TextMeshProUGUI text)> Segments = new List<(Image, GameObject, TextMeshProUGUI)>();
            internal Action<int> Changed;

            public void Set(int index, bool notify = false)
            {
                Index = index;
                for (int i = 0; i < Segments.Count; i++)
                {
                    bool on = i == index;
                    Segments[i].fill.color = on ? new Color32(30, 96, 226, 230) : new Color32(0, 0, 0, 0);
                    Segments[i].outline.SetActive(on);
                    Segments[i].text.color = on ? UiStyle.Text : UiStyle.TextFaint;
                    Segments[i].text.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                }

                if (notify)
                {
                    Changed?.Invoke(index);
                }
            }
        }

        /// <summary>A row of options of which one is chosen (Teleport / Silliq, UZB / RUS / ...).</summary>
        public static SegmentedChoice Segmented(Transform parent, float x, float y, float w, float h, string[] labels, int selected, Action<int> onChange, float fontSize = 19f)
        {
            Image track = Box(parent, x, y, w, h, 28, UiStyle.Track, UiStyle.Track);
            track.name = "Segmented";
            Border(track.transform, 28, UiStyle.Outline, 1f);
            var choice = new SegmentedChoice { Changed = onChange };
            float segment = (w - 8f) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                RectTransform rt = Node("Segment " + labels[i], track.transform, 4f + segment * i, 4f, segment, h - 8f);
                var fill = NewImage(rt.gameObject);
                fill.sprite = UiStyle.Rounded(24);
                fill.type = Image.Type.Sliced;
                Image outline = Border(rt, 24, UiStyle.OutlineBright, 2f);
                TextMeshProUGUI text = Label(rt, labels[i], 0, 0, segment, h - 8f, fontSize, UiStyle.Text, TextAlignmentOptions.Center);
                text.textWrappingMode = TextWrappingModes.NoWrap;
                var button = rt.gameObject.AddComponent<Button>();
                button.targetGraphic = fill;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => choice.Set(index, true));
                choice.Segments.Add((fill, outline.gameObject, text));
            }

            choice.Set(Mathf.Clamp(selected, 0, labels.Length - 1));
            return choice;
        }

        // ------------------------------------------------------------------ slider, stepper, check box

        /// <summary>A slider with a glowing thumb; the area that can be grabbed is 56 px high whatever the track looks like.</summary>
        public static Slider Slider(Transform parent, float x, float y, float w, float min, float max, float value, Action<float> onChange, bool whole = false)
        {
            RectTransform root = Node("Slider", parent, x, y, w, UiStyle.HitMin);
            var hit = NewImage(root.gameObject);
            hit.color = new Color(0f, 0f, 0f, 0f);

            Image track = Box(root, 0f, UiStyle.HitMin / 2f - 4f, w, 8f, 4, new Color32(70, 96, 140, 200), new Color32(70, 96, 140, 200));
            track.name = "Track";
            RectTransform fillArea = Node("Fill Area", root, 0f, UiStyle.HitMin / 2f - 4f, w, 8f);
            RectTransform fill = Fill("Fill", fillArea);
            fill.anchorMax = new Vector2(0f, 1f);
            Shade(fill, 4, UiStyle.AccentTop, UiStyle.Accent, false);

            RectTransform handleArea = Node("Handle Slide Area", root, 16f, 0f, w - 32f, UiStyle.HitMin);
            RectTransform handle = Node("Handle", handleArea, 0f, 0f, 32f, 32f);
            handle.anchorMin = new Vector2(0f, 0.5f);
            handle.anchorMax = new Vector2(0f, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(32f, 32f);
            RectTransform glow = Fill("Glow", handle, -18f);
            var glowImage = NewImage(glow.gameObject);
            glowImage.sprite = UiStyle.Glow;
            glowImage.color = new Color32(60, 150, 255, 200);
            glowImage.raycastTarget = false;
            RectTransform knob = Fill("Knob", handle);
            Image knobImage = Shade(knob, 16, new Color32(214, 234, 255, 255), UiStyle.Accent, false);
            Border(knob, 16, Color.white, 2f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.targetGraphic = knobImage;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.SetValueWithoutNotify(value);
            if (onChange != null)
            {
                slider.onValueChanged.AddListener(v => onChange(v));
            }

            return slider;
        }

        /// <summary>Minus, value, plus.</summary>
        public static TextMeshProUGUI Stepper(Transform parent, float x, float y, Func<string> value, Action<int> onStep)
        {
            TextMeshProUGUI text = null;
            Button(parent, "−", x, y, 56f, 56f, ButtonKind.Secondary, () => { onStep(-1); text.text = value(); }, 28f);
            RectTransform field = Card(parent, x + 66f, y, 88f, 56f, 14);
            text = Label(field, value(), 0, 0, 88f, 56f, 22f, UiStyle.Text, TextAlignmentOptions.Center);
            Button(parent, "+", x + 164f, y, 56f, 56f, ButtonKind.Secondary, () => { onStep(1); text.text = value(); }, 28f);
            return text;
        }

        public static Toggle Check(Transform parent, float x, float y, float size, bool value, Action<bool> onChange)
        {
            RectTransform rt = Node("Check", parent, x, y, size, size);
            Image box = Shade(rt, 14, UiStyle.Track, UiStyle.Track, true);
            Border(rt, 14, UiStyle.OutlineBright, 2f);
            RectTransform mark = Fill("Mark", rt, size * 0.2f);
            Image markImage = Shade(mark, 8, UiStyle.AccentTop, UiStyle.Accent, false);
            RectTransform tick = Tick(mark, size * 0.6f, Color.white);
            var toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = markImage;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.SetIsOnWithoutNotify(value);
            tick.gameObject.SetActive(value);
            toggle.onValueChanged.AddListener(v =>
            {
                tick.gameObject.SetActive(v);
                onChange?.Invoke(v);
            });
            return toggle;
        }

        // ------------------------------------------------------------------ small pieces of the design

        /// <summary>A step number in a ring ("01"), filled when the step is done.</summary>
        public static void StepBadge(Transform parent, float x, float y, float size, string number, bool done, bool current)
        {
            RectTransform rt = Node("Badge", parent, x, y, size, size);
            int radius = Mathf.RoundToInt(size / 2f);
            Color fill = done ? (Color)UiStyle.Good : current ? (Color)new Color32(22, 60, 130, 255) : (Color)UiStyle.Track;
            Shade(rt, radius, fill, fill, false);
            Border(rt, radius, done ? (Color)UiStyle.Good : current ? (Color)UiStyle.OutlineBright : (Color)UiStyle.Outline, 2.5f);
            if (done)
            {
                Tick(rt, size, new Color32(4, 30, 26, 255));
                return;
            }

            TextMeshProUGUI text = Label(rt, number, 0, 0, size, size, size * 0.44f, UiStyle.Text, TextAlignmentOptions.Center, true);
            Stretch(text.rectTransform);
        }

        /// <summary>A check mark drawn from two bars (the font has no such glyph).</summary>
        public static RectTransform Tick(Transform parent, float size, Color color)
        {
            RectTransform root = Fill("Tick", parent);
            float t = size * 0.11f;
            Vector2 elbow = new Vector2(-size * 0.07f, -size * 0.17f);
            foreach ((float length, float angle) in new[] { (size * 0.26f, 135f), (size * 0.5f, 50f) })
            {
                var go = new GameObject("Bar", typeof(RectTransform));
                go.layer = parent.gameObject.layer;
                var rt = (RectTransform)go.transform;
                rt.SetParent(root, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(length, t);
                rt.anchoredPosition = elbow;
                rt.localRotation = Quaternion.Euler(0f, 0f, angle);
                var image = NewImage(go);
                image.sprite = UiStyle.Rounded(2);
                image.type = Image.Type.Sliced;
                image.color = color;
                image.raycastTarget = false;
            }

            return root;
        }

        /// <summary>A small coloured tag ("Yangi", "Tugallangan").</summary>
        public static void Tag(Transform parent, float x, float y, string text, Color color)
        {
            float w = 36f + text.Length * 11f;
            Image box = Box(parent, x, y, w, 30f, 8, color, color * new Color(0.8f, 0.8f, 0.8f, 1f));
            TextMeshProUGUI label = Label(box.transform, text, 0, 0, w, 30f, 16f, new Color32(8, 24, 52, 255), TextAlignmentOptions.Center, true);
            Stretch(label.rectTransform);
        }

        /// <summary>A bar of thin segments, part of them lit (the "Umumiy ballari" meter of the profile card).</summary>
        public static void Meter(Transform parent, float x, float y, float w, float h, float value01, Color color)
        {
            const int count = 24;
            float step = w / count;
            int lit = Mathf.RoundToInt(Mathf.Clamp01(value01) * count);
            for (int i = 0; i < count; i++)
            {
                Color c = i < lit ? color : (Color)new Color32(110, 130, 165, 120);
                Box(parent, x + i * step, y, step - 3f, h, 2, c, c);
            }
        }
    }
}
