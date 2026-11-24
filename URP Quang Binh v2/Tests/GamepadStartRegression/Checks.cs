using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public static class Checks
{
    static int count;
    static EventSystem System => EventSystem.current;
    static void Assert(bool value, string text) { if (!value) throw new Exception(text); count++; }
    static Canvas Panel(string name)
    {
        var go = new GameObject(name);
        var canvas = new Canvas { gameObject = go };
        go.canvas = canvas;
        return canvas;
    }
    static T Control<T>(string name, Canvas canvas) where T : Selectable, new()
    {
        var go = new GameObject(name) { canvas = canvas };
        var control = new T { gameObject = go };
        go.control = control;
        Selectable.All.Add(control);
        return control;
    }
    static void Frame()
    {
        Time.frameCount++; Time.unscaledTime++;
        foreach (var input in InputAction.All.Values) { input.Pressed = false; input.Stick = Vector2.zero; }
    }
    static void Press(string name) => InputAction.All[name].Pressed = true;
    static void Clear()
    {
        GamepadUIInput.ResetSelection(); Selectable.All.Clear();
        System.SetSelectedGameObject(null); Frame();
    }
    static void Run()
    {
        ParticipantInput.SetGamepadModeEnabled(true);
        Clear();
        var survey = Panel("General_Discomfort_Survey");
        var slider = Control<Slider>("SicknessSlider", survey);
        var confirm = Control<Button>("Confirm", survey);
        var otherConfirm = Control<Button>("Confirm", Panel("Unrelated"));
        otherConfirm.gameObject.activeInHierarchy = false;
        int confirmed = 0, started = 0;
        Canvas scenario4 = null;
        confirm.Click = () =>
        {
            confirmed++;
            slider.gameObject.activeInHierarchy = false;
            confirm.gameObject.activeInHierarchy = false;
            scenario4 = Panel("A1_P4_Start");
            // Actual shared production helper, extracted by the runner.
            FloodScenarioContinuePrompt.AddStartControl(scenario4.transform, () => started++, -220);
        };
        GamepadUIInput.Tick();
        Assert(System.currentSelectedGameObject == slider.gameObject, "Survey slider was not selected");
        Assert(!System.sendNavigationEvents, "Native Submit/Cancel is still active");
        Frame(); Press("Quest Gamepad Confirm"); GamepadUIInput.Tick();
        Assert(System.currentSelectedGameObject == confirm.gameObject, "A did not select survey Confirm");
        Frame(); System.SetSelectedGameObject(null); GamepadUIInput.Tick();
        Assert(System.currentSelectedGameObject == confirm.gameObject, "Confirm focus was not restored");
        Press("Quest Gamepad Start"); GamepadUIInput.SubmitTutorialStart(System);
        Assert(confirmed == 0 && System.currentSelectedGameObject == confirm.gameObject, "B affected the survey");
        Frame(); Press("Quest Gamepad Confirm"); Press("Quest Gamepad Start"); Press("Quest Gamepad Simulation Start");
        GamepadUIInput.Tick(); GamepadUIInput.Tick();
        Assert(confirmed == 1 && started == 0, "Closing the survey also started the next scenario");
        Assert(!ParticipantInput.GamepadStartWasPressedThisFrame() && !ParticipantInput.SimulationStartWasPressedThisFrame(null), "Phase shortcut escaped confirmation frame");
        GamepadUIInput.MaintainSelectionFocus();
        var start = System.currentSelectedGameObject.control as Button;
        Assert(start != null && start.name == "Continue", "P4 start button did not gain focus");
        Assert(start.gameObject.label.text == "Press A to start", "P4 still advertises B");
        Frame(); Press("Quest Gamepad Start"); GamepadUIInput.SubmitTutorialStart(System);
        Assert(started == 0 && System.currentSelectedGameObject == start.gameObject, "B started or stole focus from P4");
        System.SetSelectedGameObject(null); GamepadUIInput.MaintainSelectionFocus();
        Assert(System.currentSelectedGameObject == start.gameObject, "P4 start focus was lost");
        Frame(); Press("Quest Gamepad Confirm"); GamepadUIInput.Tick(); GamepadUIInput.Tick();
        Assert(started == 1, "A did not start P4 exactly once");

        for (int n = 1; n <= 6; n++)
        {
            Clear(); int launches = 0;
            var panel = Panel("A1_P" + n + "_Start");
            FloodScenarioContinuePrompt.AddStartControl(panel.transform, () => launches++, -220);
            GamepadUIInput.Tick(); Frame(); Press("Quest Gamepad Confirm"); GamepadUIInput.Tick();
            Assert(launches == 1, "A start failed in scenario " + n);
        }

        for (int n = 4; n <= 6; n++)
        {
            Clear(); int finished = 0;
            var oldStart = Control<Button>("StartGameButton", Panel("OldTutorialStart"));
            int wrongStarts = 0; oldStart.Click = () => wrongStarts++;
            var feedback = Panel("Feedback_A1_P" + n);
            var finish = Control<Button>("Finish", feedback);
            finish.gameObject.label = new TMPro.TMP_Text { text = "Finish" };
            finish.Click = () =>
            {
                finished++;
                finish.gameObject.activeInHierarchy = false;
                FloodScenarioContinuePrompt.AddStartControl(Panel("NextScenario").transform, () => { }, -220);
            };
            // Reproduce an EventSystem still pointing at an older start button.
            System.SetSelectedGameObject(oldStart.gameObject);
            GamepadUIInput.Tick();
            Assert(System.currentSelectedGameObject == finish.gameObject, "Finish did not gain focus in scenario " + n);
            Frame(); Press("Quest Gamepad Start"); GamepadUIInput.SubmitTutorialStart(System);
            Assert(finished == 0 && wrongStarts == 0 && System.currentSelectedGameObject == finish.gameObject,
                "B affected Finish in scenario " + n);
            System.SetSelectedGameObject(null); GamepadUIInput.MaintainSelectionFocus();
            Assert(System.currentSelectedGameObject == finish.gameObject, "Lost Finish focus in scenario " + n);
            Frame(); Press("Quest Gamepad Confirm"); GamepadUIInput.Tick(); GamepadUIInput.Tick();
            Assert(finished == 1, "A did not submit Finish exactly once in scenario " + n);
            GamepadUIInput.MaintainSelectionFocus();
            Assert(System.currentSelectedGameObject != null && System.currentSelectedGameObject.name == "Continue",
                "Focus did not transfer after Finish in scenario " + n);
        }

        Clear();
        var question = Panel("Quiz_A1_P4");
        var answerA = Control<Button>("Answer_A", question);
        var answerB = Control<Button>("Answer_B", question);
        GamepadUIInput.Tick(); Frame();
        InputAction.All["Quest Gamepad UI Navigation"].Stick = new Vector2(0, -1);
        GamepadUIInput.Tick();
        Assert(System.currentSelectedGameObject == answerB.gameObject, "Answer navigation failed");
        Frame(); Press("Quest Gamepad Start"); GamepadUIInput.SubmitTutorialStart(System);
        Assert(System.currentSelectedGameObject == answerB.gameObject, "B changed answer selection");
        System.SetSelectedGameObject(answerA.gameObject); GamepadUIInput.MaintainSelectionFocus();
        Assert(System.currentSelectedGameObject == answerB.gameObject, "External focus change was not repaired");
        Clear(); Assert(System.sendNavigationEvents, "Navigation ownership was not released");
        Frame(); Press("Quest Gamepad Simulation Start");
        Assert(ParticipantInput.SimulationStartWasPressedThisFrame(null), "Y no longer starts simulation outside UI");

        ParticipantSession.InteractionMode = ParticipantInteractionMode.Virtual_Hands;
        VirtualHandsPinchInteractor.Active = new VirtualHandsPinchInteractor { PinchStartedThisFrame = true };
        Assert(ParticipantInput.StartWasPressedThisFrame(null), "Virtual Hands pinch start changed");
        int controlsBefore = Selectable.All.Count;
        FloodScenarioContinuePrompt.AddStartControl(Panel("Hands").transform, () => { }, -220);
        Assert(Selectable.All.Count == controlsBefore && FloodLearningUI.Texts[FloodLearningUI.Texts.Count - 1].text == "Pinch to continue", "Virtual Hands prompt changed");
        ParticipantSession.InteractionMode = ParticipantInteractionMode.Controller;
        var questA = new InputAction("Controller A", InputActionType.Button, "A"); questA.Enable(); questA.Pressed = true;
        Assert(ParticipantInput.StartWasPressedThisFrame(questA), "Controller A changed");
    }
    public static int Main()
    {
        try { Run(); Console.WriteLine("PASS: " + count + " Gamepad start/focus regression checks."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
