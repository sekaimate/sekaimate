#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

public static class BasisWebMediaPlayerLoop
{
    private static readonly List<Action> Callbacks = new List<Action>();
    private static bool installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Callbacks.Clear();
        installed = false;
    }

    public static void Register(Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (!installed) Install();
        if (!Callbacks.Contains(callback)) Callbacks.Add(callback);
    }

    public static void Unregister(Action callback)
    {
        if (callback != null) Callbacks.Remove(callback);
    }

    private static void Install()
    {
        PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
        PlayerLoopSystem[] rootSystems = loop.subSystemList;
        for (int i = 0; i < rootSystems.Length; i++)
        {
            if (rootSystems[i].type != typeof(UnityEngine.PlayerLoop.Update)) continue;
            PlayerLoopSystem update = rootSystems[i];
            var systems = new List<PlayerLoopSystem>(update.subSystemList ?? Array.Empty<PlayerLoopSystem>())
            {
                new PlayerLoopSystem
                {
                    type = typeof(BasisWebMediaPlayerLoop),
                    updateDelegate = Tick,
                }
            };
            update.subSystemList = systems.ToArray();
            rootSystems[i] = update;
            loop.subSystemList = rootSystems;
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
            return;
        }
        throw new InvalidOperationException("Unity Update PlayerLoop was not found.");
    }

    private static void Tick()
    {
        for (int i = Callbacks.Count - 1; i >= 0; i--)
        {
            Callbacks[i]();
        }
    }
}
#endif
