using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Provides the right-hand pinch ray used by the virtual-hands condition.
/// The ray is an ordinary XR Ray Interactor, so the existing world-space
/// canvases and buttons continue to use the same UI event system as the
/// controller condition. Pinch state is fed into XRI's manual input reader.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class VirtualHandsPinchInteractor : MonoBehaviour
{
    [Header("Rig (resolved automatically when empty)")]
    [SerializeField] OVRCameraRig cameraRig;
    [SerializeField] OVRHand rightHand;

    [Header("Ray")]
    [SerializeField] Transform rayOrigin;
    [SerializeField, Min(0.5f)] float rayLength = 12f;
    [SerializeField, Min(0.0005f)] float rayWidth = 0.0035f;
    [SerializeField] Color rayColor = new Color(0f, 1f, 1f, 1f);
    [SerializeField] Color rayEndColor = new Color(0f, 0.75f, 1f, 0.75f);

    [Header("Wrist ray origin")]
    [Tooltip("Local offset from the tracked right wrist. Z points toward the fingers; Y lowers the ray toward the arm.")]
    [SerializeField] Vector3 wristRayLocalOffset = new Vector3(0f, -0.035f, 0.06f);
    [SerializeField] Vector3 wristRayLocalEulerAngles;

    XRRayInteractor rayInteractor;
    XRInputButtonReader pinchReader;
    XRInteractorLineVisual lineVisual;
    Material rayMaterial;
    bool rayOriginCreatedByScript;
    bool previousPinching;
    bool warnedMissingHand;

    public static VirtualHandsPinchInteractor Active { get; private set; }
    public XRRayInteractor RayInteractor => rayInteractor;
    public Transform RayOrigin => rayOrigin;
    public bool IsPinching { get; private set; }
    public bool PinchStartedThisFrame { get; private set; }
    public bool PinchReleasedThisFrame { get; private set; }

    void Awake()
    {
        Active = this;

        if (cameraRig == null)
            cameraRig = GetComponentInChildren<OVRCameraRig>(true);
        if (cameraRig == null)
            cameraRig = FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);

        ResolveRightHand();

        if (rayOrigin == null)
        {
            // The virtual rig's rightHandAnchor is a right-hand pose even
            // before hand tracking has reported an OVRHand. Create the ray
            // there immediately so the visual is never missing, then move
            // it to the tracked wrist as soon as that OVRHand is available.
            Transform wrist = rightHand != null ? rightHand.transform : cameraRig != null ? cameraRig.rightHandAnchor : transform;
            if (wrist != null)
            {
                GameObject wristOrigin = new GameObject("Virtual Hands Wrist Ray Origin");
                wristOrigin.transform.SetParent(wrist, false);
                wristOrigin.transform.localPosition = rightHand != null ? wristRayLocalOffset : Vector3.zero;
                wristOrigin.transform.localRotation = Quaternion.Euler(wristRayLocalEulerAngles);
                rayOrigin = wristOrigin.transform;
                rayOriginCreatedByScript = true;
            }
        }

        GameObject rayObject = new GameObject("Virtual Hands Pinch Ray");
        rayObject.transform.SetParent(transform, false);
        if (FindFirstObjectByType<XRInteractionManager>(FindObjectsInactive.Include) == null)
        {
            GameObject managerObject = new GameObject("Virtual Hands XR Interaction Manager");
            managerObject.AddComponent<XRInteractionManager>();
        }
        rayInteractor = rayObject.AddComponent<XRRayInteractor>();
        rayInteractor.lineType = XRRayInteractor.LineType.StraightLine;
        rayInteractor.maxRaycastDistance = rayLength;
        rayInteractor.rayOriginTransform = rayOrigin;
        rayInteractor.enableUIInteraction = true;
        rayInteractor.allowHover = true;

        pinchReader = new XRInputButtonReader(
            "Virtual Hands Pinch", "Virtual Hands Pinch Value", false,
            XRInputButtonReader.InputSourceMode.ManualValue);
        rayInteractor.selectInput = pinchReader;
        rayInteractor.uiPressInput = pinchReader;

        lineVisual = rayObject.AddComponent<XRInteractorLineVisual>();
        lineVisual.overrideInteractorLineLength = true;
        lineVisual.lineLength = rayLength;
        lineVisual.lineWidth = rayWidth;
        lineVisual.lineOriginTransform = rayOrigin;
        ConfigureRayRenderer(rayObject.GetComponent<LineRenderer>());
        ApplyRayColor();
        lineVisual.enabled = true;
    }

    void ResolveRightHand()
    {
        if (rightHand != null)
            return;

        if (cameraRig != null)
        {
            foreach (OVRHand hand in cameraRig.GetComponentsInChildren<OVRHand>(true))
            {
                if (hand.GetHand() == OVRPlugin.Hand.HandRight)
                {
                    rightHand = hand;
                    break;
                }
            }
        }

        if (rightHand == null)
        {
            foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hand != null && hand.GetHand() == OVRPlugin.Hand.HandRight)
                {
                    rightHand = hand;
                    break;
                }
            }
        }

        if (rightHand != null && rayOrigin == null)
        {
            GameObject wristOrigin = new GameObject("Virtual Hands Wrist Ray Origin");
            wristOrigin.transform.SetParent(rightHand.transform, false);
            wristOrigin.transform.localPosition = wristRayLocalOffset;
            wristOrigin.transform.localRotation = Quaternion.Euler(wristRayLocalEulerAngles);
            rayOrigin = wristOrigin.transform;
            rayOriginCreatedByScript = true;
        }
        else if (rightHand != null && rayOriginCreatedByScript)
        {
            rayOrigin.SetParent(rightHand.transform, false);
            rayOrigin.localPosition = wristRayLocalOffset;
            rayOrigin.localRotation = Quaternion.Euler(wristRayLocalEulerAngles);
        }
    }

    void ConfigureRayRenderer(LineRenderer renderer)
    {
        if (renderer == null)
            return;

        renderer.useWorldSpace = true;
        renderer.widthMultiplier = rayWidth;
        renderer.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        // A runtime-created LineRenderer has no guaranteed URP material. In
        // URP that can render magenta when the fallback shader is missing, so
        // explicitly use a shader that is available in the project.
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Sprites/Default") ??
                        Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        rayMaterial = new Material(shader)
        {
            name = "Virtual Hands Ray Cyan Material"
        };
        if (rayMaterial.HasProperty("_BaseColor"))
            rayMaterial.SetColor("_BaseColor", Color.white);
        if (rayMaterial.HasProperty("_Color"))
            rayMaterial.SetColor("_Color", Color.white);
        renderer.sharedMaterial = rayMaterial;
    }

    void ApplyRayColor()
    {
        if (lineVisual == null)
            return;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(rayColor, 0f),
                new GradientColorKey(rayEndColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(rayColor.a, 0f),
                new GradientAlphaKey(rayEndColor.a, 1f)
            });

        lineVisual.setLineColorGradient = true;
        lineVisual.validColorGradient = gradient;
        lineVisual.invalidColorGradient = gradient;
        lineVisual.blockedColorGradient = gradient;

        LineRenderer renderer = lineVisual.GetComponent<LineRenderer>();
        if (renderer != null)
        {
            renderer.colorGradient = gradient;
            renderer.startColor = rayColor;
            renderer.endColor = rayEndColor;
        }
    }

    void OnEnable()
    {
        Active = this;
        if (rayInteractor != null)
            rayInteractor.enabled = true;
        if (lineVisual != null)
            lineVisual.enabled = true;
    }

    void Update()
    {
        ResolveRightHand();

        bool pinching = rightHand != null && rightHand.isActiveAndEnabled &&
            rightHand.IsTracked && rightHand.IsDataHighConfidence &&
            rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

        if (rightHand == null && !warnedMissingHand)
        {
            Debug.LogWarning("Virtual hands: no right OVRHand was found below the virtual rig.", this);
            warnedMissingHand = true;
        }

        IsPinching = pinching;
        PinchStartedThisFrame = pinching && !previousPinching;
        PinchReleasedThisFrame = !pinching && previousPinching;
        previousPinching = pinching;

        if (pinchReader != null)
            pinchReader.QueueManualState(pinching, pinching ? 1f : 0f);

        if (rayInteractor != null)
        {
            rayInteractor.maxRaycastDistance = rayLength;
            rayInteractor.rayOriginTransform = rayOrigin;
        }

        if (lineVisual != null)
        {
            lineVisual.lineLength = rayLength;
            lineVisual.lineWidth = rayWidth;
            lineVisual.lineOriginTransform = rayOrigin;
        }
    }

    void LateUpdate()
    {
        // PlayMaker can restore the authored tutorial wording whenever a
        // panel enters a state. Reapply the virtual-hands wording after that
        // state transition, just like the Gamepad condition does.
        GamepadUIInput.RefreshTutorialInstructions();
    }

    public bool TryGetCurrent3DRaycastHit(out RaycastHit hit)
    {
        if (rayInteractor != null)
            return rayInteractor.TryGetCurrent3DRaycastHit(out hit);

        hit = default;
        return false;
    }

    void OnDisable()
    {
        IsPinching = false;
        PinchStartedThisFrame = false;
        PinchReleasedThisFrame = false;
        previousPinching = false;
        if (rayInteractor != null)
            rayInteractor.enabled = false;
        if (lineVisual != null)
            lineVisual.enabled = false;
        if (Active == this)
            Active = null;
    }

    void OnDestroy()
    {
        if (rayMaterial != null)
            Destroy(rayMaterial);
        if (Active == this)
            Active = null;
    }

    void OnValidate()
    {
        rayLength = Mathf.Max(0.5f, rayLength);
        rayWidth = Mathf.Max(0.0005f, rayWidth);
    }
}
