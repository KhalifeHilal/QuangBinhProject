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
    static Sprite arrowLeftSprite;
    static Sprite arrowRightSprite;
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

    public enum ArrowDirection { Up, Down, Left, Right }

    public static void ApplyArrow(Image image, ArrowDirection direction)
    {
        if (image == null) return;
        image.sprite = direction switch
        {
            ArrowDirection.Left => ArrowLeftSprite,
            ArrowDirection.Right => ArrowRightSprite,
            ArrowDirection.Down => ArrowDownSprite,
            _ => ArrowUpSprite
        };
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

    static Sprite ArrowUpSprite => arrowUpSprite != null ? arrowUpSprite : (arrowUpSprite = CreateArrowSprite(ArrowDirection.Up));
    static Sprite ArrowDownSprite => arrowDownSprite != null ? arrowDownSprite : (arrowDownSprite = CreateArrowSprite(ArrowDirection.Down));
    static Sprite ArrowLeftSprite => arrowLeftSprite != null ? arrowLeftSprite : (arrowLeftSprite = CreateArrowSprite(ArrowDirection.Left));
    static Sprite ArrowRightSprite => arrowRightSprite != null ? arrowRightSprite : (arrowRightSprite = CreateArrowSprite(ArrowDirection.Right));

    static Sprite CreateArrowSprite(ArrowDirection direction)
    {
        const int size = 48;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = $"FloodLearningUI_Arrow{direction}Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * .5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Texture pixels use a bottom-left origin in Unity. The narrow
            // end is the arrow tip and the opposite edge is the broad base.
            float progress;
            float halfWidth;
            if (direction == ArrowDirection.Up)
            {
                progress = (size - 1 - y) / (float)(size - 1);
                halfWidth = progress * (size * .42f);
                bool inside = Mathf.Abs(x - center) <= halfWidth;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                continue;
            }
            if (direction == ArrowDirection.Down)
            {
                progress = y / (float)(size - 1);
                halfWidth = progress * (size * .42f);
                bool inside = Mathf.Abs(x - center) <= halfWidth;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                continue;
            }

            progress = direction == ArrowDirection.Right
                ? (size - 1 - x) / (float)(size - 1)
                : x / (float)(size - 1);
            halfWidth = progress * (size * .42f);
            bool horizontalInside = Mathf.Abs(y - center) <= halfWidth;
            pixels[y * size + x] = horizontalInside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        sprite.name = $"FloodLearningUI_Arrow{direction}";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
