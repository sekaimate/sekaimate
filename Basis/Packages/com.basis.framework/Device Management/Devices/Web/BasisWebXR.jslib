mergeInto(LibraryManager.library, {
  $BasisWebXRRuntime: {
    jointNames: [
      'wrist',
      'thumb-metacarpal',
      'thumb-phalanx-proximal',
      'thumb-phalanx-distal',
      'thumb-tip',
      'index-finger-metacarpal',
      'index-finger-phalanx-proximal',
      'index-finger-phalanx-intermediate',
      'index-finger-phalanx-distal',
      'index-finger-tip',
      'middle-finger-metacarpal',
      'middle-finger-phalanx-proximal',
      'middle-finger-phalanx-intermediate',
      'middle-finger-phalanx-distal',
      'middle-finger-tip',
      'ring-finger-metacarpal',
      'ring-finger-phalanx-proximal',
      'ring-finger-phalanx-intermediate',
      'ring-finger-phalanx-distal',
      'ring-finger-tip',
      'pinky-finger-metacarpal',
      'pinky-finger-phalanx-proximal',
      'pinky-finger-phalanx-intermediate',
      'pinky-finger-phalanx-distal',
      'pinky-finger-tip',
    ],
    session: null,
    referenceSpace: null,
    referenceSpaceName: '',
    frame: 0,
    supported: false,
    lastError: '',
    snapshot: {
      schemaVersion: 1,
      frame: 0,
      supported: false,
      sessionActive: false,
      referenceSpace: '',
      head: { valid: false, position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0, w: 1 } },
      sources: [],
    },
    pose: function(xrPose) {
      if (!xrPose) {
        return { valid: false, position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0, w: 1 } };
      }
      var position = xrPose.transform.position;
      var rotation = xrPose.transform.orientation;
      return {
        valid: true,
        position: { x: position.x, y: position.y, z: position.z },
        rotation: { x: rotation.x, y: rotation.y, z: rotation.z, w: rotation.w },
      };
    },
    joint: function(frame, jointSpace, name) {
      var jointPose = frame.getJointPose(jointSpace, BasisWebXRRuntime.referenceSpace);
      if (!jointPose) {
        return {
          name: name,
          valid: false,
          position: { x: 0, y: 0, z: 0 },
          rotation: { x: 0, y: 0, z: 0, w: 1 },
          radius: 0,
        };
      }
      var pose = BasisWebXRRuntime.pose(jointPose);
      return {
        name: name,
        valid: true,
        position: pose.position,
        rotation: pose.rotation,
        radius: jointPose.radius || 0,
      };
    },
    source: function(frame, inputSource) {
      var gripPose = inputSource.gripSpace
        ? frame.getPose(inputSource.gripSpace, BasisWebXRRuntime.referenceSpace)
        : null;
      var targetRayPose = frame.getPose(inputSource.targetRaySpace, BasisWebXRRuntime.referenceSpace);
      var joints = [];
      if (inputSource.hand) {
        BasisWebXRRuntime.jointNames.forEach(function(name) {
          var jointSpace = inputSource.hand.get(name);
          joints.push(BasisWebXRRuntime.joint(frame, jointSpace, name));
        });
      }
      var gamepad = inputSource.gamepad;
      return {
        handedness: inputSource.handedness || '',
        targetRayMode: inputSource.targetRayMode || '',
        profiles: Array.from(inputSource.profiles || []),
        hasGripPose: !!gripPose,
        gripPose: BasisWebXRRuntime.pose(gripPose),
        targetRayPose: BasisWebXRRuntime.pose(targetRayPose),
        handTracked: !!inputSource.hand && joints.length === BasisWebXRRuntime.jointNames.length,
        joints: joints,
        buttons: gamepad ? Array.from(gamepad.buttons, function(button) { return button.value; }) : [],
        axes: gamepad ? Array.from(gamepad.axes) : [],
      };
    },
    publish: function(snapshot) {
      BasisWebXRRuntime.snapshot = snapshot;
      if (window.basisWebXR) {
        window.basisWebXR.supported = snapshot.supported;
        window.basisWebXR.sessionActive = snapshot.sessionActive;
        window.basisWebXR.referenceSpace = snapshot.referenceSpace;
        window.basisWebXR.frame = snapshot.frame;
        window.basisWebXR.lastError = BasisWebXRRuntime.lastError;
        window.basisWebXR.snapshot = snapshot;
      }
    },
    onFrame: function(time, frame) {
      var session = frame.session;
      session.requestAnimationFrame(BasisWebXRRuntime.onFrame);
      if (!BasisWebXRRuntime.referenceSpace) {
        return;
      }
      var viewerPose = frame.getViewerPose(BasisWebXRRuntime.referenceSpace);
      var sources = Array.from(session.inputSources, function(inputSource) {
        return BasisWebXRRuntime.source(frame, inputSource);
      });
      BasisWebXRRuntime.frame += 1;
      BasisWebXRRuntime.publish({
        schemaVersion: 1,
        frame: BasisWebXRRuntime.frame,
        supported: BasisWebXRRuntime.supported,
        sessionActive: true,
        referenceSpace: BasisWebXRRuntime.referenceSpaceName,
        head: BasisWebXRRuntime.pose(viewerPose),
        sources: sources,
      });
    },
    requestReferenceSpace: async function(session) {
      var names = ['local-floor', 'bounded-floor', 'local'];
      for (var index = 0; index < names.length; index += 1) {
        try {
          var space = await session.requestReferenceSpace(names[index]);
          BasisWebXRRuntime.referenceSpaceName = names[index];
          return space;
        } catch (error) {
        }
      }
      throw new Error('No supported WebXR local reference space was found.');
    },
    enter: async function() {
      if (BasisWebXRRuntime.session) {
        return true;
      }
      if (!navigator.xr) {
        throw new Error('WebXR is unavailable.');
      }
      try {
        var session = await navigator.xr.requestSession('immersive-vr', {
          optionalFeatures: ['local-floor', 'bounded-floor', 'hand-tracking'],
        });
        BasisWebXRRuntime.session = session;
        BasisWebXRRuntime.referenceSpace = await BasisWebXRRuntime.requestReferenceSpace(session);
        BasisWebXRRuntime.lastError = '';
        session.addEventListener('end', BasisWebXRRuntime.onSessionEnd, { once: true });
        session.addEventListener('inputsourceschange', function() {});
        var button = document.getElementById('basis-webxr-enter');
        if (button) {
          button.hidden = true;
        }
        session.requestAnimationFrame(BasisWebXRRuntime.onFrame);
        return true;
      } catch (error) {
        BasisWebXRRuntime.lastError = error instanceof Error ? error.message : String(error);
        BasisWebXRRuntime.publish(BasisWebXRRuntime.snapshot);
        throw error;
      }
    },
    exit: async function() {
      if (BasisWebXRRuntime.session) {
        await BasisWebXRRuntime.session.end();
      }
    },
    onSessionEnd: function() {
      BasisWebXRRuntime.session = null;
      BasisWebXRRuntime.referenceSpace = null;
      BasisWebXRRuntime.referenceSpaceName = '';
      BasisWebXRRuntime.publish({
        schemaVersion: 1,
        frame: BasisWebXRRuntime.frame,
        supported: BasisWebXRRuntime.supported,
        sessionActive: false,
        referenceSpace: '',
        head: BasisWebXRRuntime.pose(null),
        sources: [],
      });
      var button = document.getElementById('basis-webxr-enter');
      if (button) {
        button.hidden = !BasisWebXRRuntime.supported;
      }
    },
    installButton: function() {
      if (document.getElementById('basis-webxr-enter')) {
        return;
      }
      var button = document.createElement('button');
      button.id = 'basis-webxr-enter';
      button.type = 'button';
      button.textContent = 'Enter XR';
      button.hidden = true;
      button.style.position = 'fixed';
      button.style.right = '16px';
      button.style.bottom = '16px';
      button.style.zIndex = '2147483647';
      button.style.minWidth = '112px';
      button.style.minHeight = '48px';
      button.addEventListener('click', function() {
        BasisWebXRRuntime.enter().catch(function() {});
      });
      document.body.appendChild(button);
    },
    initialize: function() {
      BasisWebXRRuntime.installButton();
      window.basisWebXR = {
        schemaVersion: 1,
        supported: false,
        sessionActive: false,
        referenceSpace: '',
        frame: 0,
        lastError: '',
        snapshot: BasisWebXRRuntime.snapshot,
        enter: BasisWebXRRuntime.enter,
        exit: BasisWebXRRuntime.exit,
      };
      if (!navigator.xr) {
        BasisWebXRRuntime.publish(BasisWebXRRuntime.snapshot);
        return;
      }
      navigator.xr.isSessionSupported('immersive-vr').then(function(supported) {
        BasisWebXRRuntime.supported = supported;
        var button = document.getElementById('basis-webxr-enter');
        if (button) {
          button.hidden = !supported;
        }
        var snapshot = BasisWebXRRuntime.snapshot;
        snapshot.supported = supported;
        BasisWebXRRuntime.publish(snapshot);
      }).catch(function(error) {
        BasisWebXRRuntime.lastError = error instanceof Error ? error.message : String(error);
        BasisWebXRRuntime.publish(BasisWebXRRuntime.snapshot);
      });
    },
  },

  BasisWebXRInitialize__deps: ['$BasisWebXRRuntime'],
  BasisWebXRInitialize: function() {
    BasisWebXRRuntime.initialize();
  },

  BasisWebXRGetSnapshot__deps: ['$BasisWebXRRuntime'],
  BasisWebXRGetSnapshot: function() {
    var json = JSON.stringify(BasisWebXRRuntime.snapshot);
    var size = lengthBytesUTF8(json) + 1;
    var pointer = _malloc(size);
    stringToUTF8(json, pointer, size);
    return pointer;
  },

  BasisWebXRReleaseSnapshot: function(pointer) {
    _free(pointer);
  },

  BasisWebXREndSession__deps: ['$BasisWebXRRuntime'],
  BasisWebXREndSession: function() {
    BasisWebXRRuntime.exit().catch(function() {});
  },

  BasisWebXRPublishBasisState: function(diagnosticsJsonPointer) {
    if (!window.basisWebXR) {
      return;
    }
    window.basisWebXR.basisState = JSON.parse(UTF8ToString(diagnosticsJsonPointer));
  },
});
