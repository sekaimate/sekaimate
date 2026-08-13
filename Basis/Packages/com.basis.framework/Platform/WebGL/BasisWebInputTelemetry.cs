#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using Basis.Scripts.BasisCharacterController;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices.Desktop;
using UnityEngine;
using UnityEngine.InputSystem;

internal sealed class BasisWebInputTelemetry : MonoBehaviour
{
    private const string EnableQuery = "basisInputE2E=1";
    private const float PublishIntervalSeconds = 0.05f;

    private float nextPublishTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Application.absoluteURL.IndexOf(EnableQuery, StringComparison.Ordinal) < 0)
        {
            return;
        }

        GameObject telemetryObject = new GameObject(nameof(BasisWebInputTelemetry));
        DontDestroyOnLoad(telemetryObject);
        telemetryObject.AddComponent<BasisWebInputTelemetry>();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextPublishTime)
        {
            return;
        }

        nextPublishTime = Time.unscaledTime + PublishIntervalSeconds;
        BasisWebInputTelemetryPublish(JsonUtility.ToJson(CaptureSnapshot()));
    }

    private static BasisWebInputSnapshot CaptureSnapshot()
    {
        BasisLocalPlayer player = BasisLocalPlayer.Instance;
        BasisLocalCharacterDriver character = player?.LocalCharacterDriver;
        BasisDesktopEye eye = BasisDesktopEye.Instance;
        BasisOnScreenControls controls = FindAnyObjectByType<BasisOnScreenControls>(FindObjectsInactive.Exclude);

        return new BasisWebInputSnapshot
        {
            schemaVersion = 1,
            frame = Time.frameCount,
            ready = character != null && eye != null && BasisLocalInputActions.MoveAction?.enabled == true,
            pointerLocked = BasisCursorManagement.ActiveLockState() == CursorLockMode.Locked,
            moveAction = ReadVector(BasisLocalInputActions.MoveAction),
            moveDevice = ReadDeviceLayout(BasisLocalInputActions.MoveAction),
            movement = character?.MovementVector ?? Vector2.zero,
            playerPosition = player != null ? player.transform.position : Vector3.zero,
            lookAction = ReadVector(BasisLocalInputActions.LookAction),
            lookDevice = ReadDeviceLayout(BasisLocalInputActions.LookAction),
            lookVector = eye != null ? eye.LookRotationVector : Vector2.zero,
            lookYaw = eye != null ? eye.rotationYaw : 0f,
            lookPitch = eye != null ? eye.rotationPitch : 0f,
            activeTouches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count,
            onScreenControls = CaptureOnScreenControls(controls),
            screenSize = new Vector2(Screen.width, Screen.height),
        };
    }

    private static Vector2 ReadVector(InputAction action)
    {
        return action?.enabled == true ? action.ReadValue<Vector2>() : Vector2.zero;
    }

    private static string ReadDeviceLayout(InputAction action)
    {
        return action?.activeControl?.device?.layout ?? string.Empty;
    }

    private static BasisWebOnScreenControlsSnapshot CaptureOnScreenControls(BasisOnScreenControls controls)
    {
        if (controls == null)
        {
            return new BasisWebOnScreenControlsSnapshot();
        }

        return new BasisWebOnScreenControlsSnapshot
        {
            ready = true,
            leftStick = ToScreenPoint(controls.LeftControl.transform as RectTransform),
            rightStick = ToScreenPoint(controls.RightControl.transform as RectTransform),
            jump = ToScreenPoint(controls.Space.transform as RectTransform),
            crouch = ToScreenPoint(controls.C.transform as RectTransform),
        };
    }

    private static Vector2 ToScreenPoint(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return Vector2.zero;
        }

        Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        return RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(rectTransform.rect.center));
    }

    [DllImport("__Internal")]
    private static extern void BasisWebInputTelemetryPublish(string snapshotJson);
}

[Serializable]
internal sealed class BasisWebInputSnapshot
{
    public int schemaVersion;
    public int frame;
    public bool ready;
    public bool pointerLocked;
    public Vector2 moveAction;
    public string moveDevice;
    public Vector2 movement;
    public Vector3 playerPosition;
    public Vector2 lookAction;
    public string lookDevice;
    public Vector2 lookVector;
    public float lookYaw;
    public float lookPitch;
    public int activeTouches;
    public BasisWebOnScreenControlsSnapshot onScreenControls;
    public Vector2 screenSize;
}

[Serializable]
internal sealed class BasisWebOnScreenControlsSnapshot
{
    public bool ready;
    public Vector2 leftStick;
    public Vector2 rightStick;
    public Vector2 jump;
    public Vector2 crouch;
}
#endif
