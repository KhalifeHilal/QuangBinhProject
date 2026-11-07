using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Preloads the tested hand-tracking rig alongside the controller rig.
///
/// The check-in UI always starts on the normal controller rig. VR_Hand_Rig is
/// authored directly in Tutorial_Scene and starts inactive; after check-in
/// only the selected rig is enabled. This avoids a PlayMaker-driven scene
/// transition or a second rig appearing mid-frame.
/// </summary>
[DefaultExecutionOrder(-300)]
public sealed class VirtualHandsRigSwitcher : MonoBehaviour
{
    static VirtualHandsRigSwitcher instance;

    readonly List<GameObject> disabledLegacyRoots = new List<GameObject>();
    GameObject virtualPlayerRoot;
    Transform legacyFollowRoot;
    Vector3 lastLegacyPosition;
    Quaternion lastLegacyRotation;
    bool hasLegacyFollowPose;
    bool virtualModeActive;
    bool virtualRigReady;
    bool preloadStarted;
    bool restored;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        VirtualHandsRigSwitcher switcher = EnsureInstance();
        switcher.BeginPreload();
    }

    void Update()
    {
        EvaluateMode();
    }

    void LateUpdate()
    {
        if (!virtualModeActive || virtualPlayerRoot == null)
            return;

        // PlayMaker's tutorial FSM moves the controller XR Origin when the
        // participant pinches the Start button and enters Zone_02. The
        // controller rig may be inactive in Virtual Hands mode, but its
        // Transform is still changed by SetPosition. Mirror only an external
        // change of that source pose; ordinary hand locomotion moves the
        // virtual rig and leaves the source unchanged.
        SynchronizeVirtualRigAfterTutorialTeleport();
    }

    /// <summary>
    /// Called directly by the check-in UI.  The old implementation waited
    /// for the next Update tick, which could leave the controller rig active
    /// while the hand rig was still not created.  Creating/evaluating the
    /// switcher here makes the mode change deterministic on check-in.
    /// </summary>
    public static void RefreshForParticipant()
    {
        VirtualHandsRigSwitcher switcher = EnsureInstance();
        if (switcher != null)
            switcher.EvaluateMode();
    }

    static VirtualHandsRigSwitcher EnsureInstance()
    {
        if (instance != null)
            return instance;

        GameObject existing = GameObject.Find("Virtual Hands Runtime");
        GameObject root = existing != null ? existing : new GameObject("Virtual Hands Runtime");
        instance = root.GetComponent<VirtualHandsRigSwitcher>();
        if (instance == null)
            instance = root.AddComponent<VirtualHandsRigSwitcher>();
        DontDestroyOnLoad(root);
        return instance;
    }

    void EvaluateMode()
    {
        bool shouldUseVirtualHands = ParticipantSession.IsCheckedIn &&
            ParticipantSession.InteractionMode == ParticipantInteractionMode.Virtual_Hands;

        if (!preloadStarted)
            BeginPreload();

        if (shouldUseVirtualHands && virtualRigReady && !virtualModeActive)
        {
            ActivateVirtualHands();
        }
        else if (!shouldUseVirtualHands && virtualModeActive)
            DeactivateVirtualHands();

        // PlayMaker states can re-enable an authored XR rig when a tutorial
        // state changes. Keep the selected rig authoritative without doing a
        // scene search every frame.
        if (virtualModeActive)
        {
            if (virtualPlayerRoot != null && !virtualPlayerRoot.activeSelf)
                virtualPlayerRoot.SetActive(true);

            for (int i = 0; i < disabledLegacyRoots.Count; i++)
            {
                GameObject legacyRoot = disabledLegacyRoots[i];
                if (legacyRoot != null && legacyRoot.activeSelf)
                    legacyRoot.SetActive(false);
            }
        }
        else if (virtualRigReady && virtualPlayerRoot != null && virtualPlayerRoot.activeSelf)
        {
            virtualPlayerRoot.SetActive(false);
        }
    }

    void BeginPreload()
    {
        if (preloadStarted)
            return;

        preloadStarted = true;
        StartCoroutine(PreloadVirtualHandsRig());
    }

    IEnumerator PreloadVirtualHandsRig()
    {
        restored = false;

        Transform previousRig = FindLegacyRigTransform();
        Vector3 spawnPosition = previousRig != null ? previousRig.position : Vector3.zero;
        Quaternion spawnRotation = previousRig != null ? previousRig.rotation : Quaternion.identity;
        legacyFollowRoot = previousRig;

        // VR_Hand_Rig is authored directly in Tutorial_Scene. We only find
        // it here and keep it inactive; there is no additive scene load.
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject player = null;
        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null &&
                (roots[i].name == "VR_Hand_Rig" || roots[i].name == "PlayerRoot"))
            {
                player = roots[i];
                break;
            }
        }

        if (player == null)
        {
            Debug.LogError("Tutorial_Scene has no direct VR_Hand_Rig for the virtual-hands rig.", this);
            RestoreLegacyControllerRigs();
            yield break;
        }

        // Keep the controller rig active while the hand rig is prepared. The
        // virtual rig is switched on only after ParticipantSession.CheckIn.
        player.SetActive(false);
        player.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        virtualPlayerRoot = player;
        if (player.GetComponent<LeftPinchLocomotion>() == null)
            player.AddComponent<LeftPinchLocomotion>();
        if (player.GetComponent<VirtualHandsPinchInteractor>() == null)
            player.AddComponent<VirtualHandsPinchInteractor>();

        // Do not activate the virtual rig yet. It is already present in the
        // Tutorial_Scene hierarchy and can be toggled without another scene
        // load when the participant ID is confirmed.
        virtualRigReady = true;
        CaptureLegacyFollowPose();
        Debug.Log("Virtual hands rig preloaded: VR_Hand_Rig is ready and inactive.", this);
    }

    void ActivateVirtualHands()
    {
        if (!virtualRigReady || virtualPlayerRoot == null)
            return;

        // The old rig is disabled only after the complete hand rig exists.
        // Both operations happen before the next rendered frame.
        DisableLegacyControllerRigs();
        virtualPlayerRoot.SetActive(true);
        virtualModeActive = true;
        CaptureLegacyFollowPose();
        Debug.Log("Virtual hands rig active: VR_Hand_Rig and right-hand ray are ready.", this);
    }

    void DeactivateVirtualHands()
    {
        if (virtualPlayerRoot != null)
            virtualPlayerRoot.SetActive(false);

        virtualModeActive = false;
        hasLegacyFollowPose = false;
        RestoreLegacyControllerRigs();
    }

    void CaptureLegacyFollowPose()
    {
        if (legacyFollowRoot == null)
            legacyFollowRoot = FindLegacyRigTransform();

        if (legacyFollowRoot == null)
        {
            hasLegacyFollowPose = false;
            return;
        }

        lastLegacyPosition = legacyFollowRoot.position;
        lastLegacyRotation = legacyFollowRoot.rotation;
        hasLegacyFollowPose = true;
    }

    void SynchronizeVirtualRigAfterTutorialTeleport()
    {
        if (legacyFollowRoot == null)
            legacyFollowRoot = FindLegacyRigTransform();

        if (legacyFollowRoot == null)
            return;

        Vector3 sourcePosition = legacyFollowRoot.position;
        Quaternion sourceRotation = legacyFollowRoot.rotation;

        if (!hasLegacyFollowPose)
        {
            lastLegacyPosition = sourcePosition;
            lastLegacyRotation = sourceRotation;
            hasLegacyFollowPose = true;
            return;
        }

        bool positionChanged = (sourcePosition - lastLegacyPosition).sqrMagnitude > 0.000001f;
        bool rotationChanged = Quaternion.Angle(sourceRotation, lastLegacyRotation) > 0.05f;
        if (positionChanged || rotationChanged)
            virtualPlayerRoot.transform.SetPositionAndRotation(sourcePosition, sourceRotation);

        lastLegacyPosition = sourcePosition;
        lastLegacyRotation = sourceRotation;
    }

    Transform FindLegacyRigTransform()
    {
        XROrigin[] origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (XROrigin origin in origins)
        {
            if (origin == null || origin.transform.IsChildOf(transform))
                continue;
            return origin.transform;
        }

        return null;
    }

    void DisableLegacyControllerRigs()
    {
        disabledLegacyRoots.Clear();
        restored = false;
        XROrigin[] origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (XROrigin origin in origins)
        {
            if (origin == null || origin.gameObject == virtualPlayerRoot ||
                (virtualPlayerRoot != null && origin.transform.IsChildOf(virtualPlayerRoot.transform)))
                continue;

            GameObject root = origin.gameObject;
            if (!root.activeSelf)
                continue;

            disabledLegacyRoots.Add(root);
            root.SetActive(false);
        }

        // Some scenes use an XR rig without an XROrigin component. Disable
        // its interactors as a safe fallback, while leaving the EventSystem
        // and UI canvases alive.
        XRBaseInteractor[] interactors = FindObjectsByType<XRBaseInteractor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (XRBaseInteractor interactor in interactors)
        {
            if (interactor == null || (virtualPlayerRoot != null &&
                interactor.transform.IsChildOf(virtualPlayerRoot.transform)))
                continue;
            if (!interactor.gameObject.activeSelf)
                continue;

            if (!disabledLegacyRoots.Contains(interactor.gameObject))
            {
                disabledLegacyRoots.Add(interactor.gameObject);
                interactor.gameObject.SetActive(false);
            }
        }
    }

    void RestoreLegacyControllerRigs()
    {
        if (restored)
            return;

        for (int i = 0; i < disabledLegacyRoots.Count; i++)
        {
            GameObject root = disabledLegacyRoots[i];
            if (root != null)
                root.SetActive(true);
        }

        disabledLegacyRoots.Clear();
        restored = true;
    }

    void OnDestroy()
    {
        if (virtualPlayerRoot != null)
            virtualPlayerRoot.SetActive(false);

        if (instance == this)
            instance = null;

        if (!restored)
            RestoreLegacyControllerRigs();
    }
}
