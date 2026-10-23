using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// The world seed card in the top-right corner. SeedManager builds it in code, so it needs no scene setup.
// It shows the current seed and the seeds derived from it, and rebuilds the world from a typed seed (Enter
// or the Generate button) or from the current one. Clicking the header or pressing Tab collapses it to a
// small tab, and the choice is remembered between sessions.
[DisallowMultipleComponent]
public class WorldSeedPanel : MonoBehaviour
{
    const string CollapsedPreferenceKey = "WorldSeedPanel.Collapsed";

    // Layout in canvas units at the 1920x1080 reference resolution.
    const float ScreenMargin = 20f;
    const float PanelWidth = 340f;
    const float Padding = 18f;
    const float ContentWidth = PanelWidth - 2f * Padding;
    const float HeaderHeight = 46f;
    const float BodyHeight = 236f;
    const float TitleX = 34f;
    const float ChevronSize = 16f;
    const float ControlHeight = 38f;
    const float CornerRadius = 14f;
    const float ControlRadius = 10f;
    const float ShadowBlur = 18f;
    const float ShadowDrop = 6f;
    const float MaximumTabSeedWidth = 170f;
    const int SeedCharacterLimit = 32;

    const float AnimationDuration = 0.22f;
    const float FlashDuration = 1.2f;

    // Generated sprites have two texels per canvas unit, so edges stay sharp when the canvas scales up.
    const int TexelsPerUnit = 2;

    static readonly Color CardColor = new Color32(14, 22, 24, 235);
    static readonly Color CardBorderColor = new Color(1f, 1f, 1f, 0.09f);
    static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.4f);
    static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.07f);
    static readonly Color PrimaryTextColor = new Color32(236, 243, 240, 255);
    static readonly Color SecondaryTextColor = new Color32(163, 184, 177, 255);
    static readonly Color MutedTextColor = new Color32(112, 133, 127, 255);
    static readonly Color AccentColor = new Color32(94, 214, 168, 255);
    static readonly Color AccentHoverColor = new Color32(128, 228, 190, 255);
    static readonly Color AccentPressedColor = new Color32(70, 186, 143, 255);
    static readonly Color OnAccentTextColor = new Color32(9, 32, 25, 255);
    static readonly Color ControlColor = new Color(1f, 1f, 1f, 0.05f);
    static readonly Color ControlHoverColor = new Color(1f, 1f, 1f, 0.09f);
    static readonly Color ControlPressedColor = new Color(1f, 1f, 1f, 0.13f);
    static readonly Color ControlBorderColor = new Color(1f, 1f, 1f, 0.1f);
    static readonly Color FocusBorderColor = new Color32(94, 214, 168, 200);
    static readonly Color HeaderIdleColor = new Color(1f, 1f, 1f, 0f);
    static readonly Color HeaderHoverColor = new Color(1f, 1f, 1f, 0.05f);
    static readonly Color HeaderPressedColor = new Color(1f, 1f, 1f, 0.08f);
    static readonly Color KeycapColor = new Color(1f, 1f, 1f, 0.08f);

    static readonly string[] SeedAdjectives =
    {
        "MOSSY", "FROSTY", "MISTY", "AMBER", "SILVER", "QUIET",
        "NORTHERN", "HOLLOW", "WILD", "PALE", "STORMY", "SUNLIT"
    };
    static readonly string[] SeedNouns =
    {
        "FJORD", "PINE", "TAIGA", "LAKE", "RIDGE", "BIRCH",
        "TUNDRA", "ISLE", "GROVE", "SPRUCE", "MARSH", "CAIRN"
    };

    const string PrewarmedCharacters =
        " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-&.";

    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    static TMP_FontAsset systemRegularFont;
    static TMP_FontAsset systemStrongFont;

    readonly System.Random random = new System.Random();
    SeedManager seedManager;
    TMP_FontAsset regularFont;
    TMP_FontAsset strongFont;

    RectTransform frame;
    RectTransform chevron;
    CanvasGroup body;
    CanvasGroup shortcutHint;
    TextMeshProUGUI title;
    TextMeshProUGUI tabSeed;
    TextMeshProUGUI seedName;
    TextMeshProUGUI numericSeed;
    TextMeshProUGUI terrainSeed;
    TextMeshProUGUI generateLabel;
    TMP_InputField seedInput;
    Image seedInputOutline;

    bool collapsed;
    float openAmount;
    float collapsedWidth = PanelWidth;
    bool layoutDirty = true;
    float flash;

    public static WorldSeedPanel Create(SeedManager seedManager, TMP_FontAsset fallbackFont)
    {
        GameObject root = new GameObject("World Seed Panel", typeof(RectTransform));
        // Built while inactive, so every UI component is fully set up before its OnEnable runs.
        root.SetActive(false);
        root.transform.SetParent(seedManager.transform, false);

        WorldSeedPanel panel = root.AddComponent<WorldSeedPanel>();
        panel.Build(seedManager, fallbackFont);
        root.SetActive(true);
        return panel;
    }

    void Start()
    {
        EnsureEventSystem();
        RefreshSeedLabels();
        ApplyOpenAmount();
    }

    void OnDestroy()
    {
        if (seedManager != null) seedManager.SeedChanged -= RefreshSeedLabels;
    }

    void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame && !GodCamera.IsTypingInUI())
        {
            SetCollapsed(!collapsed);
        }

        float target = collapsed ? 0f : 1f;
        if (openAmount != target || layoutDirty)
        {
            openAmount = Mathf.MoveTowards(openAmount, target, deltaTime / AnimationDuration);
            ApplyOpenAmount();
        }

        Color outline = seedInput.isFocused ? FocusBorderColor : ControlBorderColor;
        seedInputOutline.color = Color.Lerp(seedInputOutline.color, outline, 1f - Mathf.Exp(-18f * deltaTime));

        if (flash > 0f)
        {
            flash = Mathf.Max(0f, flash - deltaTime / FlashDuration);
            seedName.color = Color.Lerp(PrimaryTextColor, AccentColor, flash * flash);
        }
    }

    void Build(SeedManager manager, TMP_FontAsset fallbackFont)
    {
        seedManager = manager;
        seedManager.SeedChanged += RefreshSeedLabels;

        LoadSystemFonts();
        regularFont = systemRegularFont != null ? systemRegularFont
            : fallbackFont != null ? fallbackFont : TMP_Settings.defaultFontAsset;
        strongFont = systemRegularFont != null && systemStrongFont != null ? systemStrongFont : regularFont;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                                           AdditionalCanvasShaderChannels.Normal |
                                           AdditionalCanvasShaderChannels.Tangent;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        frame = CreateRect("Frame", transform);
        frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.one;
        frame.anchoredPosition = new Vector2(-ScreenMargin, -ScreenMargin);

        Image shadow = CreateImage("Shadow", frame, ShadowSprite(CornerRadius, ShadowBlur), ShadowColor);
        Stretch(shadow.rectTransform, -ShadowBlur, -ShadowBlur - ShadowDrop, -ShadowBlur, -ShadowBlur + ShadowDrop);

        Image card = CreateImage("Card", frame, RoundedSprite(CornerRadius), CardColor);
        card.raycastTarget = true;
        Stretch(card.rectTransform, 0f, 0f, 0f, 0f);
        // Clips the body while the card grows and shrinks.
        card.gameObject.AddComponent<RectMask2D>();

        BuildHeader(card.rectTransform);
        BuildBody(card.rectTransform);

        Image border = CreateImage("Border", frame, RoundedSprite(CornerRadius, 1f), CardBorderColor);
        Stretch(border.rectTransform, 0f, 0f, 0f, 0f);

        collapsed = PlayerPrefs.GetInt(CollapsedPreferenceKey, 0) == 1;
        openAmount = collapsed ? 0f : 1f;
        body.interactable = !collapsed;
        body.blocksRaycasts = !collapsed;
    }

    void BuildHeader(RectTransform card)
    {
        RectTransform header = CreateRect("Header", card);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0f, HeaderHeight);
        header.anchoredPosition = Vector2.zero;

        // The whole header toggles the panel; the pointer lands on this plate, inset from the card's edge.
        Image hover = CreateImage("Hover", header, RoundedSprite(ControlRadius), Color.white);
        hover.raycastTarget = true;
        Stretch(hover.rectTransform, 4f, 4f, 4f, 4f);
        Button toggle = header.gameObject.AddComponent<Button>();
        SetUpSelectable(toggle, hover, Tints(HeaderIdleColor, HeaderHoverColor, HeaderPressedColor));
        toggle.onClick.AddListener(() => SetCollapsed(!collapsed));

        Image dot = CreateImage("Dot", header, CircleSprite(8f), AccentColor);
        PlaceLeft(dot.rectTransform, Padding, 9f, 9f);

        title = CreateText("Title", header, "WORLD SEED", 12f, SecondaryTextColor, true,
                           TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 12f;
        PlaceLeft(title.rectTransform, TitleX, 120f, 20f);

        // Stands in for the body while collapsed.
        tabSeed = CreateText("Seed", header, "", 13f, PrimaryTextColor, true, TextAlignmentOptions.Left);
        PlaceLeft(tabSeed.rectTransform, TitleX + 120f, MaximumTabSeedWidth, 20f);

        RectTransform shortcut = CreateRect("Shortcut", header);
        PlaceRight(shortcut, 16f + ChevronSize + 8f, 36f, 20f);
        shortcutHint = shortcut.gameObject.AddComponent<CanvasGroup>();
        Image keycap = CreateImage("Keycap", shortcut, RoundedSprite(5f), KeycapColor);
        Stretch(keycap.rectTransform, 0f, 0f, 0f, 0f);
        TextMeshProUGUI key = CreateText("Key", shortcut, "TAB", 10f, MutedTextColor, true,
                                         TextAlignmentOptions.Center);
        key.characterSpacing = 8f;
        Stretch(key.rectTransform, 0f, 0f, 0f, 0f);

        chevron = CreateImage("Chevron", header, ChevronSprite(), SecondaryTextColor).rectTransform;
        PlaceRight(chevron, 16f, ChevronSize, ChevronSize);
    }

    void BuildBody(RectTransform card)
    {
        // Pinned to the card's top-right corner like the card itself, so while the card narrows the body
        // stays put and is clipped rather than squeezed.
        RectTransform content = CreateRect("Body", card);
        PlaceTopRight(content, 0f, HeaderHeight, PanelWidth, BodyHeight);
        body = content.gameObject.AddComponent<CanvasGroup>();

        Image divider = CreateImage("Divider", content, null, DividerColor);
        PlaceTopLeft(divider.rectTransform, Padding, 0f, ContentWidth, 1f);

        seedName = CreateText("Seed", content, "", 24f, PrimaryTextColor, true, TextAlignmentOptions.Left);
        PlaceTopLeft(seedName.rectTransform, Padding, 14f, ContentWidth, 32f);

        numericSeed = CreateDetailRow(content, "Numeric seed", 52f);
        terrainSeed = CreateDetailRow(content, "Terrain & plants", 72f);

        const float controlsY = 106f;
        seedInput = CreateSeedInput(content, controlsY, ContentWidth - ControlHeight - 8f);

        Image randomBackground = CreateImage("Random Seed", content, RoundedSprite(ControlRadius), Color.white);
        randomBackground.raycastTarget = true;
        PlaceTopRight(randomBackground.rectTransform, Padding, controlsY, ControlHeight, ControlHeight);
        AddOutline(randomBackground.rectTransform);
        Image dice = CreateImage("Icon", randomBackground.rectTransform, DiceSprite(), SecondaryTextColor);
        PlaceCentre(dice.rectTransform, 18f, 18f);
        Button randomButton = randomBackground.gameObject.AddComponent<Button>();
        SetUpSelectable(randomButton, randomBackground, Tints(ControlColor, ControlHoverColor, ControlPressedColor));
        randomButton.onClick.AddListener(FillRandomSeed);

        Image generateBackground = CreateImage("Generate", content, RoundedSprite(ControlRadius), Color.white);
        generateBackground.raycastTarget = true;
        PlaceTopLeft(generateBackground.rectTransform, Padding, controlsY + ControlHeight + 8f, ContentWidth, 40f);
        generateLabel = CreateText("Label", generateBackground.rectTransform, "", 15f, OnAccentTextColor, true,
                                   TextAlignmentOptions.Center);
        Stretch(generateLabel.rectTransform, 0f, 0f, 0f, 0f);
        Button generateButton = generateBackground.gameObject.AddComponent<Button>();
        SetUpSelectable(generateButton, generateBackground, Tints(AccentColor, AccentHoverColor, AccentPressedColor));
        generateButton.onClick.AddListener(Generate);

        TextMeshProUGUI hint = CreateText("Hint", content, "Rebuilds the terrain, plants and animals.", 12f,
                                          MutedTextColor, false, TextAlignmentOptions.Left);
        PlaceTopLeft(hint.rectTransform, Padding, 202f, ContentWidth, 16f);
    }

    TextMeshProUGUI CreateDetailRow(RectTransform parent, string label, float top)
    {
        TextMeshProUGUI caption = CreateText(label, parent, label, 13f, MutedTextColor, false,
                                             TextAlignmentOptions.Left);
        PlaceTopLeft(caption.rectTransform, Padding, top, ContentWidth, 18f);

        TextMeshProUGUI value = CreateText(label + " Value", parent, "", 13f, SecondaryTextColor, false,
                                           TextAlignmentOptions.Right);
        PlaceTopLeft(value.rectTransform, Padding, top, ContentWidth, 18f);
        return value;
    }

    TMP_InputField CreateSeedInput(RectTransform parent, float top, float width)
    {
        Image background = CreateImage("Seed Input", parent, RoundedSprite(ControlRadius), Color.white);
        background.raycastTarget = true;
        PlaceTopLeft(background.rectTransform, Padding, top, width, ControlHeight);
        seedInputOutline = AddOutline(background.rectTransform);

        RectTransform viewport = CreateRect("Text Area", background.rectTransform);
        Stretch(viewport, 12f, 0f, 12f, 0f);
        // A little slack so the caret is not clipped at either end.
        viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(-4f, -4f, -4f, -4f);

        TextMeshProUGUI placeholder = CreateText("Placeholder", viewport, "Type a new seed", 14f, MutedTextColor,
                                                 false, TextAlignmentOptions.Left);
        Stretch(placeholder.rectTransform, 0f, 0f, 0f, 0f);
        TextMeshProUGUI text = CreateText("Text", viewport, "", 14f, PrimaryTextColor, false,
                                          TextAlignmentOptions.Left);
        text.overflowMode = TextOverflowModes.Overflow;
        Stretch(text.rectTransform, 0f, 0f, 0f, 0f);

        TMP_InputField field = background.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = viewport;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = SeedCharacterLimit;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.richText = false;
        field.onFocusSelectAll = true;
        field.customCaretColor = true;
        field.caretColor = AccentColor;
        field.caretWidth = 2;
        field.selectionColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f);

        ColorBlock tints = Tints(ControlColor, ControlHoverColor, ControlHoverColor);
        tints.selectedColor = ControlHoverColor;
        SetUpSelectable(field, background, tints);

        field.onSubmit.AddListener(_ => Generate());
        field.onValueChanged.AddListener(_ => RefreshGenerateLabel());
        return field;
    }

    // Rebuilds the world from the typed seed, or from the current one when nothing is typed.
    void Generate()
    {
        string typedSeed = seedInput.text.Trim();
        seedInput.text = "";
        ClearSelection();
        flash = 1f;
        seedManager.GenerateWorld(typedSeed);
    }

    void FillRandomSeed()
    {
        string adjective = SeedAdjectives[random.Next(SeedAdjectives.Length)];
        string noun = SeedNouns[random.Next(SeedNouns.Length)];
        seedInput.text = $"{adjective}-{noun}-{random.Next(10, 100)}";
        ClearSelection();
    }

    void SetCollapsed(bool value)
    {
        collapsed = value;
        body.interactable = !collapsed;
        body.blocksRaycasts = !collapsed;
        PlayerPrefs.SetInt(CollapsedPreferenceKey, collapsed ? 1 : 0);
        ClearSelection();
    }

    void RefreshSeedLabels()
    {
        string seed = seedManager.SeedString;
        seedName.text = seed;
        tabSeed.text = seed;
        numericSeed.text = seedManager.Seed.ToString();
        terrainSeed.text = seedManager.GetSeed("Terrain").ToString();
        RefreshGenerateLabel();

        // The collapsed tab is just wide enough for the title and the seed.
        float titleWidth = title.GetPreferredValues(title.text).x;
        float seedWidth = Mathf.Min(tabSeed.GetPreferredValues(seed).x, MaximumTabSeedWidth);
        float seedX = TitleX + titleWidth + 10f;
        PlaceLeft(title.rectTransform, TitleX, titleWidth + 2f, 20f);
        PlaceLeft(tabSeed.rectTransform, seedX, seedWidth + 2f, 20f);
        collapsedWidth = Mathf.Min(PanelWidth, seedX + seedWidth + 16f + ChevronSize + 16f);
        layoutDirty = true;
    }

    void RefreshGenerateLabel()
    {
        string typedSeed = seedInput.text.Trim();
        bool sameWorld = typedSeed.Length == 0 || typedSeed == seedManager.SeedString;
        generateLabel.text = sameWorld ? "Regenerate world" : "Generate world";
    }

    void ApplyOpenAmount()
    {
        float eased = openAmount * openAmount * (3f - 2f * openAmount);
        frame.sizeDelta = new Vector2(Mathf.Lerp(collapsedWidth, PanelWidth, eased),
                                      Mathf.Lerp(HeaderHeight, HeaderHeight + BodyHeight, eased));

        // The body fades in late while opening and out early while closing; the seed takes its place in the tab.
        float bodyAlpha = Mathf.Clamp01((eased - 0.4f) / 0.6f);
        body.alpha = bodyAlpha;
        shortcutHint.alpha = bodyAlpha;
        tabSeed.alpha = Mathf.Clamp01(1f - 3f * eased);
        chevron.localEulerAngles = new Vector3(0f, 0f, 180f * eased);
        layoutDirty = false;
    }

    // Leaves nothing selected, so Enter cannot press a button again and the camera keys stay free.
    static void ClearSelection()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current == null && FindAnyObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }

    // The panel's typeface, for other small UI built in code.
    internal static TMP_FontAsset UiFont(bool strong)
    {
        LoadSystemFonts();
        TMP_FontAsset font = strong && systemStrongFont != null ? systemStrongFont : systemRegularFont;
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    // Avenir Next on macOS and Segoe UI on Windows read better than the bundled font; elsewhere the panel
    // uses the font the older seed UI used.
    static void LoadSystemFonts()
    {
        if (systemRegularFont != null) return;

        const string avenirNext = "/System/Library/Fonts/Avenir Next.ttc";
        systemRegularFont = LoadFontFace(avenirNext, "Medium");
        if (systemRegularFont != null)
        {
            systemStrongFont = LoadFontFace(avenirNext, "Demi Bold");
            return;
        }

        string windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        systemRegularFont = LoadFontFace(Path.Combine(windowsFonts, "segoeui.ttf"), null);
        if (systemRegularFont != null)
        {
            systemStrongFont = LoadFontFace(Path.Combine(windowsFonts, "seguisb.ttf"), null);
        }
    }

    // Loads an installed font file, picking the face by style name when the file holds several.
    static TMP_FontAsset LoadFontFace(string path, string style)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            int faceIndex = 0;
            if (!string.IsNullOrEmpty(style))
            {
                if (FontEngine.LoadFontFace(path) != FontEngineError.Success) return null;
                faceIndex = Array.FindIndex(FontEngine.GetFontFaces(), face => IsStyle(face, style));
                if (faceIndex < 0) return null;
            }

            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(path, faceIndex, 90, 9, GlyphRenderMode.SDFAA,
                                                               1024, 1024);
            if (font == null) return null;

            // Adding the panel's usual characters up front proves the face renders, and saves adding them
            // one at a time later.
            if (!font.TryAddCharacters(PrewarmedCharacters))
            {
                Destroy(font);
                return null;
            }

            font.name = $"{Path.GetFileNameWithoutExtension(path)} {style}".Trim();
            return font;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not load the font {path}: {exception.Message}");
            return null;
        }
    }

    // Font faces are listed as "Family - Style".
    static bool IsStyle(string face, string style)
    {
        int separator = face.LastIndexOf(" - ", StringComparison.Ordinal);
        string faceStyle = separator >= 0 ? face.Substring(separator + 3) : face;
        return string.Equals(faceStyle, style, StringComparison.OrdinalIgnoreCase);
    }

    TextMeshProUGUI CreateText(string objectName, Transform parent, string content, float size, Color color,
                               bool strong, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = CreateRect(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = strong ? strongFont : regularFont;
        // Without a heavier face, bold is drawn by thickening the regular one.
        if (strong && strongFont == regularFont) text.fontStyle = FontStyles.Bold;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = false;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    internal static Image CreateImage(string objectName, Transform parent, Sprite sprite, Color color)
    {
        Image image = CreateRect(objectName, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Image AddOutline(RectTransform control)
    {
        Image outline = CreateImage("Outline", control, RoundedSprite(ControlRadius, 1f), ControlBorderColor);
        Stretch(outline.rectTransform, 0f, 0f, 0f, 0f);
        return outline;
    }

    internal static RectTransform CreateRect(string objectName, Transform parent)
    {
        RectTransform rect = (RectTransform)new GameObject(objectName, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    static ColorBlock Tints(Color normal, Color hover, Color pressed)
    {
        ColorBlock tints = ColorBlock.defaultColorBlock;
        tints.normalColor = normal;
        tints.highlightedColor = hover;
        tints.pressedColor = pressed;
        tints.selectedColor = normal;
        tints.disabledColor = normal;
        tints.colorMultiplier = 1f;
        tints.fadeDuration = 0.08f;
        return tints;
    }

    // Graphics are white, so the tints are the colours actually shown.
    static void SetUpSelectable(Selectable selectable, Graphic target, ColorBlock tints)
    {
        selectable.targetGraphic = target;
        selectable.transition = Selectable.Transition.ColorTint;
        selectable.colors = tints;
        // No keyboard navigation between controls: the arrow keys and WASD belong to the camera.
        selectable.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    // Pins a rect's top-left corner at an offset from its parent's top-left corner.
    static void PlaceTopLeft(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(left, -top);
    }

    static void PlaceTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(-right, -top);
    }

    // Centres a rect vertically, a distance in from the parent's left or right edge. The pivot is the rect's
    // centre, so the chevron turns in place.
    internal static void PlaceLeft(RectTransform rect, float left, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(left + width * 0.5f, 0f);
    }

    static void PlaceRight(RectTransform rect, float right, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(-right - width * 0.5f, 0f);
    }

    internal static void PlaceCentre(RectTransform rect, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = Vector2.zero;
    }

    // Fills the parent, inset by the given amounts; negative amounts reach outside it.
    internal static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    // A white rounded rectangle for sliced Images, or only its outline when a ring width is given.
    internal static Sprite RoundedSprite(float radius, float ringWidth = 0f)
    {
        return CachedSprite($"Rounded {radius} {ringWidth}", () =>
        {
            int corner = Mathf.CeilToInt(radius * TexelsPerUnit);
            // One clear texel around the shape for anti-aliasing and two stretchable texels in the middle.
            int size = 2 * (corner + 1) + 2;
            Vector2 centre = Vector2.one * (size * 0.5f);
            Vector2 halfSize = centre - Vector2.one;
            float ring = ringWidth * TexelsPerUnit;
            return Paint("Rounded", size, size, corner + 1, point =>
            {
                float distance = RoundedRectDistance(point, centre, halfSize, corner);
                float coverage = Mathf.Clamp01(0.5f - distance);
                return ring > 0f ? coverage - Mathf.Clamp01(0.5f - distance - ring) : coverage;
            });
        });
    }

    // A soft rounded rectangle whose solid middle is inset from the sprite's edges by the blur distance.
    static Sprite ShadowSprite(float radius, float blur)
    {
        return CachedSprite($"Shadow {radius} {blur}", () =>
        {
            int corner = Mathf.CeilToInt(radius * TexelsPerUnit);
            int spread = Mathf.CeilToInt(blur * TexelsPerUnit);
            int size = 2 * (corner + spread) + 2;
            Vector2 centre = Vector2.one * (size * 0.5f);
            Vector2 halfSize = centre - Vector2.one * spread;
            return Paint("Shadow", size, size, corner + spread, point =>
            {
                float outside = Mathf.Max(0f, RoundedRectDistance(point, centre, halfSize, corner)) / (0.45f * spread);
                return Mathf.Exp(-outside * outside);
            });
        });
    }

    static Sprite CircleSprite(float diameter)
    {
        return CachedSprite($"Circle {diameter}", () =>
        {
            float radius = diameter * TexelsPerUnit * 0.5f;
            int size = Mathf.CeilToInt(2f * radius) + 2;
            Vector2 centre = Vector2.one * (size * 0.5f);
            return Paint("Circle", size, size, 0f, point => 0.5f - (Vector2.Distance(point, centre) - radius));
        });
    }

    // A downward chevron, turned to point up while the panel is open.
    static Sprite ChevronSprite()
    {
        return CachedSprite("Chevron", () =>
        {
            Vector2 left = new Vector2(10f, 19.5f);
            Vector2 tip = new Vector2(16f, 13.5f);
            Vector2 right = new Vector2(22f, 19.5f);
            return Paint("Chevron", 16 * TexelsPerUnit, 16 * TexelsPerUnit, 0f, point =>
                1.9f - Mathf.Min(SegmentDistance(point, left, tip), SegmentDistance(point, tip, right)));
        });
    }

    // A die showing three, for the random seed button.
    static Sprite DiceSprite()
    {
        return CachedSprite("Dice", () =>
        {
            Vector2 centre = new Vector2(18f, 18f);
            Vector2 halfSize = new Vector2(12.5f, 12.5f);
            Vector2[] pips = { new Vector2(12.5f, 23.5f), centre, new Vector2(23.5f, 12.5f) };
            return Paint("Dice", 18 * TexelsPerUnit, 18 * TexelsPerUnit, 0f, point =>
            {
                float distance = RoundedRectDistance(point, centre, halfSize, 6f);
                float alpha = Mathf.Clamp01(0.5f - distance) - Mathf.Clamp01(0.5f - distance - 2.6f);
                foreach (Vector2 pip in pips)
                {
                    alpha = Mathf.Max(alpha, Mathf.Clamp01(2.9f - Vector2.Distance(point, pip)));
                }

                return alpha;
            });
        });
    }

    static Sprite CachedSprite(string key, Func<Sprite> paint)
    {
        if (!sprites.TryGetValue(key, out Sprite sprite) || sprite == null)
        {
            sprite = paint();
            sprites[key] = sprite;
        }

        return sprite;
    }

    // Draws a white sprite whose alpha comes from a function of the texel centre, in texels.
    internal static Sprite Paint(string spriteName, int width, int height, float border, Func<Vector2, float> alphaAt)
    {
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float alpha = Mathf.Clamp01(alphaAt(new Vector2(x + 0.5f, y + 0.5f)));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = spriteName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
                                      100f * TexelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.one * border);
        sprite.name = spriteName;
        return sprite;
    }

    // Signed distance from a point to a rounded rectangle, negative inside.
    static float RoundedRectDistance(Vector2 point, Vector2 centre, Vector2 halfSize, float radius)
    {
        float x = Mathf.Abs(point.x - centre.x) - (halfSize.x - radius);
        float y = Mathf.Abs(point.y - centre.y) - (halfSize.y - radius);
        float outside = new Vector2(Mathf.Max(x, 0f), Mathf.Max(y, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(x, y), 0f) - radius;
    }

    internal static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float along = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
        return Vector2.Distance(point, start + segment * along);
    }
}
