using TMPro;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>World-space participant check-in shown once when the tutorial scene starts.</summary>
public sealed class ParticipantCheckInUI : MonoBehaviour
{
    static bool created;
    Canvas canvas;
    TMP_Text numberText, selectedSetText;
    Button[] setButtons;
    int participantNumber = 1;
    ParticipantQuestionSet selectedSet = ParticipantQuestionSet.A;
    PlayMakerFSM tutorialManager;
    bool tutorialManagerWasEnabled;
    readonly System.Collections.Generic.List<GameObject> hiddenTutorialObjects = new System.Collections.Generic.List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBootstrap()
    {
        created = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.name.ToLowerInvariant().Contains("tutorial") || created) return;
        GameObject root = new GameObject("Participant_CheckIn");
        root.AddComponent<ParticipantCheckInUI>();
        created = true;
    }

    void Awake()
    {
        PauseTutorialManager();
        HideTutorialPanelsWhileCheckingIn();
        Build();
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 500;
        canvas.worldCamera = Camera.main;
        gameObject.AddComponent<CanvasScaler>();
        TrackedDeviceGraphicRaycaster raycaster = gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        raycaster.ignoreReversedGraphics = false;
        RectTransform root = (RectTransform)transform;
        root.sizeDelta = new Vector2(1100, 900);
        root.localScale = Vector3.one * .00135f;
        Image background = gameObject.AddComponent<Image>();
        background.color = new Color(.02f, .045f, .075f, .98f);
        // The panel itself is raycastable so the hover marker is visible anywhere on it.
        // Buttons remain the topmost hit and receive the Trigger click.
        background.raycastTarget = true;

        Text("Participant Check-In", 48, -35, 0, TextAlignmentOptions.Center, true);
        Text("Participant number", 34, -135, 0, TextAlignmentOptions.Center, true);
        numberText = Text("01", 52, -205, 0, TextAlignmentOptions.Center, true);
        Button("▲", 28, -190, 75, ChangeNumberUp, 130);
        Button("▼", 28, -190, -75, ChangeNumberDown, 130);

        Text("Question set", 34, -465, 0, TextAlignmentOptions.Center, true);
        setButtons = new Button[4];
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            setButtons[i] = Button(((char)('A' + i)).ToString(), 34, -535, -330 + i * 220, () => SelectSet((ParticipantQuestionSet)index), 170);
        }
        selectedSetText = Text("Selected: A", 28, -635, 0, TextAlignmentOptions.Center, false);
        selectedSetText.color = new Color(.45f, .85f, 1f);
        Text("Point at a button with the Ray and press Trigger.", 26, -685, 0, TextAlignmentOptions.Center, false);
        Button("Check In", 34, -765, 0, Confirm, 500);
        PlaceBeforeXRRig();
        SelectSet(ParticipantQuestionSet.A);
    }

    void ChangeNumberUp() { participantNumber = participantNumber >= 99 ? 1 : participantNumber + 1; RefreshNumber(); }
    void ChangeNumberDown() { participantNumber = participantNumber <= 1 ? 99 : participantNumber - 1; RefreshNumber(); }
    void RefreshNumber() { if (numberText != null) numberText.text = participantNumber.ToString("00"); }

    void SelectSet(ParticipantQuestionSet value)
    {
        selectedSet = value;
        if (selectedSetText != null) selectedSetText.text = $"Selected: {value}";
        if (setButtons == null) return;
        for (int i = 0; i < setButtons.Length; i++)
        {
            ColorBlock colors = setButtons[i].colors;
            colors.normalColor = i == (int)value ? new Color(.08f, .55f, .85f) : new Color(.07f, .24f, .37f);
            colors.highlightedColor = new Color(.12f, .65f, .9f);
            setButtons[i].colors = colors;
        }
    }

    void Confirm()
    {
        ParticipantSession.CheckIn(participantNumber, selectedSet);
        RestoreTutorialPanels();
        RestoreTutorialManager();
        Destroy(gameObject);
    }

    void PauseTutorialManager()
    {
        GameObject manager = FindSceneObject("Tutorial_Manager");
        if (manager == null) return;
        tutorialManager = manager.GetComponent<PlayMakerFSM>();
        if (tutorialManager == null) return;
        tutorialManagerWasEnabled = tutorialManager.enabled;
        tutorialManager.enabled = false;
    }

    void RestoreTutorialManager()
    {
        if (tutorialManager != null) tutorialManager.enabled = tutorialManagerWasEnabled;
    }

    void HideTutorialPanelsWhileCheckingIn()
    {
        string[] names = { "TutoPanel_Interface", "TutoPanel_Moving", "Choose_Language" };
        foreach (string name in names)
        {
            GameObject target = FindSceneObject(name);
            if (target != null && target.activeSelf)
            {
                hiddenTutorialObjects.Add(target);
                target.SetActive(false);
            }
        }
    }

    void RestoreTutorialPanels()
    {
        foreach (GameObject target in hiddenTutorialObjects)
            if (target != null) target.SetActive(true);
        hiddenTutorialObjects.Clear();
    }

    static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child.gameObject;
        return null;
    }

    void PlaceBeforeXRRig()
    {
        GameObject rigObject = FindSceneObject("XR Origin (XR Rig)");
        Camera camera = Camera.main;
        if (rigObject == null)
        {
            FloodLearningUI.PlaceInFrontOfPlayer(transform, 2.15f);
            return;
        }

        Transform rig = rigObject.transform;
        Vector3 forward = Vector3.ProjectOnPlane(rig.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        transform.position = rig.position + Vector3.up * 1.35f + forward * 2.15f;
        Vector3 lookTarget = camera != null ? camera.transform.position : rig.position + Vector3.up * 1.35f;
        transform.rotation = Quaternion.LookRotation(transform.position - lookTarget, Vector3.up);
    }

    TMP_Text Text(string value, float size, float top, float x, TextAlignmentOptions alignment, bool bold)
    {
        GameObject obj = new GameObject("Text_" + value, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(x, top);
        rect.sizeDelta = new Vector2(1080, 68);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.text = value; text.fontSize = size; text.alignment = alignment; text.color = Color.white;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.enableWordWrapping = true; text.raycastTarget = false;
        return text;
    }

    Button Button(string label, float fontSize, float top, float x, UnityEngine.Events.UnityAction action, float width)
    {
        // Keep both number arrows beside the value, even if their label was imported with bad encoding.
        if (action != null && action.Method != null)
        {
            if (action.Method.Name == nameof(ChangeNumberUp))
            {
                label = "\u25B2"; top = -205f; x = 180f; width = 150f; fontSize = 30f;
            }
            else if (action.Method.Name == nameof(ChangeNumberDown))
            {
                label = "\u25BC"; top = -305f; x = 180f; width = 150f; fontSize = 30f;
            }
        }
        // Repair the two legacy arrow labels if the source file was imported with UTF-8 mojibake.
        if (label.Length == 3 && label[0] == '\u00e2' && label[1] == '\u2013')
        {
            bool up = label[2] == '\u00b2';
            label = up ? "\u25B2" : "\u25BC";
            top = up ? -275f : -375f;
            x = 0f;
            width = 150f;
            fontSize = 30f;
        }
        GameObject obj = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f); rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(x, top); rect.sizeDelta = new Vector2(width, 90);
        Image image = obj.GetComponent<Image>(); image.color = new Color(.07f, .24f, .37f, 1f);
        Button button = obj.GetComponent<Button>(); button.onClick.AddListener(action);
        ColorBlock colors = button.colors; colors.highlightedColor = new Color(.12f, .65f, .9f); colors.pressedColor = new Color(.04f, .75f, 1f); button.colors = colors;
        TMP_Text text = TextOnButton(label, obj.transform, fontSize);
        return button;
    }

    static TMP_Text TextOnButton(string value, Transform parent, float size)
    {
        GameObject obj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        TMP_Text text = obj.GetComponent<TMP_Text>(); text.text = value; text.fontSize = size; text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.fontStyle = FontStyles.Bold; text.raycastTarget = false; return text;
    }
}
