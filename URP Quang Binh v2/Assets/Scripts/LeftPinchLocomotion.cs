using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>Hold a left index pinch and displace the hand to fly forward/backward or up/down.</summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(CharacterController))]
public sealed class LeftPinchLocomotion : MonoBehaviour
{
    [Header("Rig (resolved automatically when empty)")]
    [SerializeField] private OVRCameraRig cameraRig;
    [SerializeField] private OVRHand leftHand;

    [Header("Movement")]
    [Tooltip("Hand displacement in metres before movement starts.")]
    [SerializeField, Min(0)] private float deadZone = 0.03f;
    [Tooltip("Hand displacement in metres that produces maximum speed.")]
    [SerializeField, Min(0.01f)] private float fullSpeedDistance = 0.18f;
    [SerializeField, Min(0)] private float forwardSpeed = 1.2f;
    [SerializeField, Min(0)] private float verticalSpeed = 0.7f;
    [Tooltip("Time for the pinch to settle before its neutral position is captured.")]
    [SerializeField, Min(0)] private float activationDelay = 0.15f;

    private CharacterController body;
    private HandGrabInteractor[] grabInteractors;
    private Vector3 neutralPosition;
    private Vector3 forward;
    private float pinchStartTime;
    private bool pinchPending;
    private bool moving;
    private bool releaseRequired = true;
    private bool applicationFocused = true;

    public bool IsMoving => moving;

    private void Start()
    {
        body = GetComponent<CharacterController>();
        if (cameraRig == null)
            cameraRig = GetComponentInChildren<OVRCameraRig>(true);

        if (cameraRig != null && leftHand == null)
        {
            foreach (var hand in cameraRig.GetComponentsInChildren<OVRHand>(true))
            {
                if (hand.GetHand() == OVRPlugin.Hand.HandLeft)
                {
                    leftHand = hand;
                    break;
                }
            }
        }

        if (cameraRig == null || leftHand == null || !cameraRig.transform.IsChildOf(transform))
        {
            Debug.LogError("LeftPinchLocomotion needs an OVRCameraRig with a left OVRHand below PlayerRoot.", this);
            enabled = false;
            return;
        }

        grabInteractors = cameraRig.GetComponentsInChildren<HandGrabInteractor>(true);
    }

    private void LateUpdate()
    {
        if (!applicationFocused || !OVRManager.hasInputFocus || !body.enabled ||
            !cameraRig.isActiveAndEnabled || !leftHand.isActiveAndEnabled ||
            !leftHand.IsTracked || !leftHand.IsDataHighConfidence ||
            leftHand.IsSystemGestureInProgress)
        {
            StopUntilRelease();
            return;
        }

        // Keep the collision capsule around the user's physical head position.
        Vector3 head = transform.InverseTransformPoint(cameraRig.centerEyeAnchor.position);
        body.height = Mathf.Clamp(head.y, body.radius * 2f + 0.02f, 2.4f);
        body.center = new Vector3(head.x, head.y - body.height * 0.5f + 0.02f, head.z);

        if (!leftHand.GetFingerIsPinching(OVRHand.HandFinger.Index))
        {
            moving = false;
            pinchPending = false;
            releaseRequired = false;
            return;
        }

        // Object interaction takes priority. A new pinch is needed afterwards.
        foreach (var grab in grabInteractors)
        {
            if (grab != null && grab.isActiveAndEnabled && grab.Hand != null &&
                grab.Hand.Handedness == Handedness.Left &&
                (grab.State == InteractorState.Select || (!moving && grab.State == InteractorState.Hover)))
            {
                StopUntilRelease();
                return;
            }
        }

        if (releaseRequired)
            return;

        // Tracking-space coordinates exclude translation caused by locomotion itself.
        // OVRHand is a data provider; its own transform is not necessarily animated.
        Vector3 position = cameraRig.trackingSpace.InverseTransformPoint(cameraRig.leftHandAnchor.position);
        if (!moving)
        {
            if (!pinchPending)
            {
                pinchPending = true;
                pinchStartTime = Time.unscaledTime;
            }
            if (Time.unscaledTime - pinchStartTime < activationDelay)
                return;

            neutralPosition = position;
            Vector3 heading = Vector3.ProjectOnPlane(cameraRig.centerEyeAnchor.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.001f)
                heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            forward = heading.normalized;
            moving = true;
        }

        Vector3 displacement = cameraRig.trackingSpace.TransformVector(position - neutralPosition);
        Vector2 input = new Vector2(Axis(Vector3.Dot(displacement, forward)), Axis(displacement.y));
        input = Vector2.ClampMagnitude(input, 1f);
        Vector3 velocity = forward * (input.x * forwardSpeed) + Vector3.up * (input.y * verticalSpeed);
        body.Move(velocity * Time.deltaTime);
    }

    private float Axis(float distance)
    {
        float amount = Mathf.InverseLerp(deadZone, fullSpeedDistance, Mathf.Abs(distance));
        return Mathf.Sign(distance) * amount;
    }

    private void StopUntilRelease()
    {
        moving = false;
        pinchPending = false;
        releaseRequired = true;
    }

    private void OnDisable() => StopUntilRelease();

    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        StopUntilRelease();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            StopUntilRelease();
    }

    private void OnValidate()
    {
        deadZone = Mathf.Max(0f, deadZone);
        fullSpeedDistance = Mathf.Max(deadZone + 0.01f, fullSpeedDistance);
    }
}
