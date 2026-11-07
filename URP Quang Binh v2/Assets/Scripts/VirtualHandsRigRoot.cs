using UnityEngine;

/// <summary>
/// Keeps the hand-tracking condition deliberately small and deterministic.
/// The Meta camera/hand provider is still used for tracking, but the large
/// interaction sample that comes with it is not part of this study rig.
/// Only the two study interaction scripts and the hand visuals remain active.
/// </summary>
[DefaultExecutionOrder(-250)]
public sealed class VirtualHandsRigRoot : MonoBehaviour
{
    [Header("Minimal study rig")]
    // Keep this prefab active because it also contains the tracked hand
    // visuals. Individual unwanted interactors are disabled below instead.
    [SerializeField] bool disableComprehensiveInteraction = false;
    [SerializeField] bool disableUnusedHandInteractors = true;

    static readonly string[] UnusedInteractorNames =
    {
        "HandGrabInteractor",
        "HandRayInteractor",
        "HandPokeInteractor",
        "HandTouchInteractor",
        "HandDistanceInteractor",
        "HandGrabUseInteractor",
        "DistanceHandGrabInteractor",
        "TouchHandGrabInteractor",
        "DistanceGrabInteractor",
        "GrabInteractor",
        "PokeInteractor",
        "RayInteractor"
    };

    void Awake()
    {
        ConfigureMinimalRig();
    }

    void OnEnable()
    {
        ConfigureMinimalRig();
    }

    public void ConfigureMinimalRig()
    {
        if (disableComprehensiveInteraction)
        {
            Transform comprehensive = FindChildByName(transform, "OVRInteractionComprehensive");
            if (comprehensive != null && comprehensive.gameObject.activeSelf)
                comprehensive.gameObject.SetActive(false);
        }

        if (!disableUnusedHandInteractors)
            return;

        // Some versions of the Meta prefabs put individual interactors
        // outside the comprehensive prefab. Disable those by component name,
        // while leaving OVRHand/OVRSkeleton and the custom study ray intact.
        MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
        {
            MonoBehaviour component = components[i];
            if (component == null || component == this ||
                component is LeftPinchLocomotion ||
                component is VirtualHandsPinchInteractor ||
                (component.transform != null &&
                 component.transform.name == "Virtual Hands Pinch Ray"))
                continue;

            string typeName = component.GetType().Name;
            if (IsUnusedInteractor(typeName))
                component.enabled = false;
        }
    }

    static bool IsUnusedInteractor(string typeName)
    {
        for (int i = 0; i < UnusedInteractorNames.Length; i++)
        {
            if (typeName == UnusedInteractorNames[i] ||
                typeName.EndsWith(UnusedInteractorNames[i], System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    static Transform FindChildByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == targetName)
                return all[i];
        }

        return null;
    }
}
