using UnityEngine;

/// <summary>
/// Keeps a world-space UI canvas in front of the XR camera so it behaves like an overlay in VR.
/// Attach this to the waypoint marker canvas root and assign the XR camera or headset transform.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class XRCanvasFollower : MonoBehaviour
{
    [Header("Follow Target")]
    [Tooltip("The XR camera / headset transform to follow. If left empty, Camera.main is used.")]
    [SerializeField] private Transform followTarget;

    [Header("Overlay Offset")]
    [Tooltip("Local offset from the headset in meters. Z should usually be positive so the canvas sits in front of the player.")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, -0.05f, 1.25f);

    [Tooltip("Additional local rotation applied after facing the headset. Leave at zero for a straight-on HUD.")]
    [SerializeField] private Vector3 localEulerOffset = Vector3.zero;

    [Tooltip("Extra yaw correction applied after the canvas is aimed at the headset. Use 180 to flip reversed UI, or 0 if the canvas already faces the right way.")]
    [SerializeField] private float facingYawOffset = 180f;

    [Tooltip("If enabled, the canvas faces the headset every frame.")]
    [SerializeField] private bool faceHeadset = true;

    [Tooltip("If enabled, the canvas will be treated as a world-space canvas at runtime.")]
    [SerializeField] private bool forceWorldSpace = true;

    private Canvas cachedCanvas;

    private void Awake()
    {
        cachedCanvas = GetComponent<Canvas>();
        EnsureFollowTarget();
        ApplyCanvasMode();
    }

    private void OnEnable()
    {
        EnsureFollowTarget();
        ApplyCanvasMode();
        UpdateTransform();
    }

    private void LateUpdate()
    {
        EnsureFollowTarget();
        UpdateTransform();
    }

    private void EnsureFollowTarget()
    {
        if (followTarget != null)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            followTarget = mainCamera.transform;
        }
    }

    private void ApplyCanvasMode()
    {
        if (!forceWorldSpace || cachedCanvas == null)
        {
            return;
        }

        cachedCanvas.renderMode = RenderMode.WorldSpace;
    }

    private void UpdateTransform()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 worldPosition = followTarget.position + followTarget.rotation * localOffset;
        Quaternion worldRotation = followTarget.rotation * Quaternion.Euler(localEulerOffset);

        if (faceHeadset)
        {
            Vector3 lookDirection = followTarget.position - worldPosition;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                worldRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                    * Quaternion.Euler(0f, facingYawOffset, 0f)
                    * Quaternion.Euler(localEulerOffset);
            }
        }

        transform.SetPositionAndRotation(worldPosition, worldRotation);
    }
}
