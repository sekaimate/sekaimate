using System.Linq;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Interactions;
using NUnit.Framework;
using UnityEngine;

public class BasisWorldInteractionFixtureBuilderTests
{
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
}
