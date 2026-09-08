using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public sealed class FloodScenarioQuizPanel : MonoBehaviour
{
    static readonly string[] Answers =
    {
        "A) Path A, because shorter pathways generally transport floodwater more quickly.",
        "B) Path B, because the continuous low-elevation corridor provides a more plausible flood pathway despite its greater length.",
        "C) Both pathways should be considered equally because distance and elevation compensate for each other.",
        "D) Path A, because water accumulating on elevated terrain creates a stronger downstream flood wave."
    };
    readonly Button[] answerButtons = new Button[4];
    GameObject questionGroup, confidenceGroup, feedbackGroup;
    Slider confidenceSlider;
    TMP_Text confidenceValue;
    int selectedAnswer = -1;
    Action repeatScenario;
    Action<int> answerSubmitted;
    bool confidenceOnly;

    public static FloodScenarioQuizPanel Show(Action onRepeatScenario, Action<int> onAnswerSubmitted)
    {
        GameObject root = new GameObject("Quiz_A1_P1", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        FloodScenarioQuizPanel panel = root.AddComponent<FloodScenarioQuizPanel>();
        panel.repeatScenario = onRepeatScenario;
        panel.answerSubmitted = onAnswerSubmitted;
        panel.Build();
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
        return panel;
    }

    public static FloodScenarioQuizPanel ShowConfidence(int answer)
    {
        GameObject root = new GameObject("Confidence_A1_P1", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        FloodScenarioQuizPanel panel = root.AddComponent<FloodScenarioQuizPanel>();
        panel.selectedAnswer = answer;
        panel.confidenceOnly = true;
        panel.Build();
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
        return panel;
    }

    void Build()
    {
        GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        GetComponent<Canvas>().sortingOrder = 200;
        RectTransform root = (RectTransform)transform;
        root.sizeDelta = new Vector2(1450, 1020);
        root.localScale = Vector3.one * .0015f;
        gameObject.AddComponent<Image>().color = new Color(.025f, .055f, .09f, .98f);
        FloodLearningUI.Text("Title", transform, "A1 - P1  |  Dominant Low Corridor", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);

        questionGroup = FloodLearningUI.Group("QuestionGroup", transform);
        FloodLearningUI.Text("Question", questionGroup.transform, "Which pathway should receive greater attention when planning flood protection?", 34, FontStyles.Bold, -110, 90, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < Answers.Length; i++) answerButtons[i] = CreateAnswerButton(i, -225 - i * 125);
        TMP_Text hint = FloodLearningUI.Text("Hint", questionGroup.transform, "Point at an answer and confirm it with the controller trigger.", 27, FontStyles.Normal, -750, 55, TextAlignmentOptions.Center, 55);
        hint.color = new Color(.72f, .86f, 1);
        FloodLearningUI.Button("RepeatScenario", questionGroup.transform, "Repeat scenario", -840, 82, 29, RepeatScenario, 390);
        BuildConfidenceStep();
        BuildFeedbackStep();
        questionGroup.SetActive(!confidenceOnly);
        confidenceGroup.SetActive(confidenceOnly);
        feedbackGroup.SetActive(false);
        if (confidenceOnly) UpdateConfidenceText(confidenceSlider.value);
    }

    Button CreateAnswerButton(int index, float top) => FloodLearningUI.Button($"Answer_{(char)('A' + index)}", questionGroup.transform, Answers[index], top, 102, 27, () => SelectAnswer(index));

    void BuildConfidenceStep()
    {
        confidenceGroup = FloodLearningUI.Group("ConfidenceGroup", transform);
        FloodLearningUI.Text("ConfidenceQuestion", confidenceGroup.transform, "How confident are you in your answer?", 42, FontStyles.Bold, -150, 70, TextAlignmentOptions.Center, 80);
        confidenceValue = FloodLearningUI.Text("ConfidenceValue", confidenceGroup.transform, "60% - Moderately confident", 38, FontStyles.Bold, -270, 65, TextAlignmentOptions.Center, 100);
        confidenceSlider = FloodLearningUI.Slider("ConfidenceSlider", confidenceGroup.transform, -390);
        confidenceSlider.minValue = 0; confidenceSlider.maxValue = 5; confidenceSlider.wholeNumbers = true; confidenceSlider.value = 3;
        confidenceSlider.onValueChanged.AddListener(UpdateConfidenceText);
        FloodLearningUI.Text("Ticks", confidenceGroup.transform, "0                 20                 40                 60                 80                100", 25, FontStyles.Normal, -455, 45, TextAlignmentOptions.Center, 170);
        TMP_Text scale = FloodLearningUI.Text("ScaleLabels", confidenceGroup.transform, "Not confident                                      Moderately confident                                      Very confident", 23, FontStyles.Normal, -510, 55, TextAlignmentOptions.Center, 130);
        scale.color = new Color(.75f, .85f, .95f);
        FloodLearningUI.Button("ConfirmConfidence", confidenceGroup.transform, "Confirm confidence", -650, 95, 31, ConfirmConfidence, 420);
    }

    void BuildFeedbackStep()
    {
        feedbackGroup = FloodLearningUI.Group("FeedbackGroup", transform);
        FloodLearningUI.Text("Result", feedbackGroup.transform, "", 39, FontStyles.Bold, -120, 62, TextAlignmentOptions.Center, 65);
        TMP_Text correct = FloodLearningUI.Text("CorrectAnswer", feedbackGroup.transform, "Correct answer: B) Path B, because the continuous low-elevation corridor provides a more plausible flood pathway despite its greater length.", 31, FontStyles.Bold, -220, 135, TextAlignmentOptions.TopLeft, 70);
        correct.color = new Color(.4f, 1, .55f);
        FloodLearningUI.Text("ExplanationTitle", feedbackGroup.transform, "Explanation", 34, FontStyles.Bold, -395, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", feedbackGroup.transform, "Floodwater movement is strongly influenced by terrain connectivity and elevation. A longer but continuously low-lying corridor can provide a more plausible route than a shorter route that requires water to cross substantially higher terrain. Straight-line distance alone is therefore not sufficient for identifying the dominant flood pathway.", 29, FontStyles.Normal, -465, 260, TextAlignmentOptions.TopLeft, 70);
    }

    void SelectAnswer(int index)
    {
        if (selectedAnswer >= 0) return;
        selectedAnswer = index;
        foreach (Button button in answerButtons) button.interactable = false;
        if (answerSubmitted != null)
        {
            Action<int> callback = answerSubmitted;
            answerSubmitted = null;
            callback.Invoke(index);
            Destroy(gameObject);
            return;
        }
        questionGroup.SetActive(false);
        confidenceGroup.SetActive(true);
        UpdateConfidenceText(confidenceSlider.value);
    }

    void RepeatScenario()
    {
        if (selectedAnswer >= 0 || repeatScenario == null) return;
        Action callback = repeatScenario;
        repeatScenario = null;
        callback.Invoke();
        Destroy(gameObject);
    }

    void UpdateConfidenceText(float value)
    {
        string[] labels = { "Not confident", "Low confidence", "Slightly confident", "Moderately confident", "Confident", "Very confident" };
        int step = Mathf.Clamp(Mathf.RoundToInt(value), 0, 5);
        confidenceValue.text = $"{step * 20}% - {labels[step]}";
    }

    void ConfirmConfidence()
    {
        int confidence = Mathf.RoundToInt(confidenceSlider.value) * 20;
        confidenceGroup.SetActive(false);
        feedbackGroup.SetActive(true);
        TMP_Text result = feedbackGroup.transform.Find("Result").GetComponent<TMP_Text>();
        result.text = selectedAnswer == 1 ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + selectedAnswer)}  |  Confidence: {confidence}%";
        result.color = selectedAnswer == 1 ? new Color(.4f, 1, .55f) : new Color(1, .68f, .35f);
        Debug.Log($"A1-P1 result: answer={(char)('A' + selectedAnswer)}, correct={selectedAnswer == 1}, confidence={confidence}");
    }
}

static class FloodLearningUI
{
    public static GameObject Group(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
        RectTransform r = (RectTransform)obj.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        return obj;
    }

    public static TMP_Text Text(string name, Transform parent, string value, float size, FontStyles style, float top, float height, TextAlignmentOptions alignment, float margin)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(parent, false);
        RectTransform r = (RectTransform)obj.transform; r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(.5f, 1); r.anchoredPosition = new Vector2(0, top); r.sizeDelta = new Vector2(-2 * margin, height);
        TMP_Text text = obj.GetComponent<TMP_Text>(); text.text = value; text.fontSize = size; text.fontStyle = style; text.alignment = alignment; text.color = Color.white; text.enableWordWrapping = true; text.raycastTarget = false;
        return text;
    }

    public static Button Button(string name, Transform parent, string label, float top, float height, float fontSize, UnityEngine.Events.UnityAction action, float width = -1)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
        RectTransform r = (RectTransform)obj.transform; r.anchorMin = r.anchorMax = new Vector2(.5f, 1); r.pivot = new Vector2(.5f, 1); r.anchoredPosition = new Vector2(0, top); r.sizeDelta = new Vector2(width < 0 ? 1340 : width, height);
        Image image = obj.GetComponent<Image>(); image.color = new Color(.07f, .24f, .37f, 1);
        Button button = obj.GetComponent<Button>(); ColorBlock colors = button.colors; colors.highlightedColor = new Color(.08f, .46f, .68f, 1); colors.pressedColor = new Color(.04f, .64f, .88f, 1); button.colors = colors; button.onClick.AddListener(action);
        TMP_Text text = Text("Label", obj.transform, label, fontSize, FontStyles.Normal, -8, height - 16, TextAlignmentOptions.MidlineLeft, 25); text.enableAutoSizing = true; text.fontSizeMin = 20; text.fontSizeMax = fontSize;
        return button;
    }

    public static Slider Slider(string name, Transform parent, float top)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Slider)); obj.transform.SetParent(parent, false);
        RectTransform r = (RectTransform)obj.transform; r.anchorMin = r.anchorMax = new Vector2(.5f, 1); r.pivot = new Vector2(.5f, 1); r.anchoredPosition = new Vector2(0, top); r.sizeDelta = new Vector2(1050, 55);
        SliderImage("Background", obj.transform, new Color(.14f, .2f, .25f), 18);
        RectTransform fillArea = SliderRect("Fill Area", obj.transform, 25, -25); Image fill = SliderImage("Fill", fillArea, new Color(.1f, .65f, .92f), 18);
        RectTransform handleArea = SliderRect("Handle Slide Area", obj.transform, 22, -22); Image handle = SliderImage("Handle", handleArea, Color.white, 42); RectTransform handleRect = (RectTransform)handle.transform; handleRect.anchorMin = handleRect.anchorMax = new Vector2(0, .5f); handleRect.sizeDelta = new Vector2(42, 42);
        UnityEngine.UI.Slider slider = obj.GetComponent<UnityEngine.UI.Slider>(); slider.fillRect = (RectTransform)fill.transform; slider.handleRect = handleRect; slider.targetGraphic = handle; slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        return slider;
    }

    static RectTransform SliderRect(string name, Transform parent, float left, float right)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false); RectTransform r = (RectTransform)obj.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left, 0); r.offsetMax = new Vector2(right, 0); return r;
    }

    static Image SliderImage(string name, Transform parent, Color color, float height)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false); RectTransform r = (RectTransform)obj.transform; r.anchorMin = new Vector2(0, .5f); r.anchorMax = new Vector2(1, .5f); r.sizeDelta = new Vector2(0, height); Image image = obj.GetComponent<Image>(); image.color = color; return image;
    }

    public static void PlaceInFrontOfPlayer(Transform target, float distance)
    {
        Camera camera = Camera.main; if (camera == null) return; Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized; if (forward.sqrMagnitude < .1f) forward = camera.transform.forward; target.position = camera.transform.position + forward * distance; target.rotation = Quaternion.LookRotation(target.position - camera.transform.position, Vector3.up);
    }
}

sealed class FloodScenarioContinuePrompt : MonoBehaviour
{
    public static GameObject Show()
    {
        GameObject root = new GameObject("A1_P1_ContinuePrompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform r = (RectTransform)root.transform; r.sizeDelta = new Vector2(900, 330); r.localScale = Vector3.one * .0015f; root.AddComponent<Image>().color = new Color(.025f, .055f, .09f, .96f);
        FloodLearningUI.Text("Title", root.transform, "A1 - P1", 39, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Scenario", root.transform, "Dominant Low Corridor", 43, FontStyles.Bold, -105, 65, TextAlignmentOptions.Center, 45);
        TMP_Text prompt = FloodLearningUI.Text("Prompt", root.transform, "Press A to continue", 35, FontStyles.Normal, -210, 55, TextAlignmentOptions.Center, 45); prompt.color = new Color(.35f, .82f, 1);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f); return root;
    }

    public static GameObject ShowDykeBuilding()
    {
        GameObject root = new GameObject("A1_P1_DykeBuildingPrompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform r = (RectTransform)root.transform; r.sizeDelta = new Vector2(1050, 390); r.localScale = Vector3.one * .0015f; root.AddComponent<Image>().color = new Color(.025f, .055f, .09f, .96f);
        FloodLearningUI.Text("Title", root.transform, "A1 - P1  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Instructions", root.transform, "Build dykes between the snap points with the trigger button.", 33, FontStyles.Normal, -125, 90, TextAlignmentOptions.Center, 55);
        TMP_Text prompt = FloodLearningUI.Text("Prompt", root.transform,
            "Build dykes anywhere with the trigger. Point at one and press Grip to destroy it.\nPress A when you are finished to run the flood simulation.",
            29, FontStyles.Bold, -215, 105, TextAlignmentOptions.Center, 55); prompt.color = new Color(.35f, .82f, 1);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f); return root;
    }
}
