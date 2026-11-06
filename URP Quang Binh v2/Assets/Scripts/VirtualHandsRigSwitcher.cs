using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Loads the tested Meta hand-tracking rig after participant check-in.
///
/// The check-in UI still runs on the normal controller rig.  Once the
/// participant is assigned to the Virtual_Hands condition, the controller
/// rig is disabled and the PlayerRoot from Virtual_Hand_Rig is moved into the
/// current scene.  This keeps the rig independent of the tutorial/scenario
/// scene and prevents duplicate cameras and interactors.
/// </summary>
[DefaultExecutionOrder(-300)]
public sealed class VirtualHandsRigSwitcher : MonoBehaviour
{
    const string VirtualRigSceneName = "Virtual_Hand_Rig";

    static VirtualHandsRigSwitcher instance;

    readonly List<GameObject> disabledLegacyRoots = new List<GameObject>();
    GameObject virtualPlayerRoot;
    bool virtualModeActive;
    bool loading;
    bool restored;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject root = new GameObject("Virtual Hands Runtime");
        instance = root.AddComponent<VirtualHandsRigSwitcher>();
        DontDestroyOnLoad(root);
    }

    void Update()
    {
        bool shouldUseVirtualHands = ParticipantSession.IsCheckedIn &&
            ParticipantSession.InteractionMode == ParticipantInteractionMode.Virtual_Hands;

        if (shouldUseVirtualHands && !virtualModeActive && !loading)
            StartCoroutine(EnableVirtualHands());
        else if (!shouldUseVirtualHands && virtualModeActive)
            DisableVirtualHands();
    }

    IEnumerator EnableVirtualHands()
    {
        loading = true;
        restored = false;

        Transform previousRig = FindLegacyRigTransform();
        Vector3 spawnPosition = previousRig != null ? previousRig.position : Vector3.zero;
        Quaternion spawnRotation = previousRig != null ? previousRig.rotation : Quaternion.identity;

        // Allow the rig scene to be opened directly for isolated testing
        // without loading a second copy of itself.
        if (SceneManager.GetActiveScene().name == VirtualRigSceneName)
        {
            GameObject existingPlayer = GameObject.Find("PlayerRoot");
            if (existingPlayer != null)
            {
                virtualPlayerRoot = existingPlayer;
                DontDestroyOnLoad(virtualPlayerRoot);
                if (virtualPlayerRoot.GetComponent<LeftPinchLocomotion>() == null)
                    virtualPlayerRoot.AddComponent<LeftPinchLocomotion>();
                if (virtualPlayerRoot.GetComponent<VirtualHandsPinchInteractor>() == null)
                    virtualPlayerRoot.AddComponent<VirtualHandsPinchInteractor>();
                virtualModeActive = true;
                loading = false;
                yield break;
            }
        }

        if (!IsSceneAvailable(VirtualRigSceneName))
        {
            Debug.LogError($"Virtual hands scene '{VirtualRigSceneName}' is not in Build Settings.", this);
            RestoreLegacyControllerRigs();
            loading = false;
            yield break;
        }

        AsyncOperation load = SceneManager.LoadSceneAsync(VirtualRigSceneName, LoadSceneMode.Additive);
        if (load == null)
        {
            RestoreLegacyControllerRigs();
            loading = false;
            yield break;
        }

        yield return load;

        Scene rigScene = SceneManager.GetSceneByName(VirtualRigSceneName);
        GameObject[] roots = rigScene.IsValid() ? rigScene.GetRootGameObjects() : null;
        if (roots == null)
        {
            RestoreLegacyControllerRigs();
            loading = false;
            yield break;
        }

        GameObject player = null;
        foreach (GameObject root in roots)
        {
            if (root.name == "PlayerRoot")
            {
                player = root;
                // Keep the currently active controller rig rendering while
                // the hand rig is being prepared. This prevents a one-frame
                // black/flickering view during participant check-in.
                player.SetActive(false);
                continue;
            }

            // The source scene contains a floor, light, volume and helper
            // anchors. They are useful in the standalone hand demo, but must
            // not create duplicate cameras or cover the flood map here.
            root.SetActive(false);
        }

        if (player == null)
        {
            Debug.LogError("Virtual_Hand_Rig has no PlayerRoot.", this);
            yield return SceneManager.UnloadSceneAsync(rigScene);
            RestoreLegacyControllerRigs();
            loading = false;
            yield break;
        }

        player.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        SceneManager.MoveGameObjectToScene(player, SceneManager.GetActiveScene());
        DontDestroyOnLoad(player);
        virtualPlayerRoot = player;

        if (player.GetComponent<LeftPinchLocomotion>() == null)
            player.AddComponent<LeftPinchLocomotion>();
        if (player.GetComponent<VirtualHandsPinchInteractor>() == null)
            player.AddComponent<VirtualHandsPinchInteractor>();

        // The old rig is disabled only after the complete hand rig exists.
        // Both operations happen before the next rendered frame.
        DisableLegacyControllerRigs();

        virtualPlayerRoot.SetActive(true);
        virtualModeActive = true;
        loading = false;

        if (rigScene.IsValid() && rigScene.isLoaded)
            yield return SceneManager.UnloadSceneAsync(rigScene);
    }

    void DisableVirtualHands()
    {
        if (virtualPlayerRoot != null)
        {
            virtualPlayerRoot.SetActive(false);
            Destroy(virtualPlayerRoot);
            virtualPlayerRoot = null;
        }

        virtualModeActive = false;
        RestoreLegacyControllerRigs();
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

    static bool IsSceneAvailable(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.EndsWith('/' + sceneName + ".unity") ||
                path.EndsWith('\\' + sceneName + ".unity"))
                return true;
        }

        return false;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;

        if (!restored)
            RestoreLegacyControllerRigs();
    }
}
