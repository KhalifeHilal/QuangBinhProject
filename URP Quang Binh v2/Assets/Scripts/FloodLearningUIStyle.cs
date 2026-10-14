using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared visual style for the dynamically-created learning panels.</summary>
public static class FloodLearningUIStyle
{
    static Sprite roundedSprite;
    static TMP_FontAsset cachedFont;

    public static Color PanelColor => new Color(.025f, .055f, .09f, .98f);
    public static Color ButtonColor => new Color(.07f, .24f, .37f, 1f);
    public static Color ButtonHighlight => new Color(.12f, .65f, .9f, 1f);

    public static void ApplyPanel(Image image)
    {
        if (image == null) return;
        image.sprite = RoundedSprite;
        image.type = Image.Type.Sliced;
        image.color = PanelColor;
    }

    public static void ApplyButton(Image image, Color? color = null)
    {
        if (image == null) return;
        image.sprite = RoundedSprite;
        image.type = Image.Type.Sliced;
        image.color = color ?? ButtonColor;
    }

    public static void ApplySlider(Image image, Color color)
    {
        if (image == null) return;
        image.sprite = RoundedSprite;
        image.type = Image.Type.Sliced;
        image.color = color;
    }

    public static void ApplyFont(TMP_Text text)
    {
        if (text == null) return;
        TMP_FontAsset font = Font;
        if (font != null) text.font = font;
    }

    static TMP_FontAsset Font
    {
        get
        {
            if (cachedFont != null) return cachedFont;
            cachedFont = TMP_Settings.defaultFontAsset;
            if (cachedFont == null)
            {
                TMP_Text existing = Object.FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
                if (existing != null) cachedFont = existing.font;
            }
            return cachedFont;
        }
    }

    static Sprite RoundedSprite
    {
        get
        {
            if (roundedSprite != null) return roundedSprite;

            const int size = 64;
            const float radius = 16f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FloodLearningUI_RoundedTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, 0f, x - (size - 1 - radius));
                float dy = Mathf.Max(radius - y, 0f, y - (size - 1 - radius));
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt((radius + .75f - distance) * 255f), 0, 255);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            roundedSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            roundedSprite.name = "FloodLearningUI_RoundedSprite";
            roundedSprite.hideFlags = HideFlags.HideAndDontSave;
            return roundedSprite;
        }
    }
}
