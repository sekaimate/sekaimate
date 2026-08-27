#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;

public enum BasisWebAudioUiSound
{
    Hover = 0,
    Press = 1,
    Chat = 2,
}

public static class BasisWebAudioUiSoundBridge
{
    public static void Play(BasisWebAudioUiSound sound, float volume)
    {
        BasisWebAudioPlayUiSound((int)sound, volume);
    }

    [DllImport("__Internal")]
    private static extern void BasisWebAudioPlayUiSound(int sound, float volume);
}
#endif
