using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
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
    [SerializeField, Min(0.5f)] float rayLength = 100f;
    [SerializeField, Min(0.0005f)] float rayWidth = 0.005f;
    [SerializeField] Color rayColor = new Color(0f, 0.627451f, 1f, 1f);
    [SerializeField] Color rayEndColor = new Color(1f, 1f, 1f, 0f);
    [SerializeField] Color invalidRayColor = new Color(1f, 0.0141509175f, 0.0141509175f, 1f);
    [SerializeField] Color blockedRayColor = new Color(1f, 0.92156863f, 0.015686275f, 1f);

    [Header("Wrist ray origin")]
    [Tooltip("Local offset from the tracked right wrist. Z moves into the palm toward the space between thumb and index finger; Y lowers the ray slightly toward the arm.")]
    [SerializeField] Vector3 wristRayLocalOffset = new Vector3(-0.025f, -0.055f, 0.095f);
    [SerializeField] Vector3 wristRayLocalEulerAngles;

    [Header("Pinch stability")]
    [Range(0f, 1f)] [SerializeField] float pinchStartStrength = 0.55f;
    [Range(0f, 1f)] [SerializeField] float pinchReleaseStrength = 0.35f;
    [Min(0f)] [SerializeField] float trackingGracePeriod = 0.12f;

    XRRayInteractor rayInteractor;
    XRInputButtonReader pinchReader;
    XRInteractorLineVisual lineVisual;
    LineRenderer visibleRayRenderer;
    Material rayMaterial;
    bool rayOriginCreatedByScript;
    bool previousPinching;
    bool stablePinching;
    float lastTrackedPinchTime = -Mathf.Infinity;
    bool warnedMissingHand;
    Slider activeSlider;
    Graphic[] cachedUiGraphics;
    float nextUiGraphicsRefresh;

    public static VirtualHandsPinchInteractor Active { get; private set; }
    public XRRayInteractor RayInteractor => rayInteractor;
    public Transform RayOrigin => rayOrigin;
    public bool IsPinching { get; private set; }
    public bool PinchStartedThisFrame { get; private set; }
    public bool PinchReleasedThisFrame { get; private set; }

    /// <summary>
    /// Keeps the visible hand ray at its authored length while the tutorial
    /// uses the two dyke snap colliders.  The XR ray still reports the hit to
    /// DykeManagerTutorial; only the line visual is prevented from retracting
    /// to the snap collider (which made the ray appear to snap into the hand).
    /// </summary>
    public void SetTutorialSnapPointRayMode(bool enabled)
    {
        if (lineVisual == null && visibleRayRenderer == null)
            return;

        // Always use the custom renderer for virtual hands. The XRI line
        // visual stops at the first 3D collider (terrain, buildings, or a
        // snap point), while the intended hand ray should remain at full
        // length and stop only at a UI canvas. The XRRayInteractor itself
        // stays enabled for UI and placement hit testing.
        if (lineVisual != null)
            lineVisual.enabled = false;
        if (visibleRayRenderer != null)
            visibleRayRenderer.enabled = true;
    }

    void Awake()
    {
        Active = this;

        // Keep the virtual-hand ray long enough to cross the complete map.
        // It is shortened visually only when the UI endpoint is found.
        rayLength = Mathf.Max(rayLength, 100f);

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
        lineVisual.overrideInteractorLineOrigin = true;
        lineVisual.overrideInteractorLineLength = true;
        lineVisual.lineLength = rayLength;
        lineVisual.autoAdjustLineLength = true;
        lineVisual.minLineLength = 0.5f;
        lineVisual.useDistanceToHitAsMaxLineLength = true;
        lineVisual.lineRetractionDelay = 0.5f;
        lineVisual.lineLengthChangeSpeed = 12f;
        lineVisual.lineWidth = rayWidth;
        lineVisual.lineOriginTransform = rayOrigin;
        ConfigureRayRenderer(rayObject.GetComponent<LineRenderer>());
        ApplyRayColor();

        // Keep a separate renderer for a guaranteed visible cyan ray while
        // XRRayInteractor still handles all hit testing and UI interaction.
        GameObject visibleRay = new GameObject("Virtual Hands Visible Ray");
        visibleRay.transform.SetParent(transform, false);
        visibleRayRenderer = visibleRay.AddComponent<LineRenderer>();
        ConfigureRayRenderer(visibleRayRenderer);
        visibleRayRenderer.positionCount = 2;
        // The controller condition uses only XRInteractorLineVisual. Keep
        // this fallback disabled while that visual exists so it cannot mask
        // the controller-style red/yellow hit-state colors.
        visibleRayRenderer.enabled = false;
        ApplyRayColor();
        // The custom renderer below is used instead of XRInteractorLineVisual
        // so 3D colliders cannot shorten the visible hand ray.
        lineVisual.enabled = false;
        visibleRayRenderer.enabled = true;

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
        Gradient invalidGradient = CreateGradient(invalidRayColor, rayEndColor);
        Gradient blockedGradient = CreateGradient(blockedRayColor, blockedRayColor);
        lineVisual.invalidColorGradient = invalidGradient;
        lineVisual.blockedColorGradient = blockedGradient;

        LineRenderer renderer = lineVisual.GetComponent<LineRenderer>();
        if (renderer != null)
        {
            renderer.colorGradient = gradient;
            renderer.startColor = rayColor;
            renderer.endColor = rayEndColor;
        }

        if (visibleRayRenderer != null)
        {
            // The hand ray must remain visibly continuous all the way to the
            // UI hit. Do not fade its endpoint like the controller line does.
            Gradient visibleGradient = CreateGradient(rayColor, rayColor);
            visibleRayRenderer.colorGradient = visibleGradient;
            visibleRayRenderer.startColor = rayColor;
            visibleRayRenderer.endColor = rayColor;
        }
    }

    static Gradient CreateGradient(Color start, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(end.a, 1f)
            });
        return gradient;
    }

    void OnEnable()
    {
        Active = this;
        if (rayInteractor != null)
            rayInteractor.enabled = true;
        if (lineVisual != null)
            lineVisual.enabled = false;
        if (visibleRayRenderer != null)
            visibleRayRenderer.enabled = true;
        Debug.Log("Virtual hands ray enabled.", this);
    }

    void Update()
    {
        ResolveRightHand();
        EnsureRayOrigin();
        ResolveRightWristIfAvailable();

        bool pinching = ReadStablePinch();

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

        if (lineVisual != null)
            lineVisual.enabled = false;
        if (visibleRayRenderer != null)
            visibleRayRenderer.enabled = true;
    }

    void LateUpdate()
    {
        UpdateVirtualSliderDrag();
        UpdateVisibleRay();

        // PlayMaker can restore the authored tutorial wording whenever a
        // panel enters a state. Reapply the virtual-hands wording after that
        // state transition, just like the Gamepad condition does.
        GamepadUIInput.RefreshTutorialInstructions();
    }

    void UpdateVirtualSliderDrag()
    {
        if (!IsPinching)
        {
            activeSlider = null;
            return;
        }

        if (activeSlider == null && rayInteractor != null &&
            rayInteractor.TryGetCurrentUIRaycastResult(out var currentUiHit))
        {
            if (currentUiHit.gameObject != null)
                activeSlider = currentUiHit.gameObject.GetComponentInParent<Slider>();
        }

        if (activeSlider == null || rayOrigin == null ||
            !activeSlider.gameObject.activeInHierarchy)
            return;

        RectTransform sliderRect = activeSlider.GetComponent<RectTransform>();
        if (sliderRect == null)
            return;

        // Project the hand ray onto the slider's plane. This keeps the slider
        // draggable even when the ray briefly leaves the handle while the
        // pinch is held, which is common with hand-tracking jitter.
        Plane sliderPlane = new Plane(sliderRect.forward, sliderRect.position);
        Ray handRay = new Ray(rayOrigin.position, rayOrigin.forward);
        if (!sliderPlane.Raycast(handRay, out float distance) || distance < 0f)
            return;

        Vector3 localPoint = sliderRect.InverseTransformPoint(handRay.GetPoint(distance));
        Rect rect = sliderRect.rect;
        float normalized;
        if (activeSlider.direction == Slider.Direction.BottomToTop ||
            activeSlider.direction == Slider.Direction.TopToBottom)
        {
            normalized = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);
            if (activeSlider.direction == Slider.Direction.TopToBottom)
                normalized = 1f - normalized;
        }
        else
        {
            normalized = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            if (activeSlider.direction == Slider.Direction.RightToLeft)
                normalized = 1f - normalized;
        }

        activeSlider.value = Mathf.Lerp(activeSlider.minValue, activeSlider.maxValue, normalized);
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
        float closestUiDistance = rayLength;

        // Use only the UI result for the visible endpoint.  A terrain or
        // snap-point collider may still be returned by the XR interactor for
        // dyke construction, but it must not shorten the hand ray.
        if (rayInteractor != null &&
            rayInteractor.TryGetCurrentUIRaycastResult(out var uiRaycastResult))
        {
            Vector3 toUi = uiRaycastResult.worldPosition - start;
            float distanceAlongRay = Vector3.Dot(toUi, origin.forward);
            if (distanceAlongRay > 0f && distanceAlongRay <= closestUiDistance)
            {
                closestUiDistance = distanceAlongRay;
                end = uiRaycastResult.worldPosition;
            }
        }

        // Some generated panels intentionally disable Image.raycastTarget on
        // their background. The XR UI module can therefore still interact
        // with their buttons but has no UI endpoint for the visual ray. Use a
        // cached world-space Graphic pass as a visual-only fallback so every
        // panel, slider, and feedback canvas stops the ray naturally.
        if (TryGetClosestWorldSpaceGraphicHit(start, origin.forward, closestUiDistance,
            out Vector3 graphicPoint, out float graphicDistance))
        {
            end = graphicPoint;
        }

        visibleRayRenderer.positionCount = 2;
        visibleRayRenderer.SetPosition(0, start);
        visibleRayRenderer.SetPosition(1, end);
        visibleRayRenderer.widthMultiplier = rayWidth;
    }

    bool TryGetClosestWorldSpaceGraphicHit(Vector3 origin, Vector3 direction,
        float maxDistance, out Vector3 hitPoint, out float hitDistance)
    {
        hitPoint = default;
        hitDistance = maxDistance;
        RefreshUiGraphicsCache();
        if (cachedUiGraphics == null)
            return false;

        Ray ray = new Ray(origin, direction);
        bool found = false;
        for (int i = 0; i < cachedUiGraphics.Length; i++)
        {
            Graphic graphic = cachedUiGraphics[i];
            if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvas == null ||
                graphic.canvas.renderMode != RenderMode.WorldSpace)
                continue;

            RectTransform rect = graphic.rectTransform;
            if (rect == null || rect.rect.width <= 0f || rect.rect.height <= 0f)
                continue;

            Plane plane = new Plane(rect.forward, rect.position);
            if (!plane.Raycast(ray, out float distance) || distance <= 0f || distance > hitDistance)
                continue;

            Vector3 localPoint = rect.InverseTransformPoint(ray.GetPoint(distance));
            if (!rect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                continue;

            hitDistance = distance;
            hitPoint = ray.GetPoint(distance);
            found = true;
        }

        return found;
    }

    void RefreshUiGraphicsCache()
    {
        if (Time.unscaledTime < nextUiGraphicsRefresh)
            return;

        nextUiGraphicsRefresh = Time.unscaledTime + 0.2f;
        List<Graphic> graphics = new List<Graphic>(64);
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            Graphic[] canvasGraphics = canvas.GetComponentsInChildren<Graphic>(false);
            for (int j = 0; j < canvasGraphics.Length; j++)
            {
                Graphic graphic = canvasGraphics[j];
                if (graphic != null && graphic.isActiveAndEnabled)
                    graphics.Add(graphic);
            }
        }

        cachedUiGraphics = graphics.ToArray();
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
        stablePinching = false;
        lastTrackedPinchTime = -Mathf.Infinity;
        activeSlider = null;
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
        pinchReleaseStrength = Mathf.Min(pinchReleaseStrength, pinchStartStrength);
        trackingGracePeriod = Mathf.Max(0f, trackingGracePeriod);
    }

    bool ReadStablePinch()
    {
        bool handAvailable = rightHand != null && rightHand.isActiveAndEnabled &&
            rightHand.IsTracked && rightHand.IsDataHighConfidence;
        if (!handAvailable)
        {
            // Hand tracking can briefly lose confidence while the user drags
            // a slider. Keep the press alive for a short grace period instead
            // of sending a false release to the XR UI module.
            return stablePinching && Time.unscaledTime - lastTrackedPinchTime <= trackingGracePeriod;
        }

        bool rawPinching = rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        float strength = rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

        if (!stablePinching)
            stablePinching = rawPinching || strength >= pinchStartStrength;
        else
            stablePinching = rawPinching || strength >= pinchReleaseStrength;

        if (stablePinching)
            lastTrackedPinchTime = Time.unscaledTime;

        return stablePinching;
    }
}
