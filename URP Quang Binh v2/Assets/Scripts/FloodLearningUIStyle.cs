using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Shared visual style for the dynamically-created learning panels.</summary>
public static class FloodLearningUIStyle
{
    static Sprite roundedSprite;
    static Sprite arrowUpSprite;
    static Sprite arrowDownSprite;
    static TMP_FontAsset cachedFont;
    static TMP_FontAsset cachedMediumFont;
    static int normalizedSceneHandle = -1;

    // The same dark translucent panel colour used by the PlayMaker tutorial panels.
    public static Color PanelColor => new Color(.03773582f, .03773582f, .03773582f, .8627451f);
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

    public static void ApplyArrow(Image image, bool pointsUp)
    {
        if (image == null) return;
        image.sprite = pointsUp ? ArrowUpSprite : ArrowDownSprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        image.raycastTarget = false;
    }

    public static void ApplyFont(TMP_Text text)
    {
        if (text == null) return;
        // Keep the same TMP font asset on every learning UI element.  The
        // text style (for example Bold) may still be used for emphasis, but
        // the underlying asset is always Lexend-Medium SDF.
        TMP_FontAsset font = MediumFont;
        if (font == null) font = Font;
        if (font == null) return;

        text.font = font;
        text.fontSharedMaterial = font.material;

        // Tutorial text can already exist in the scene before the runtime
        // learning panels are created. Normalize those components once too,
        // so no Lexend-Bold asset remains in the active UI.
        int activeSceneHandle = SceneManager.GetActiveScene().handle;
        if (normalizedSceneHandle == activeSceneHandle) return;
        normalizedSceneHandle = activeSceneHandle;
        foreach (TMP_Text candidate in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            candidate.font = font;
            candidate.fontSharedMaterial = font.material;
        }
    }

    static TMP_FontAsset MediumFont
    {
        get
        {
            if (cachedMediumFont == null) cachedMediumFont = FindTutorialFont("Lexend-Medium");
            return cachedMediumFont;
        }
    }

    static TMP_FontAsset FindTutorialFont(string namePart)
    {
        foreach (TMP_Text candidate in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.font != null && candidate.font.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                return candidate.font;
        }
        return null;
    }

    static TMP_FontAsset Font
    {
        get
        {
            if (cachedFont != null) return cachedFont;
            cachedFont = TMP_Settings.defaultFontAsset;
            if (cachedFont == null)
            {
                TMP_Text existing = UnityEngine.Object.FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
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

            roundedSprite = Resources.Load<Sprite>("TutorialGuiPanel");
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

    static Sprite ArrowUpSprite => arrowUpSprite != null ? arrowUpSprite : (arrowUpSprite = CreateArrowSprite(true));
    static Sprite ArrowDownSprite => arrowDownSprite != null ? arrowDownSprite : (arrowDownSprite = CreateArrowSprite(false));

    static Sprite CreateArrowSprite(bool pointsUp)
    {
        const int size = 48;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = pointsUp ? "FloodLearningUI_ArrowUpTexture" : "FloodLearningUI_ArrowDownTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * .5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // The narrow end is the point of the arrow.  For an up arrow it
            // must be at the top; for a down arrow it must be at the bottom.
            float progress = pointsUp ? y / (float)(size - 1) : (size - 1 - y) / (float)(size - 1);
            float halfWidth = progress * (size * .42f);
            bool inside = Mathf.Abs(x - center) <= halfWidth;
            pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        sprite.name = pointsUp ? "FloodLearningUI_ArrowUp" : "FloodLearningUI_ArrowDown";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
