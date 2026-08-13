using System.IO;
using Basis.Scripts.Device_Management.Devices.Web;
using NUnit.Framework;
using UnityEngine;

public class BasisWebXRInputTests
{
    private const string BrowserPluginPath = "Packages/com.basis.framework/Device Management/Devices/Web/BasisWebXR.jslib";
    private const string BackendPath = "Packages/com.basis.framework/Device Management/Devices/Web/BasisWebXRBackend.cs";

    [Test]
    public void ConvertsWebXRRightHandedPoseToUnityCoordinates()
    {
        BasisWebXRPose pose = new BasisWebXRPose
        {
            position = new Vector3(1f, 2f, 3f),
            rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f),
        };

        BasisWebXRInputMapping.ConvertToUnity(ref pose);

        Assert.That(pose.position, Is.EqualTo(new Vector3(1f, 2f, -3f)));
        Assert.That(pose.rotation, Is.EqualTo(new Quaternion(-0.1f, -0.2f, 0.3f, 0.9f)));
    }

    [Test]
    public void MapsXrStandardTriggerSqueezeThumbstickAndFaceButtons()
    {
        BasisWebXRSource source = new BasisWebXRSource
        {
            buttons = new[] { 0.75f, 0.6f, 0f, 1f, 1f, 0f },
            axes = new[] { 0.1f, 0.2f, -0.4f, 0.8f },
        };

        BasisWebXRControllerState state = BasisWebXRInputMapping.MapController(source);

        Assert.That(state.trigger, Is.EqualTo(0.75f));
        Assert.That(state.grip, Is.EqualTo(0.6f));
        Assert.That(state.primaryAxis, Is.EqualTo(new Vector2(-0.4f, 0.8f)));
        Assert.That(state.axisClick, Is.True);
        Assert.That(state.primaryButton, Is.True);
        Assert.That(state.secondaryButton, Is.False);
    }

    [Test]
    public void DerivesPinchFromThumbAndIndexTipsRelativeToHandScale()
    {
        BasisWebXRJoint[] joints = BasisWebXRInputMapping.CreateEmptyJointArray();
        joints[BasisWebXRInputMapping.WristIndex].position = Vector3.zero;
        joints[BasisWebXRInputMapping.IndexMetacarpalIndex].position = new Vector3(0f, 0.1f, 0f);
        joints[BasisWebXRInputMapping.ThumbTipIndex].position = new Vector3(0f, 0.1f, 0f);
        joints[BasisWebXRInputMapping.IndexTipIndex].position = new Vector3(0.005f, 0.1f, 0f);

        float pinch = BasisWebXRInputMapping.CalculatePinch(joints);

        Assert.That(pinch, Is.GreaterThan(0.9f));
    }

    [Test]
    public void BrowserBackendUsesStandardImmersiveSessionFrameAndInputApis()
    {
        string source = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("navigator.xr.isSessionSupported", source);
        StringAssert.Contains("navigator.xr.requestSession", source);
        StringAssert.Contains("requestReferenceSpace", source);
        StringAssert.Contains("session.requestAnimationFrame", source);
        StringAssert.Contains("frame.getViewerPose", source);
        StringAssert.Contains("frame.getPose", source);
        StringAssert.Contains("frame.getJointPose", source);
        StringAssert.Contains("inputSource.gamepad", source);
        StringAssert.Contains("window.basisWebXR", source);
        StringAssert.Contains("basis-webxr-enter", source);
        StringAssert.Contains("BasisWebXRPublishBasisState", source);
    }

    [Test]
    public void UnityBackendIsWebPlayerOnlyAndOwnsXRDeviceLifecycle()
    {
        string source = File.ReadAllText(BackendPath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("BasisWebXRInitialize", source);
        StringAssert.Contains("BasisWebXRHeadInput", source);
        StringAssert.Contains("BasisWebXRHandInput", source);
        StringAssert.Contains("EndImmersiveSession", source);
        StringAssert.Contains("basisWebXRE2E=1", source);
        StringAssert.Contains("BasisWebXRPublishBasisState", source);
    }
}
