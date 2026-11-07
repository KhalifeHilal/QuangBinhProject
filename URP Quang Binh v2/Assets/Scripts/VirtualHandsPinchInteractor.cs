using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
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
    [SerializeField] OVRSkeleton rightSkeleton;

    [Header("Ray")]
    [SerializeField] Transform rayOrigin;
    [SerializeField, Min(0.5f)] float rayLength = 10f;
    [SerializeField, Min(0.0005f)] float rayWidth = 0.005f;
    [SerializeField] Color rayColor = new Color(0f, 1f, 1f, 1f);
    [SerializeField] Color rayEndColor = new Color(1f, 1f, 1f, 0f);

    [Header("Wrist ray origin")]
    [Tooltip("Local offset from the tracked right wrist. Z moves into the palm toward the space between thumb and index finger; Y lowers the ray slightly toward the arm.")]
    [SerializeField] Vector3 wristRayLocalOffset = new Vector3(-0.025f, -0.055f, 0.095f);
    [SerializeField] Vector3 wristRayLocalEulerAngles;

    XRRayInteractor rayInteractor;
    XRInputButtonReader pinchReader;
    XRInteractorLineVisual lineVisual;
    LineRenderer visibleRayRenderer;
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

        ResolveCameraRig();
        ResolveRightHand();
        EnsureRayOrigin();

        GameObject rayObject = new GameObject("Virtual Hands Pinch Ray");
        rayObject.transform.SetParent(transform, false);
        XRInteractionManager interactionManager =
            FindFirstObjectByType<XRInteractionManager>(FindObjectsInactive.Include);
        if (interactionManager == null || !interactionManager.isActiveAndEnabled)
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

        // XRI's line visual can hide itself while the hand/provider is still
        // initializing. Keep a separate renderer for a guaranteed visible
        // cyan ray while XRRayInteractor still handles all hit testing.
        GameObject visibleRay = new GameObject("Virtual Hands Visible Ray");
        visibleRay.transform.SetParent(transform, false);
        visibleRayRenderer = visibleRay.AddComponent<LineRenderer>();
        ConfigureRayRenderer(visibleRayRenderer);
        visibleRayRenderer.positionCount = 2;
        visibleRayRenderer.enabled = true;
        ApplyRayColor();
        // Use the same XRI line visual as the controller ray. The separate
        // renderer remains as a fallback for providers that hide XRI visuals
        // during hand-tracking startup.
        lineVisual.enabled = true;

        Debug.Log($"Virtual hands ray ready. Origin: {(rayOrigin != null ? rayOrigin.name : "fallback")}, length: {rayLength:0.##} m", this);
    }

    void ResolveRightHand()
    {
        ResolveCameraRig();

        if (rightHand == null && cameraRig != null)
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
            foreach (OVRHand hand in GetComponentsInChildren<OVRHand>(true))
            {
                if (hand != null && hand.GetHand() == OVRPlugin.Hand.HandRight)
                {
                    rightHand = hand;
                    break;
                }
            }
        }

        if (rightHand != null && rightSkeleton == null)
        {
            rightSkeleton = rightHand.GetComponentInChildren<OVRSkeleton>(true);

            if (rightSkeleton == null)
            {
                OVRSkeleton[] skeletons = GetComponentsInChildren<OVRSkeleton>(true);
                for (int i = 0; i < skeletons.Length; i++)
                {
                    OVRSkeleton skeleton = skeletons[i];
                    if (skeleton != null && skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.HandRight)
                    {
                        rightSkeleton = skeleton;
                        break;
                    }
                }
            }
        }
    }

    void ResolveCameraRig()
    {
        // Resolve only inside this VR_Hand_Rig. Falling back to a global
        // OVRCameraRig could accidentally attach the ray to the controller
        // rig that is used for check-in.
        if (cameraRig == null)
            cameraRig = GetComponentInChildren<OVRCameraRig>(true);
    }

    void EnsureRayOrigin()
    {
        if (rayOrigin != null && !rayOriginCreatedByScript)
            return;

        Transform trackedWrist = FindRightWristTransform();
        Transform wrist = trackedWrist;
        if (wrist == null)
            // The OVRCameraRig hand anchor is the correct tracked fallback
            // while the hand skeleton is still initializing. The OVRHand
            // provider object itself can sit at tracking-space origin.
            wrist = cameraRig != null ? cameraRig.rightHandAnchor :
                rightHand != null ? rightHand.transform : transform;

        if (wrist == null)
            return;

        if (rayOrigin == null)
        {
            GameObject wristOrigin = new GameObject("VR Hand Ray Origin");
            rayOrigin = wristOrigin.transform;
            rayOriginCreatedByScript = true;
        }

        if (rayOrigin.parent != wrist)
            rayOrigin.SetParent(wrist, false);

        bool usingCameraAnchor = trackedWrist == null && cameraRig != null && wrist == cameraRig.rightHandAnchor;
        rayOrigin.localPosition = usingCameraAnchor ? Vector3.zero : wristRayLocalOffset;
        rayOrigin.localRotation = usingCameraAnchor ? Quaternion.identity : Quaternion.Euler(wristRayLocalEulerAngles);
    }

    Transform FindRightWristTransform()
    {
        if (rightSkeleton == null || rightSkeleton.Bones == null)
            return null;

        for (int i = 0; i < rightSkeleton.Bones.Count; i++)
        {
            OVRBone bone = rightSkeleton.Bones[i];
            if (bone != null && bone.Id == OVRSkeleton.BoneId.Hand_WristRoot && bone.Transform != null)
                return bone.Transform;
        }

        return null;
    }

    void ResolveRightWristIfAvailable()
    {
        if (rightHand == null)
            return;

        if (rightSkeleton == null)
            ResolveRightHand();

        Transform wrist = FindRightWristTransform();
        if (wrist != null && rayOriginCreatedByScript && rayOrigin.parent != wrist)
        {
            rayOrigin.SetParent(wrist, false);
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
        renderer.alignment = LineAlignment.View;
        renderer.numCapVertices = 4;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.sortingOrder = 1000;

        // A runtime-created LineRenderer has no guaranteed URP material. In
        // URP that can render magenta when the fallback shader is missing, so
        // explicitly use a shader that is available in the project.
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Sprites/Default") ??
                        Shader.Find("Unlit/Color") ??
                        Shader.Find("UI/Default") ??
                        Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            Debug.LogError("Virtual hands ray could not find a compatible line shader.", this);
            return;
        }

        if (rayMaterial == null)
        {
            rayMaterial = new Material(shader)
            {
                name = "Virtual Hands Ray Cyan Material"
            };
            if (rayMaterial.HasProperty("_BaseColor"))
                rayMaterial.SetColor("_BaseColor", Color.white);
            if (rayMaterial.HasProperty("_Color"))
                rayMaterial.SetColor("_Color", Color.white);
            if (rayMaterial.HasProperty("_ZTest"))
                rayMaterial.SetFloat("_ZTest", 8f); // Always; keep the hand ray visible over the terrain.
            rayMaterial.renderQueue = 3000;
        }
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

        if (visibleRayRenderer != null)
        {
            visibleRayRenderer.colorGradient = gradient;
            visibleRayRenderer.startColor = rayColor;
            visibleRayRenderer.endColor = rayEndColor;
        }
    }

    void OnEnable()
    {
        Active = this;
        if (rayInteractor != null)
            rayInteractor.enabled = true;
        if (lineVisual != null)
            lineVisual.enabled = true;
        if (visibleRayRenderer != null)
            visibleRayRenderer.enabled = true;
        Debug.Log("Virtual hands ray enabled.", this);
    }

    void Update()
    {
        ResolveRightHand();
        EnsureRayOrigin();
        ResolveRightWristIfAvailable();

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
        UpdateVisibleRay();

        // PlayMaker can restore the authored tutorial wording whenever a
        // panel enters a state. Reapply the virtual-hands wording after that
        // state transition, just like the Gamepad condition does.
        GamepadUIInput.RefreshTutorialInstructions();
    }

    void UpdateVisibleRay()
    {
        if (visibleRayRenderer == null)
            return;

        Transform origin = rayOrigin != null ? rayOrigin : transform;
        bool visible = isActiveAndEnabled && origin != null;
        visibleRayRenderer.enabled = visible;
        if (!visible)
            return;

        Vector3 start = origin.position;
        Vector3 end = start + origin.forward * rayLength;
        visibleRayRenderer.positionCount = 2;
        visibleRayRenderer.SetPosition(0, start);
        visibleRayRenderer.SetPosition(1, end);
        visibleRayRenderer.widthMultiplier = rayWidth;
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
        if (visibleRayRenderer != null)
            visibleRayRenderer.enabled = false;
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
