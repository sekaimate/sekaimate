using System.IO;
using System.Linq;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.UI;
using Basis.Scripts.UI.UI_Panels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class BasisWorldInteractionFixtureBuilderTests
{
    private const string WebHarnessPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebWorldInteractionE2E.cs";
    private GameObject contentObject;
    private GameObject fixtureRoot;

    [TearDown]
    public void TearDown()
    {
        if (fixtureRoot != null)
        {
            Object.DestroyImmediate(fixtureRoot);
        }
        if (contentObject != null)
        {
            Object.DestroyImmediate(contentObject);
        }
    }

    [Test]
    public void CreateAddsEveryProductionInteractionTypeToWorldFixture()
    {
        contentObject = new GameObject("WorldContent");
        BasisScene content = contentObject.AddComponent<BasisScene>();
        content.SpawnPoint = contentObject.transform;

        fixtureRoot = BasisWorldInteractionFixtureBuilder.Create(content);

        string[] typeNames = fixtureRoot.GetComponentsInChildren<Component>(true)
            .Where(component => component != null)
            .Select(component => component.GetType().Name)
            .Distinct()
            .ToArray();
        CollectionAssert.IsSupersetOf(typeNames, new[]
        {
            nameof(BasisPickupInteractable),
            nameof(BasisSeat),
            "BasisVehiclePilotSeat",
            "BasisImagePickupObject",
            "CueGrip"
        });
    }

    [Test]
    public void PickupUsesDesktopAutoHoldAndIndependentUseKey()
    {
        contentObject = new GameObject("WorldContent");
        BasisScene content = contentObject.AddComponent<BasisScene>();
        content.SpawnPoint = contentObject.transform;

        fixtureRoot = BasisWorldInteractionFixtureBuilder.Create(content);
        BasisPickupInteractable pickup = GameObject.Find(BasisWorldInteractionFixtureBuilder.PickupName)
            .GetComponent<BasisPickupInteractable>();

        Assert.That(pickup.AutoHold, Is.EqualTo(BasisInteractableObject.BasisAutoHold.DesktopOnly));
        Assert.That(pickup.InputKey, Is.EqualTo(BasisInteractableObject.BasisInputKey.PrimaryButtonGetState));
        Assert.That(pickup.OnPickupUse, Is.Not.Null);
    }

    [Test]
    public void PoolCueUsesPickupOnCueGripObject()
    {
        contentObject = new GameObject("WorldContent");
        BasisScene content = contentObject.AddComponent<BasisScene>();
        content.SpawnPoint = contentObject.transform;

        fixtureRoot = BasisWorldInteractionFixtureBuilder.Create(content);
        Component[] cueGrips = fixtureRoot.GetComponentsInChildren<Component>(true)
            .Where(component => component != null && component.GetType().Name == "CueGrip")
            .ToArray();

        Assert.That(cueGrips, Has.Length.EqualTo(2));
        Assert.That(cueGrips.All(cueGrip => cueGrip.GetComponent<BasisPickupInteractable>() != null), Is.True);
    }

    [Test]
    public void PoolCueControllerReferencesCueGripComponents()
    {
        contentObject = new GameObject("WorldContent");
        BasisScene content = contentObject.AddComponent<BasisScene>();
        content.SpawnPoint = contentObject.transform;

        fixtureRoot = BasisWorldInteractionFixtureBuilder.Create(content);
        Component controller = fixtureRoot.GetComponentsInChildren<Component>(true)
            .Single(component => component != null && component.GetType().Name == "CueController");
        var serializedController = new SerializedObject(controller);

        GameObject primary = serializedController.FindProperty("primary").objectReferenceValue as GameObject;
        GameObject secondary = serializedController.FindProperty("secondary").objectReferenceValue as GameObject;

        Assert.That(primary, Is.Not.Null);
        Assert.That(primary.GetComponent("CueGrip"), Is.Not.Null);
        Assert.That(secondary, Is.Not.Null);
        Assert.That(secondary.GetComponent("CueGrip"), Is.Not.Null);
    }

    [Test]
    public void DirectTouchTargetUsesProductionWorldSpaceUi()
    {
        contentObject = new GameObject("WorldContent");
        BasisScene content = contentObject.AddComponent<BasisScene>();
        content.SpawnPoint = contentObject.transform;

        fixtureRoot = BasisWorldInteractionFixtureBuilder.Create(content);
        GameObject target = GameObject.Find(BasisWorldInteractionFixtureBuilder.DirectTouchName);

        Assert.That(target, Is.Not.Null);
        Assert.That(target.layer, Is.EqualTo(LayerMask.NameToLayer("UI")));
        Assert.That(target.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
        Assert.That(target.GetComponent<CanvasScaler>(), Is.Not.Null);
        Assert.That(target.GetComponent<BasisGraphicUIRayCaster>(), Is.Not.Null);
        Assert.That(target.GetComponent<BasisUIComponent>(), Is.Not.Null);
        Assert.That(target.GetComponent<BoxCollider>(), Is.Not.Null);
        Assert.That(target.GetComponentInChildren<Button>(), Is.Not.Null);
    }

    [Test]
    public void WebHarnessImportsAvatarTransformMapping()
    {
        string source = File.ReadAllText(WebHarnessPath);

        StringAssert.Contains("using Basis.Scripts.Common;", source);
        StringAssert.Contains("BasisTransformMapping mapping = BasisLocalAvatarDriver.Mapping;", source);
    }
}
