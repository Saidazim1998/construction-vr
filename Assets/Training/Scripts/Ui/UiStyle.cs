using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ConstructionVR.Training.UI
{
    /// <summary>
    /// The look of the design file "Temir yo'l VR" (Figma): deep navy glass panels with a thin light-blue outline
    /// and a blue glow along the bottom edge, white text, blue pill buttons. Canvases are laid out in the
    /// design's pixels (a 1920 x 1080 hub) and shown at 1 px = 1 mm.
    /// </summary>
    public static class UiStyle
    {
        public const float MetresPerPixel = 0.001f;
        public const float HitMin = 56f;                    // smallest size of anything the player aims at

        public static readonly Color PanelTop = new Color32(16, 38, 78, 252);
        public static readonly Color PanelBottom = new Color32(6, 20, 48, 252);
        public static readonly Color PanelGlow = new Color32(14, 110, 255, 150);
        public static readonly Color CardTop = new Color32(26, 56, 108, 205);
        public static readonly Color CardBottom = new Color32(12, 32, 70, 215);
        public static readonly Color Outline = new Color32(138, 182, 255, 110);
        public static readonly Color OutlineBright = new Color32(120, 180, 255, 230);
        public static readonly Color Accent = new Color32(47, 125, 255, 255);
        public static readonly Color AccentTop = new Color32(66, 150, 255, 255);
        public static readonly Color AccentBottom = new Color32(30, 92, 220, 255);
        public static readonly Color AccentText = new Color32(96, 172, 255, 255);
        public static readonly Color Track = new Color32(8, 22, 50, 235);
        public static readonly Color Field = new Color32(160, 190, 235, 46);
        public static readonly Color Text = new Color32(255, 255, 255, 255);
        public static readonly Color TextDim = new Color32(168, 190, 222, 255);
        public static readonly Color TextFaint = new Color32(120, 146, 186, 255);
        public static readonly Color Good = new Color32(46, 211, 160, 255);
        public static readonly Color Warn = new Color32(255, 200, 87, 255);
        public static readonly Color Bad = new Color32(255, 107, 94, 255);

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static Material _imageMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Sprites.Clear();
            _imageMaterial = null;
            _imageMaterialOnTop = null;
            TextOnTop.Clear();
        }

        /// <summary>
        /// The material of every image of the interface (Resources/UiImage, shader ConstructionVR/UI/Image).
        /// Unity's own UI/Default is drawn in the left eye only with single-pass stereo rendering in this
        /// Unity version, so no image may be left on the default material.
        /// </summary>
        public static Material ImageMaterial
        {
            get
            {
                if (_imageMaterial == null)
                {
                    _imageMaterial = Resources.Load<Material>("UiImage");
                }

                return _imageMaterial;
            }
        }

        public const float DepthTestNormal = 4f;        // CompareFunction.LEqual
        public const float DepthTestOnTop = 8f;         // CompareFunction.Always
        public static readonly int UiZTest = Shader.PropertyToID("_UiZTest");

        private static Material _imageMaterialOnTop;
        private static readonly Dictionary<Material, Material> TextOnTop = new Dictionary<Material, Material>();

        /// <summary>The image material of a panel drawn over the world (see <see cref="UiCanvas.OnTop"/>).</summary>
        public static Material ImageMaterialOnTop
        {
            get
            {
                if (_imageMaterialOnTop == null && ImageMaterial != null)
                {
                    _imageMaterialOnTop = new Material(ImageMaterial) { name = "UiImage (on top)", hideFlags = HideFlags.HideAndDontSave };
                    _imageMaterialOnTop.SetFloat(UiZTest, DepthTestOnTop);
                }

                return _imageMaterialOnTop;
            }
        }

        /// <summary>The font material a copy made by <see cref="TextMaterialOnTop"/> was made from (or the material itself).</summary>
        public static Material TextMaterialNormal(Material material)
        {
            foreach (KeyValuePair<Material, Material> pair in TextOnTop)
            {
                if (pair.Value == material)
                {
                    return pair.Key;
                }
            }

            return material;
        }

        /// <summary>The same text material drawn over the world (one copy per font material).</summary>
        public static Material TextMaterialOnTop(Material source)
        {
            if (source == null || !source.HasProperty(UiZTest) || source.GetFloat(UiZTest) == DepthTestOnTop)
            {
                return source;
            }

            if (!TextOnTop.TryGetValue(source, out Material onTop) || onTop == null)
            {
                onTop = new Material(source) { name = source.name + " (on top)", hideFlags = HideFlags.HideAndDontSave };
                onTop.SetFloat(UiZTest, DepthTestOnTop);
                TextOnTop[source] = onTop;
            }

            return onTop;
        }

        /// <summary>A filled rounded rectangle that can be stretched to any size (nine-sliced).</summary>
        public static Sprite Rounded(int radius) => Shape("r" + radius, radius, 0f);

        /// <summary>The outline of a rounded rectangle, <paramref name="thickness"/> pixels wide.</summary>
        public static Sprite Ring(int radius, float thickness = 2f) => Shape("o" + radius + "_" + thickness, radius, thickness);

        /// <summary>A soft round glow (for slider thumbs and highlights).</summary>
        public static Sprite Glow
        {
            get
            {
                if (!Sprites.TryGetValue("glow", out Sprite sprite))
                {
                    const int size = 64;
                    var texture = NewTexture(size, size);
                    var pixels = new Color32[size * size];
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                            float a = Mathf.Clamp01(1f - d);
                            pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                        }
                    }

                    texture.SetPixels32(pixels);
                    texture.Apply(false, true);
                    sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                    Sprites["glow"] = sprite;
                }

                return sprite;
            }
        }

        private static Sprite Shape(string key, int radius, float thickness)
        {
            if (Sprites.TryGetValue(key, out Sprite sprite))
            {
                return sprite;
            }

            int size = radius * 2 + 4;
            var texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // signed distance to a rounded square that fills the texture
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float alpha = Mathf.Clamp01(0.5f - outside);
                    if (thickness > 0f)
                    {
                        alpha *= Mathf.Clamp01(0.5f + outside + thickness);
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = radius + 1f;
            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.name = key;
            Sprites[key] = sprite;
            return sprite;
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }

    /// <summary>Tints a graphic from one colour at the top to another at the bottom.</summary>
    [AddComponentMenu("")]
    public class UiGradient : BaseMeshEffect
    {
        public Color Top = Color.white;
        public Color Bottom = Color.white;

        private static readonly List<UIVertex> Vertices = new List<UIVertex>();

        public void Set(Color top, Color bottom)
        {
            Top = top;
            Bottom = bottom;
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0)
            {
                return;
            }

            vh.GetUIVertexStream(Vertices);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < Vertices.Count; i++)
            {
                float y = Vertices[i].position.y;
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }

            float height = Mathf.Max(1e-4f, max - min);
            for (int i = 0; i < Vertices.Count; i++)
            {
                UIVertex v = Vertices[i];
                Color tint = Color.Lerp(Bottom, Top, (v.position.y - min) / height);
                v.color = (Color)v.color * tint;
                Vertices[i] = v;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(Vertices);
        }
    }
}
