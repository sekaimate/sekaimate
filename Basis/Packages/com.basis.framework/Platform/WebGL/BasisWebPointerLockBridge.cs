#if UNITY_WEBGL && !UNITY_EDITOR
using AOT;
using System.Runtime.InteropServices;

internal static class BasisWebPointerLockBridge
{
    private delegate void StateChangedCallback(int isLocked);

    private static readonly StateChangedCallback StateChanged = HandleStateChanged;
    private static bool initialized;

    public static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        BasisWebPointerLockInitialize(StateChanged);
        initialized = true;
    }

    public static bool RequestFromUserGesture()
    {
        EnsureInitialized();
        return BasisWebPointerLockRequestFromUserGesture() == 1;
    }

    public static void Exit()
    {
        EnsureInitialized();
        BasisWebPointerLockExit();
    }

    [MonoPInvokeCallback(typeof(StateChangedCallback))]
    private static void HandleStateChanged(int isLocked)
    {
        BasisCursorManagement.ApplyWebPointerLockState(isLocked == 1);
    }

    [DllImport("__Internal")]
    private static extern void BasisWebPointerLockInitialize(StateChangedCallback onStateChanged);

    [DllImport("__Internal")]
    private static extern int BasisWebPointerLockRequestFromUserGesture();

    [DllImport("__Internal")]
    private static extern void BasisWebPointerLockExit();
}
#endif
