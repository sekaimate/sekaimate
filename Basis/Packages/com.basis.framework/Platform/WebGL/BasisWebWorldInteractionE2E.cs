#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Basis.BasisUI;
using Basis.Scripts.Common;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class BasisWebWorldInteractionE2E : MonoBehaviour
{
    private const string EnableParameter = "basisWorldInteractionE2E";
    private const string BeeUrlParameter = "basisWorldInteractionBeeUrl";
    private const string BeePasswordParameter = "basisWorldInteractionBeePassword";
    private const string FixtureRootName = "BasisWorldInteractionFixture";
    private const string PickupName = "BasisWorldInteraction-Pickup";
    private const string SeatName = "BasisWorldInteraction-Seat";
    private const string VehicleSeatName = "BasisWorldInteraction-VehicleSeat";
    private const string ImageName = "BasisWorldInteraction-Image";
    private const string PoolName = "BasisWorldInteraction-PoolCue";
    private const string DirectTouchName = "BasisWorldInteraction-DirectTouch";

    private readonly InteractionSnapshot snapshot = new InteractionSnapshot();
    private Transform activeTransform;
    private BasisPickupInteractable activePickup;
    private BasisSeat activeSeat;
    private Button activeButton;
    private BasisInput leftInput;
    private BasisInput rightInput;
    private bool wasLeftTouching;
    private bool wasRightTouching;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        string target = ReadQueryValue(Application.absoluteURL, EnableParameter);
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        var host = new GameObject(nameof(BasisWebWorldInteractionE2E));
        DontDestroyOnLoad(host);
        host.AddComponent<BasisWebWorldInteractionE2E>().Begin(target);
    }

    private async void Begin(string target)
    {
        snapshot.schemaVersion = 1;
        snapshot.activeTarget = target;
        snapshot.stage = "loading-world";
        Publish();

        try
        {
            string beeUrl = ReadQueryValue(Application.absoluteURL, BeeUrlParameter);
            string beePassword = ReadQueryValue(Application.absoluteURL, BeePasswordParameter);
            if (string.IsNullOrWhiteSpace(beeUrl) || string.IsNullOrWhiteSpace(beePassword))
            {
                throw new InvalidOperationException("World interaction BEE URL and password are required.");
            }

            var item = new BasisDataStoreItemKeys.ItemKey
            {
                Mode = BundledContentHolder.Mode.World,
                Url = beeUrl,
                Pass = beePassword
            };
            CachedMetaData.CacheNewItemResult cacheResult = await CachedMetaData.CacheNewItem(item);
            if (cacheResult.Cached == null)
            {
                throw new InvalidOperationException("World interaction BEE metadata could not be loaded.");
            }

            CachedMetaData.SetMetaData(beeUrl, cacheResult.Cached);
            await ContentLoader.LoadWorld(item, BundledContentHolder.NetworkType.Local);
            StartCoroutine(BindFixture(target));
        }
        catch (Exception exception)
        {
            snapshot.stage = "failed";
            snapshot.error = exception.GetType().Name + ": " + exception.Message;
            Publish();
        }
    }

    private IEnumerator BindFixture(string target)
    {
        float deadline = Time.realtimeSinceStartup + 60f;
        GameObject root = null;
        while (root == null && Time.realtimeSinceStartup < deadline)
        {
            root = GameObject.Find(FixtureRootName);
            yield return null;
        }

        if (root == null)
        {
            snapshot.stage = "failed";
            snapshot.error = "World interaction fixture was not found in the loaded BEE.";
            Publish();
            yield break;
        }

        snapshot.worldLoaded = true;
        snapshot.directTouchReady = BasisDirectTouch.Instance != null;
        snapshot.fixtureTypes = root.GetComponentsInChildren<Component>(true)
            .Where(component => component != null)
            .Select(component => component.GetType().Name)
            .Distinct()
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToArray();

        GameObject targetObject = FindTarget(target);
        if (targetObject == null)
        {
            snapshot.stage = "failed";
            snapshot.error = $"Interaction target was not found: {target}";
            Publish();
            yield break;
        }

        activeTransform = targetObject.transform;
        activePickup = targetObject.GetComponentInChildren<BasisPickupInteractable>(true);
        activeSeat = targetObject.GetComponentInChildren<BasisSeat>(true);
        activeButton = targetObject.GetComponentInChildren<Button>(true);
        RegisterEvents(target);
        snapshot.stage = "ready";
        snapshot.ready = true;
        Publish();
    }

    private static GameObject FindTarget(string target)
    {
        string objectName = target switch
        {
            "pickup" => PickupName,
            "seat" => SeatName,
            "vehicle" => VehicleSeatName,
            "image" => ImageName,
            "pool" => PoolName,
            "direct-touch" => DirectTouchName,
            _ => string.Empty
        };
        return string.IsNullOrEmpty(objectName) ? null : GameObject.Find(objectName);
    }

    private void RegisterEvents(string target)
    {
        if (activePickup != null)
        {
            activePickup.OnHoverStartEvent += _ =>
            {
                snapshot.hoverStarts++;
                Publish();
            };
            activePickup.OnInteractStartEvent.AddListener(_ =>
            {
                snapshot.grabStarts++;
                if (target == "image") snapshot.imageGrabStarts++;
                if (target == "pool") snapshot.poolCueGrabStarts++;
                Publish();
            });
            activePickup.OnInteractEndEvent.AddListener(_ =>
            {
                snapshot.grabEnds++;
                Publish();
            });
            activePickup.OnPickupUse?.AddListener(mode =>
            {
                if (mode == BasisPickUpUseMode.OnPickUpUseDown)
                {
                    snapshot.useDowns++;
                    Publish();
                }
            });
        }

        if (activeSeat != null)
        {
            activeSeat.OnLocalPlayerEnterSeat += _ =>
            {
                if (target == "vehicle") snapshot.vehicleSeatEntries++;
                else snapshot.seatEntries++;
                Publish();
            };
            activeSeat.OnLocalPlayerExitSeat += _ =>
            {
                snapshot.seatExits++;
                Publish();
            };
        }

        if (activeButton != null)
        {
            AddEventCounter(activeButton.gameObject, EventTriggerType.PointerEnter, () => snapshot.directTouchPointerEnters++);
            AddEventCounter(activeButton.gameObject, EventTriggerType.PointerDown, () => snapshot.directTouchPointerDowns++);
            AddEventCounter(activeButton.gameObject, EventTriggerType.PointerUp, () => snapshot.directTouchPointerUps++);
            activeButton.onClick.AddListener(() =>
            {
                snapshot.directTouchClicks++;
                Publish();
            });
            snapshot.directTouchCenter = activeButton.transform.position;
            snapshot.directTouchNormal = activeButton.transform.forward;
        }
    }

    private void AddEventCounter(GameObject target, EventTriggerType eventType, Action increment)
    {
        EventTrigger trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
        trigger.triggers ??= new List<EventTrigger.Entry>();
        var entry = new EventTrigger.Entry { eventID = eventType };
        entry.callback.AddListener(_ =>
        {
            increment();
            Publish();
        });
        trigger.triggers.Add(entry);
    }

    private void LateUpdate()
    {
        if (!snapshot.ready || activeTransform == null)
        {
            return;
        }

        if (activeButton != null)
        {
            UpdateDirectTouchState();
            Publish();
            return;
        }

        Camera camera = BasisDesktopEye.Instance?.Camera;
        if (camera == null)
        {
            return;
        }

        bool interacting = activePickup != null && activePickup.Inputs.AnyInteracting(false);
        if (!interacting && (activeSeat == null || !activeSeat.LocallyInSeat))
        {
            activeTransform.SetPositionAndRotation(
                camera.transform.position + camera.transform.forward * 1.5f,
                Quaternion.LookRotation(-camera.transform.forward, camera.transform.up));
        }
        Publish();
    }

    private void UpdateDirectTouchState()
    {
        ResolveHandInputs();
        snapshot.leftHandInputReady = leftInput != null;
        snapshot.rightHandInputReady = rightInput != null;
        snapshot.leftDirectTouching = leftInput != null && BasisDirectTouch.Instance?.IsDeviceTouching(leftInput) == true;
        snapshot.rightDirectTouching = rightInput != null && BasisDirectTouch.Instance?.IsDeviceTouching(rightInput) == true;
        if (leftInput != null)
        {
            snapshot.leftDirectTouchFingertip = GetFingertip(leftInput);
            snapshot.leftPinch = ReadPinch(leftInput);
        }
        if (snapshot.leftDirectTouching && !wasLeftTouching) snapshot.directTouchStarts++;
        if (!snapshot.leftDirectTouching && wasLeftTouching) snapshot.directTouchEnds++;
        if (snapshot.rightDirectTouching && !wasRightTouching) snapshot.directTouchStarts++;
        if (!snapshot.rightDirectTouching && wasRightTouching) snapshot.directTouchEnds++;
        wasLeftTouching = snapshot.leftDirectTouching;
        wasRightTouching = snapshot.rightDirectTouching;
    }

    private static Vector3 GetFingertip(BasisInput input)
    {
        BasisTransformMapping mapping = BasisLocalAvatarDriver.Mapping;
        if (mapping != null && input.TryGetRole(out BasisBoneTrackedRole role))
        {
            Transform distal = role == BasisBoneTrackedRole.LeftHand ? mapping.LeftIndex[2] : mapping.RightIndex[2];
            bool hasDistal = role == BasisBoneTrackedRole.LeftHand ? mapping.HasLeftIndex[2] : mapping.HasRightIndex[2];
            if (hasDistal && distal != null)
            {
                return distal.position + distal.forward * BasisDirectTouch.DistalTipOffset;
            }
        }
        return input.RaycastCoord.position + input.RaycastCoord.rotation * (Vector3.forward * BasisDirectTouch.FingerLength);
    }

    private static float ReadPinch(BasisInput input)
    {
        var property = input.GetType().GetProperty("Pinch");
        return property?.PropertyType == typeof(float) ? (float)property.GetValue(input) : 0f;
    }

    private void ResolveHandInputs()
    {
        leftInput = null;
        rightInput = null;
        BasisInteractInput[] inputs = BasisPlayerInteract.Instance?.InteractInputs;
        if (inputs == null) return;

        foreach (BasisInteractInput interactInput in inputs)
        {
            BasisInput input = interactInput.input;
            if (input == null || !input.HasControl || !input.TryGetRole(out BasisBoneTrackedRole role)) continue;
            if (role == BasisBoneTrackedRole.LeftHand) leftInput = input;
            if (role == BasisBoneTrackedRole.RightHand) rightInput = input;
        }
    }

    private void Publish()
    {
        BasisWebWorldInteractionE2EPublish(JsonUtility.ToJson(snapshot));
    }

    private static string ReadQueryValue(string absoluteUrl, string key)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
        {
            return string.Empty;
        }

        foreach (string field in uri.Query.TrimStart('?').Split('&'))
        {
            string[] pair = field.Split(new[] { '=' }, 2);
            if (Uri.UnescapeDataString(pair[0]) == key)
            {
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
        }
        return string.Empty;
    }

    [DllImport("__Internal")]
    private static extern void BasisWebWorldInteractionE2EPublish(string snapshotJson);

    [Serializable]
    private sealed class InteractionSnapshot
    {
        public int schemaVersion;
        public bool ready;
        public string stage = string.Empty;
        public string error = string.Empty;
        public bool worldLoaded;
        public bool directTouchReady;
        public string[] fixtureTypes = Array.Empty<string>();
        public string activeTarget = string.Empty;
        public int hoverStarts;
        public int grabStarts;
        public int grabEnds;
        public int useDowns;
        public int seatEntries;
        public int seatExits;
        public int vehicleSeatEntries;
        public int imageGrabStarts;
        public int poolCueGrabStarts;
        public bool leftHandInputReady;
        public bool rightHandInputReady;
        public bool leftDirectTouching;
        public bool rightDirectTouching;
        public int directTouchStarts;
        public int directTouchEnds;
        public int directTouchPointerEnters;
        public int directTouchPointerDowns;
        public int directTouchPointerUps;
        public int directTouchClicks;
        public Vector3 directTouchCenter;
        public Vector3 directTouchNormal;
        public Vector3 leftDirectTouchFingertip;
        public float leftPinch;
    }
}
#endif
