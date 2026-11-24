// Standalone test doubles, outside Assets. Production UI/input code is compiled
// unchanged; these replace engine services unavailable without the editor.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { }
    public class Component : Object
    {
        public GameObject gameObject;
        public string name => gameObject.name;
        public Transform transform => gameObject.transform;
        public T GetComponentInParent<T>() where T : class => gameObject.GetComponentInParent<T>();
        public T GetComponentInChildren<T>(bool include) where T : class => gameObject.GetComponentInChildren<T>(include);
    }
    public class GameObject : Object
    {
        public string name;
        public bool activeInHierarchy = true;
        public Transform transform;
        public Canvas canvas;
        public UnityEngine.UI.Selectable control;
        public TMPro.TMP_Text label;
        public Scene scene;
        public GameObject(string name) { this.name = name; transform = new RectTransform { gameObject = this }; }
        public T GetComponentInParent<T>() where T : class => typeof(T) == typeof(Canvas) ? canvas as T : control as T;
        public T GetComponentInChildren<T>(bool include) where T : class => label as T;
    }
    public struct Scene { public bool IsValid() => true; }
    public class Transform : Component { public Transform parent; public Vector3 position; public Vector3 right = Vector3.right, up = Vector3.up; }
    public class RectTransform : Transform { }
    public class Canvas : Component { public int sortingOrder; }
    public struct Color { public Color(float r, float g, float b, float a = 1) { } }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2();
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
    public static class Mathf
    {
        public static float Abs(float v) => Math.Abs(v);
        public static float Sign(float v) => Math.Sign(v);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp(float v, float a, float b) => Math.Max(a, Math.Min(v, b));
    }
    public static class Time { public static int frameCount = 1; public static float unscaledTime; }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() => new T[0]; }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
}
namespace TMPro
{
    public class TMP_Text : UnityEngine.Component { public string text = ""; public UnityEngine.Color color; }
    public enum FontStyles { Normal }
    public enum TextAlignmentOptions { Center }
}
namespace UnityEngine.UI
{
    public class Selectable : UnityEngine.Component
    {
        public static readonly List<Selectable> All = new List<Selectable>();
        public static Selectable[] allSelectablesArray => All.ToArray();
        public bool isActiveAndEnabled = true, interactable = true;
        public bool IsInteractable() => interactable;
        public void Select() => UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(gameObject);
    }
    public class Button : Selectable { public Action Click; }
    public class Slider : Selectable { public float value, minValue, maxValue; public bool wholeNumbers; }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem : UnityEngine.Object
    {
        public static EventSystem current = new EventSystem();
        public UnityEngine.GameObject currentSelectedGameObject;
        public bool sendNavigationEvents = true;
        public void SetSelectedGameObject(UnityEngine.GameObject go) => currentSelectedGameObject = go;
    }
    public class BaseEventData { public BaseEventData(EventSystem system) { } }
    public static class ExecuteEvents
    {
        public static readonly object submitHandler = new object();
        public static void Execute(UnityEngine.GameObject go, BaseEventData data, object handler) => (go.control as UnityEngine.UI.Button)?.Click?.Invoke();
    }
}
namespace UnityEngine.InputSystem
{
    public enum InputActionType { Button, Value }
    public class InputAction
    {
        public static readonly Dictionary<string, InputAction> All = new Dictionary<string, InputAction>();
        public bool enabled, Pressed;
        public UnityEngine.Vector2 Stick;
        public string binding;
        public InputAction(string name, InputActionType type, string path) { binding = path; All[name] = this; }
        public void Enable() => enabled = true;
        public void Disable() => enabled = false;
        public void Dispose() { }
        public bool WasPressedThisFrame() => enabled && Pressed;
        public bool IsPressed() => enabled && Pressed;
        public T ReadValue<T>() => (T)(object)Stick;
    }
}
namespace UnityEngine.XR.Interaction.Toolkit.Inputs.Readers
{
    public class XRInputButtonReader
    {
        public enum InputSourceMode { InputAction }
        public InputSourceMode inputSourceMode;
        public UnityEngine.InputSystem.InputAction inputActionPerformed, inputActionValue;
    }
}
public enum ParticipantInteractionMode { Controller, Gamepad, Virtual_Hands }
public static class ParticipantSession { public static bool IsCheckedIn = true; public static ParticipantInteractionMode InteractionMode = ParticipantInteractionMode.Gamepad; }
public class VirtualHandsPinchInteractor { public static VirtualHandsPinchInteractor Active; public bool PinchStartedThisFrame, IsPinching; }
public static class FloodLearningUI
{
    public static readonly List<TMPro.TMP_Text> Texts = new List<TMPro.TMP_Text>();
    public static UnityEngine.UI.Button Button(string name, UnityEngine.Transform parent, string label, float top, float height, float size, Action action, float width)
    {
        var go = new UnityEngine.GameObject(name) { canvas = parent.gameObject.canvas };
        var button = new UnityEngine.UI.Button { gameObject = go, Click = action };
        go.control = button;
        go.label = new TMPro.TMP_Text { text = label };
        UnityEngine.UI.Selectable.All.Add(button);
        return button;
    }
    public static TMPro.TMP_Text Text(string name, UnityEngine.Transform parent, string value, float size, TMPro.FontStyles style, float top, float height, TMPro.TextAlignmentOptions align, float margin)
    {
        var text = new TMPro.TMP_Text { text = value };
        Texts.Add(text);
        return text;
    }
}
