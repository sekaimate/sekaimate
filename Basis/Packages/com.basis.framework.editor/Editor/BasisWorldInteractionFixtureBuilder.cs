using System;
using System.Linq;
using System.Reflection;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.UI;
using Basis.Scripts.UI.UI_Panels;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class BasisWorldInteractionFixtureBuilder
{
    public const string RootName = "BasisWorldInteractionFixture";
    public const string PickupName = "BasisWorldInteraction-Pickup";
    public const string SeatName = "BasisWorldInteraction-Seat";
    public const string VehicleName = "BasisWorldInteraction-Vehicle";
    public const string VehicleSeatName = "BasisWorldInteraction-VehicleSeat";
    public const string ImageName = "BasisWorldInteraction-Image";
    public const string PoolName = "BasisWorldInteraction-PoolCue";
    public const string DirectTouchName = "BasisWorldInteraction-DirectTouch";

    private const string PoolCuePrefabPath = "Packages/com.basis.pooltable/Modules/BilliardsModule/Prefabs/Cue.prefab";

    public static GameObject Create(BasisScene content)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        Transform spawnPoint = content.SpawnPoint != null ? content.SpawnPoint : content.transform;
        var root = new GameObject(RootName);
        root.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

        CreatePickup(root.transform, PickupName, new Vector3(0f, 1.35f, 2f));
        CreateSeat(root.transform, SeatName, new Vector3(1.7f, 0.55f, 2f));
        CreateVehicle(root.transform);
        CreateImagePickup(root.transform);
        CreatePoolCue(root.transform);
        CreateDirectTouch(root.transform);

        EditorUtility.SetDirty(root);
        return root;
    }

    private static BasisPickupInteractable CreatePickup(Transform parent, string name, Vector3 localPosition)
    {
        GameObject pickupObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pickupObject.name = name;
        pickupObject.transform.SetParent(parent, false);
        pickupObject.transform.localPosition = localPosition;
        pickupObject.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
        SetInteractableLayer(pickupObject);

        Rigidbody rigidbody = pickupObject.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        BasisPickupInteractable pickup = pickupObject.AddComponent<BasisPickupInteractable>();
        pickup.RigidRef = rigidbody;
        pickup.AutoHold = BasisInteractableObject.BasisAutoHold.DesktopOnly;
        pickup.InputKey = BasisInteractableObject.BasisInputKey.PrimaryButtonGetState;
        pickup.GenerateColliderMesh = false;
        pickup.OnPickupUse = new UnityEngine.Events.UnityEvent<BasisPickUpUseMode>();
        return pickup;
    }

    private static void CreateSeat(Transform parent, string name, Vector3 localPosition)
    {
        GameObject seatObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        seatObject.name = name;
        seatObject.transform.SetParent(parent, false);
        seatObject.transform.localPosition = localPosition;
        seatObject.transform.localScale = new Vector3(0.8f, 0.3f, 0.8f);
        SetInteractableLayer(seatObject);
        seatObject.AddComponent<BasisSeat>();
    }

    private static void CreateVehicle(Transform parent)
    {
        GameObject vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vehicle.name = VehicleName;
        vehicle.transform.SetParent(parent, false);
        vehicle.transform.localPosition = new Vector3(-1.7f, 0.4f, 2f);
        vehicle.transform.localScale = new Vector3(1.2f, 0.35f, 1.7f);
        SetInteractableLayer(vehicle);
        vehicle.AddComponent<Rigidbody>().isKinematic = true;
        AddRequiredComponent(vehicle, "BasisVehicles", "Basis.Scripts.Vehicles.Main.BasisVehicleBody");

        GameObject seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        seat.name = VehicleSeatName;
        seat.transform.SetParent(vehicle.transform, false);
        seat.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        seat.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
        SetInteractableLayer(seat);
        AddRequiredComponent(seat, "BasisVehicles", "Basis.Scripts.Vehicles.Main.BasisVehiclePilotSeat");
    }

    private static void CreateImagePickup(Transform parent)
    {
        BasisPickupInteractable pickup = CreatePickup(parent, ImageName, new Vector3(3.4f, 1.35f, 2f));
        AddRequiredComponent(pickup.gameObject, "Basis.ImagePickup", "Basis.ImagePickup.BasisImagePickupObject");
    }

    private static void CreatePoolCue(Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PoolCuePrefabPath);
        if (prefab == null)
        {
            throw new InvalidOperationException($"Pool cue prefab is missing: {PoolCuePrefabPath}");
        }

        GameObject cue = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (cue == null)
        {
            throw new InvalidOperationException("Pool cue fixture could not be instantiated.");
        }

        cue.name = PoolName;
        cue.transform.localPosition = new Vector3(-3.4f, 1.35f, 2f);
        cue.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        SetLayerRecursively(cue);

        GameObject primaryGripObject = FindRequiredChild(cue, "primary");
        GameObject secondaryGripObject = FindRequiredChild(cue, "secondary");
        Component primaryGrip = primaryGripObject.GetComponent("CueGrip")
            ?? AddRequiredComponent(primaryGripObject, "BasisPoolTable", "CueGrip");
        Component secondaryGrip = secondaryGripObject.GetComponent("CueGrip")
            ?? AddRequiredComponent(secondaryGripObject, "BasisPoolTable", "CueGrip");
        Component[] cueGrips = { primaryGrip, secondaryGrip };
        if (cueGrips.Any(cueGrip => cueGrip == null))
        {
            throw new InvalidOperationException("Pool cue fixture must contain two CueGrip components.");
        }

        foreach (Component cueGrip in cueGrips)
        {
            BasisPickupInteractable pickup = cueGrip.GetComponent<BasisPickupInteractable>();
            if (pickup == null)
            {
                pickup = cueGrip.gameObject.AddComponent<BasisPickupInteractable>();
                Rigidbody rigidbody = cueGrip.GetComponent<Rigidbody>() ?? cueGrip.gameObject.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                pickup.RigidRef = rigidbody;
                pickup.GenerateColliderMesh = false;
                pickup.OnPickupUse = new UnityEngine.Events.UnityEvent<BasisPickUpUseMode>();
            }
            pickup.AutoHold = BasisInteractableObject.BasisAutoHold.DesktopOnly;
        }

        Component controller = cue.GetComponentsInChildren<Component>(true)
            .Single(component => component != null && component.GetType().Name == "CueController");
        var serializedController = new SerializedObject(controller);
        serializedController.FindProperty("primary").objectReferenceValue = primaryGripObject;
        serializedController.FindProperty("secondary").objectReferenceValue = secondaryGripObject;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject FindRequiredChild(GameObject parent, string name)
    {
        Transform child = parent.GetComponentsInChildren<Transform>(true)
            .SingleOrDefault(candidate => candidate.name == name);
        if (child == null)
        {
            throw new InvalidOperationException($"Pool cue fixture does not contain {name}.");
        }
        return child.gameObject;
    }

    private static void CreateDirectTouch(Transform parent)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            throw new InvalidOperationException("UI layer is required for DirectTouch.");
        }

        var canvasObject = new GameObject(DirectTouchName, typeof(RectTransform));
        canvasObject.layer = uiLayer;
        canvasObject.SetActive(false);

        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.SetParent(parent, false);
        canvasRect.localPosition = new Vector3(0f, 1.35f, 1.5f);
        canvasRect.localRotation = Quaternion.Euler(0f, 180f, 0f);
        canvasRect.localScale = Vector3.one * 0.001f;
        canvasRect.sizeDelta = new Vector2(400f, 400f);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        BasisGraphicUIRayCaster rayCaster = canvasObject.AddComponent<BasisGraphicUIRayCaster>();
        rayCaster.Canvas = canvas;
        rayCaster.AddCanvasCollider();
        BasisUIComponent uiComponent = canvasObject.AddComponent<BasisUIComponent>();
        uiComponent.Canvas = canvas;
        uiComponent.CanvasScaler = scaler;
        uiComponent.GraphicUIRayCaster = rayCaster;

        var buttonObject = new GameObject("DirectTouchButton", typeof(RectTransform));
        buttonObject.layer = uiLayer;
        var buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.SetParent(canvasRect, false);
        buttonRect.sizeDelta = new Vector2(300f, 300f);
        Image image = buttonObject.AddComponent<Image>();
        image.raycastTarget = true;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        canvasObject.SetActive(true);
    }

    private static Component AddRequiredComponent(GameObject target, string assemblyName, string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
            ?.GetType(typeName, false);
        if (type == null)
        {
            type = Assembly.Load(assemblyName).GetType(typeName, true);
        }
        return target.AddComponent(type);
    }

    private static void SetLayerRecursively(GameObject root)
    {
        SetInteractableLayer(root);
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject);
        }
    }

    private static void SetInteractableLayer(GameObject target)
    {
        int layer = LayerMask.NameToLayer("Interactable");
        if (layer >= 0)
        {
            target.layer = layer;
        }
    }
}
