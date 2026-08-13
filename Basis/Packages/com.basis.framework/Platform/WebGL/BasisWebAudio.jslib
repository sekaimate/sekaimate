mergeInto(LibraryManager.library, {
  $BasisWebAudioFeedback: {
    context: null,
    getContext: function() {
      if (!BasisWebAudioFeedback.context && typeof WEBAudio !== 'undefined') {
        BasisWebAudioFeedback.context = WEBAudio.audioContext;
      }
      return BasisWebAudioFeedback.context;
    },
    play: function(sound, volume) {
      var context = BasisWebAudioFeedback.getContext();
      if (!context) return;
      var startTime = context.currentTime;
      var frequencies = [760, 420, 880];
      var durations = [0.035, 0.055, 0.1];
      var startFrequency = frequencies[sound] || frequencies[0];
      var duration = durations[sound] || durations[0];
      var oscillator = context.createOscillator();
      var gain = context.createGain();
      oscillator.type = 'sine';
      oscillator.frequency.setValueAtTime(startFrequency, startTime);
      oscillator.frequency.exponentialRampToValueAtTime(startFrequency * 0.82, startTime + duration);
      gain.gain.setValueAtTime(Math.max(0.0001, Math.min(1, volume) * 0.08), startTime);
      gain.gain.exponentialRampToValueAtTime(0.0001, startTime + duration);
      oscillator.connect(gain);
      gain.connect(context.destination);
      oscillator.start(startTime);
      oscillator.stop(startTime + duration);
      oscillator.onended = function() {
        oscillator.disconnect();
        gain.disconnect();
      };
      if (context.state === 'suspended') {
        context.resume().catch(function() {});
      }
    },
  },

  $BasisWebAudioDiagnosticsState: {
    schemaVersion: 1,
    values: null,
    reset: function() {
      var captureState = BasisWebAudioDiagnosticsState.values
        ? BasisWebAudioDiagnosticsState.values.captureState
        : 0;
      BasisWebAudioDiagnosticsState.values = {
        schemaVersion: BasisWebAudioDiagnosticsState.schemaVersion,
        captureState: captureState,
        captureStage: 'idle',
        captureError: '',
        permissionGranted: captureState === 3,
        capturePcmFrames: 0,
        capturePcmSamples: 0,
        capturePeak: 0,
        activeDeviceName: '',
        captureSampleRate: 0,
        opusEncodedPackets: 0,
        opusEncodedBytes: 0,
        networkPacketsSent: 0,
        networkBytesSent: 0,
        networkPacketsReceived: 0,
        networkBytesReceived: 0,
        opusDecodedFrames: 0,
        opusDecodedSamples: 0,
        playbackFramesPushed: 0,
        playbackSamplesPushed: 0,
        playbackNonSilentFramesPushed: 0,
        playbackPeak: 0,
        muted: false,
        muteChanges: 0,
        talkMode: 0,
        talkModeChanges: 0,
        remoteMuted: false,
        remoteMuteChanges: 0,
        remoteTalkMode: 0,
        remoteTalkModeChanges: 0,
        localVisemeFrames: 0,
        localVisemePeak: 0,
        remoteVisemeFrames: 0,
        remoteVisemePeak: 0,
      };
    },
    ensureInstalled: function() {
      if (!BasisWebAudioDiagnosticsState.values) {
        BasisWebAudioDiagnosticsState.reset();
      }
      if (globalThis.BasisWebAudioDiagnostics) {
        return;
      }
      globalThis.BasisWebAudioDiagnostics = {
        schemaVersion: BasisWebAudioDiagnosticsState.schemaVersion,
        reset: function() {
          BasisWebAudioDiagnosticsState.reset();
        },
        snapshot: function() {
          return Object.assign({}, BasisWebAudioDiagnosticsState.values);
        },
        selectInputDevice: function(namePart) {
          var normalizedName = String(namePart || '').toLocaleLowerCase();
          var device = BasisWebAudio.deviceEntries.find(function(entry) {
            return entry.name.toLocaleLowerCase().includes(normalizedName);
          });
          if (!device) return false;
          BasisWebAudio.selectDevice(device.name);
          return true;
        },
        verifySender: function() {
          var values = BasisWebAudioDiagnosticsState.values;
          var failures = [];
          if (!values.permissionGranted || values.captureState !== 3) failures.push('microphone-permission');
          if (values.capturePcmFrames <= 0 || values.capturePcmSamples <= 0) failures.push('capture-pcm');
          if (values.opusEncodedPackets <= 0 || values.opusEncodedBytes <= 0) failures.push('opus-encode');
          if (values.networkPacketsSent <= 0 || values.networkBytesSent <= 0) failures.push('network-send');
          return { passed: failures.length === 0, failures: failures, snapshot: Object.assign({}, values) };
        },
        verifyReceiver: function() {
          var values = BasisWebAudioDiagnosticsState.values;
          var failures = [];
          if (values.networkPacketsReceived <= 0 || values.networkBytesReceived <= 0) failures.push('network-receive');
          if (values.opusDecodedFrames <= 0 || values.opusDecodedSamples <= 0) failures.push('opus-decode');
          if (values.playbackFramesPushed <= 0 || values.playbackSamplesPushed <= 0) failures.push('browser-audio-push');
          if (values.playbackNonSilentFramesPushed <= 0 || values.playbackPeak <= 0) failures.push('audible-pcm');
          return { passed: failures.length === 0, failures: failures, snapshot: Object.assign({}, values) };
        },
      };
      BasisWebAudioDiagnosticsState.installOverlay();
    },
    installOverlay: function() {
      if (new URLSearchParams(window.location.search).get('basisVoiceDiagnostics') !== '1') return;
      var install = function() {
        if (document.getElementById('basis-voice-diagnostics')) return;
        var overlay = document.createElement('div');
        overlay.id = 'basis-voice-diagnostics';
        overlay.style.cssText = 'position:fixed;top:12px;right:12px;z-index:2147483647;pointer-events:none;padding:10px 12px;border-radius:8px;background:rgba(10,14,24,.92);color:#fff;font:12px/1.45 monospace;white-space:pre;box-shadow:0 2px 10px rgba(0,0,0,.4)';
        document.body.appendChild(overlay);
        var render = function() {
          var values = BasisWebAudioDiagnosticsState.values;
          var senderOk = globalThis.BasisWebAudioDiagnostics.verifySender().passed && values.capturePeak > 0;
          var receiverOk = globalThis.BasisWebAudioDiagnostics.verifyReceiver().passed;
          overlay.style.border = '2px solid ' + (senderOk && receiverOk ? '#35d07f' : '#e0a72e');
          overlay.textContent = (senderOk && receiverOk ? 'VOICE OK' : 'VOICE CHECKING')
            + '\nMic: ' + (values.activeDeviceName || 'permission required')
            + '\nInput: ' + values.capturePeak.toFixed(4) + ' @ ' + values.captureSampleRate + ' Hz'
            + '\nSent: ' + values.networkPacketsSent
            + '\nReceived: ' + values.networkPacketsReceived
            + '\nPlayback: ' + values.playbackPeak.toFixed(4);
        };
        render();
        window.setInterval(render, 250);
      };
      if (document.body) install();
      else window.addEventListener('DOMContentLoaded', install, { once: true });
    },
    markCaptureState: function(state) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.captureState = state;
      if (state === 3) {
        BasisWebAudioDiagnosticsState.values.permissionGranted = true;
      }
    },
    markCaptureStage: function(stage) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.captureStage = stage;
    },
    markCaptureError: function(error) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.captureError = error && error.message ? error.message : String(error || '');
    },
    markCapturePcm: function(samples) {
      if (!samples || samples.length <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.capturePcmFrames++;
      BasisWebAudioDiagnosticsState.values.capturePcmSamples += samples.length;
      var peak = 0;
      for (var index = 0; index < samples.length; index++) {
        peak = Math.max(peak, Math.abs(samples[index]));
      }
      BasisWebAudioDiagnosticsState.values.capturePeak = peak;
    },
    markOpusEncoded: function(encodedBytes) {
      if (encodedBytes <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.opusEncodedPackets++;
      BasisWebAudioDiagnosticsState.values.opusEncodedBytes += encodedBytes;
    },
    markNetworkSent: function(encodedBytes) {
      if (encodedBytes <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.networkPacketsSent++;
      BasisWebAudioDiagnosticsState.values.networkBytesSent += encodedBytes;
    },
    markNetworkReceived: function(encodedBytes) {
      if (encodedBytes <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.networkPacketsReceived++;
      BasisWebAudioDiagnosticsState.values.networkBytesReceived += encodedBytes;
    },
    markOpusDecoded: function(sampleCount) {
      if (sampleCount <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.opusDecodedFrames++;
      BasisWebAudioDiagnosticsState.values.opusDecodedSamples += sampleCount;
    },
    markPlaybackPushed: function(sampleCount, peak) {
      if (sampleCount <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.playbackFramesPushed++;
      BasisWebAudioDiagnosticsState.values.playbackSamplesPushed += sampleCount;
      if (peak > 0) {
        BasisWebAudioDiagnosticsState.values.playbackNonSilentFramesPushed++;
        if (peak > BasisWebAudioDiagnosticsState.values.playbackPeak) {
          BasisWebAudioDiagnosticsState.values.playbackPeak = peak;
        }
      }
    },
    markMuted: function(muted) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.muted = muted !== 0;
      BasisWebAudioDiagnosticsState.values.muteChanges++;
    },
    markTalkMode: function(talkMode) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.talkMode = talkMode;
      BasisWebAudioDiagnosticsState.values.talkModeChanges++;
    },
    markRemoteMuted: function(muted) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.remoteMuted = muted !== 0;
      BasisWebAudioDiagnosticsState.values.remoteMuteChanges++;
    },
    markRemoteTalkMode: function(talkMode) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.remoteTalkMode = talkMode;
      BasisWebAudioDiagnosticsState.values.remoteTalkModeChanges++;
    },
    markVisemeProcessed: function(isLocal, peak) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      var frameKey = isLocal !== 0 ? 'localVisemeFrames' : 'remoteVisemeFrames';
      var peakKey = isLocal !== 0 ? 'localVisemePeak' : 'remoteVisemePeak';
      BasisWebAudioDiagnosticsState.values[frameKey]++;
      if (peak > BasisWebAudioDiagnosticsState.values[peakKey]) {
        BasisWebAudioDiagnosticsState.values[peakKey] = peak;
      }
    },
  },

  $BasisWebAudio__deps: ['$BasisWebAudioDiagnosticsState'],
  $BasisWebAudio: {
    sampleRate: 48000,
    frameSize: 960,
    context: null,
    captureNode: null,
    playbackNode: null,
    playbackSources: new Map(),
    stream: null,
    source: null,
    onStateChanged: null,
    onPcm: null,
    onDevicesChanged: null,
    deviceEntries: [],
    selectedDeviceName: '',
    initialized: false,
    initializing: null,
    captureRequested: false,
    captureRequesting: false,
    nextSinkId: 1,
    State: {
      Idle: 0,
      AwaitingUserGesture: 1,
      RequestingPermission: 2,
      Running: 3,
      PermissionDenied: 4,
      Unavailable: 5,
      Suspended: 6,
    },
    notifyState: function(state) {
      BasisWebAudioDiagnosticsState.markCaptureState(state);
      if (BasisWebAudio.onStateChanged) {
        BasisWebAudio.onStateChanged(state);
      }
    },
    ensureContext: function() {
      if (BasisWebAudio.context) return BasisWebAudio.context;
      BasisWebAudioDiagnosticsState.markCaptureStage('audio-context-creating');
      BasisWebAudio.context = new window.AudioContext({
        latencyHint: 'interactive',
        sampleRate: BasisWebAudio.sampleRate,
      });
      if (BasisWebAudio.context.sampleRate !== BasisWebAudio.sampleRate) {
        var actualRate = BasisWebAudio.context.sampleRate;
        BasisWebAudio.context.close();
        BasisWebAudio.context = null;
        throw new Error('Browser created an unsupported audio sample rate: ' + actualRate + ' Hz');
      }
      return BasisWebAudio.context;
    },
    refreshDevices: async function(activeTrack) {
      var devices = await navigator.mediaDevices.enumerateDevices();
      var inputs = devices.filter(function(device) { return device.kind === 'audioinput'; });
      var names = {};
      BasisWebAudio.deviceEntries = inputs.map(function(device, index) {
        var baseName = device.label || (device.deviceId === 'default' ? 'Default microphone' : 'Microphone ' + (index + 1));
        var duplicateCount = names[baseName] || 0;
        names[baseName] = duplicateCount + 1;
        return {
          name: duplicateCount === 0 ? baseName : baseName + ' (' + (duplicateCount + 1) + ')',
          deviceId: device.deviceId,
        };
      });
      var activeDeviceId = activeTrack && activeTrack.getSettings ? activeTrack.getSettings().deviceId : '';
      var activeDevice = BasisWebAudio.deviceEntries.find(function(device) { return device.deviceId === activeDeviceId; });
      if (activeDevice) {
        BasisWebAudio.selectedDeviceName = activeDevice.name;
      }
      if (!BasisWebAudio.deviceEntries.some(function(device) { return device.name === BasisWebAudio.selectedDeviceName; })) {
        BasisWebAudio.selectedDeviceName = BasisWebAudio.deviceEntries.length > 0 ? BasisWebAudio.deviceEntries[0].name : '';
      }
      if (BasisWebAudio.onDevicesChanged) {
        var payload = JSON.stringify({ devices: BasisWebAudio.deviceEntries.map(function(device) { return device.name; }) });
        var pointer = stringToNewUTF8(payload);
        BasisWebAudio.onDevicesChanged(pointer);
        _free(pointer);
      }
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.activeDeviceName = activeTrack && activeTrack.label
        ? activeTrack.label
        : BasisWebAudio.selectedDeviceName;
    },
    captureConstraints: function() {
      var selected = BasisWebAudio.deviceEntries.find(function(device) {
        return device.name === BasisWebAudio.selectedDeviceName;
      });
      var audio = {
        sampleRate: 48000,
        channelCount: 1,
        echoCancellation: false,
        noiseSuppression: false,
        autoGainControl: false,
      };
      if (selected && selected.deviceId && selected.deviceId !== 'default') {
        audio.deviceId = { exact: selected.deviceId };
      }
      return { audio: audio, video: false };
    },
    selectDevice: function(deviceName) {
      if (BasisWebAudio.selectedDeviceName === deviceName) return;
      BasisWebAudio.selectedDeviceName = deviceName;
      if (!BasisWebAudio.stream) return;
      if (BasisWebAudio.source) {
        BasisWebAudio.source.disconnect();
        BasisWebAudio.source = null;
      }
      BasisWebAudio.stream.getTracks().forEach(function(track) { track.stop(); });
      BasisWebAudio.stream = null;
      BasisWebAudio.requestCapture();
    },
    ensureInitialized: function() {
      if (BasisWebAudio.initialized) {
        return Promise.resolve();
      }
      if (BasisWebAudio.initializing) {
        return BasisWebAudio.initializing;
      }
      if (!window.AudioContext || !navigator.mediaDevices) {
        BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
        return Promise.reject(new Error('Required Web Audio APIs are unavailable'));
      }
      BasisWebAudio.initializing = (async function() {
        BasisWebAudio.ensureContext();
        BasisWebAudioDiagnosticsState.markCaptureStage('context-resuming');
        await BasisWebAudio.context.resume();
        BasisWebAudioDiagnosticsState.markCaptureStage('context-running');
        BasisWebAudioDiagnosticsState.markCaptureStage('processor-creating');
        BasisWebAudio.captureNode = BasisWebAudio.context.createScriptProcessor(2048, 1, 1);
        BasisWebAudio.playbackNode = BasisWebAudio.captureNode;
        BasisWebAudio.captureNode.onaudioprocess = function(event) {
          var output = event.outputBuffer.getChannelData(0);
          output.fill(0);
          BasisWebAudio.playbackSources.forEach(function(source) {
            for (var index = 0; index < output.length; index++) {
              while (source.chunks.length > 0 && source.offset >= source.chunks[0].length) {
                source.chunks.shift();
                source.offset = 0;
              }
              if (source.chunks.length === 0) break;
              output[index] += source.chunks[0][source.offset++];
            }
          });

          if (!BasisWebAudio.source || !BasisWebAudio.onPcm || event.inputBuffer.numberOfChannels === 0) return;
          var samples = event.inputBuffer.getChannelData(0);
          BasisWebAudioDiagnosticsState.markCapturePcm(samples);
          var pointer = _malloc(samples.length * 4);
          HEAPF32.set(samples, pointer >> 2);
          BasisWebAudio.onPcm(pointer, samples.length);
          _free(pointer);
        };
        BasisWebAudio.captureNode.connect(BasisWebAudio.context.destination);
        BasisWebAudio.initialized = true;
        BasisWebAudioDiagnosticsState.markCaptureStage('audio-ready');
        BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
      })().catch(function(error) {
        BasisWebAudio.initializing = null;
        BasisWebAudioDiagnosticsState.markCaptureError(error);
        if (!BasisWebAudio.initialized) {
          BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
        }
        throw error;
      });
      return BasisWebAudio.initializing;
    },
    stopCapture: function() {
      BasisWebAudio.captureRequested = false;
      BasisWebAudio.captureRequesting = false;
      if (BasisWebAudio.source) {
        BasisWebAudio.source.disconnect();
        BasisWebAudio.source = null;
      }
      if (BasisWebAudio.stream) {
        BasisWebAudio.stream.getTracks().forEach(function(track) { track.stop(); });
        BasisWebAudio.stream = null;
      }
      BasisWebAudio.notifyState(BasisWebAudio.State.Idle);
    },
    requestCapture: async function() {
      if (!BasisWebAudio.captureRequested || BasisWebAudio.captureRequesting) {
        return;
      }
      BasisWebAudio.captureRequesting = true;
      BasisWebAudio.notifyState(BasisWebAudio.State.RequestingPermission);
      try {
        BasisWebAudioDiagnosticsState.markCaptureStage('permission-requested');
        var streamPromise = BasisWebAudio.stream ? null : navigator.mediaDevices.getUserMedia(BasisWebAudio.captureConstraints()).then(async function(stream) {
          BasisWebAudioDiagnosticsState.markCaptureStage('stream-ready');
          await BasisWebAudio.refreshDevices(stream.getAudioTracks()[0]);
          return stream;
        });
        await BasisWebAudio.ensureInitialized();
        BasisWebAudioDiagnosticsState.markCaptureStage('initialization-complete');
        BasisWebAudioDiagnosticsState.markCaptureStage('context-resuming');
        await BasisWebAudio.context.resume();
        BasisWebAudioDiagnosticsState.markCaptureStage('context-running');
        if (BasisWebAudio.stream) {
          BasisWebAudio.stream.getAudioTracks().forEach(function(track) { track.enabled = true; });
          BasisWebAudio.notifyState(BasisWebAudio.State.Running);
          return;
        }
        BasisWebAudioDiagnosticsState.markCaptureStage('stream-awaiting');
        var stream = await streamPromise;
        BasisWebAudioDiagnosticsState.markCaptureStage('stream-acquired');
        BasisWebAudio.stream = stream;
        BasisWebAudio.source = BasisWebAudio.context.createMediaStreamSource(stream);
        BasisWebAudioDiagnosticsState.values.captureSampleRate = BasisWebAudio.context.sampleRate;
        BasisWebAudioDiagnosticsState.markCaptureStage('source-created');
        BasisWebAudio.source.connect(BasisWebAudio.captureNode);
        BasisWebAudioDiagnosticsState.markCaptureStage('capture-running');
        BasisWebAudioDiagnosticsState.markCaptureError('');
        BasisWebAudio.notifyState(BasisWebAudio.State.Running);
      } catch (error) {
        BasisWebAudioDiagnosticsState.markCaptureError(error);
        BasisWebAudio.captureRequested = false;
        if (error && error.name === 'NotAllowedError') {
          BasisWebAudio.notifyState(BasisWebAudio.State.PermissionDenied);
        } else {
          BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
        }
      } finally {
        BasisWebAudio.captureRequesting = false;
      }
    },
    resumeFromGesture: function() {
      var context;
      try {
        context = BasisWebAudio.ensureContext();
      } catch (error) {
        BasisWebAudioDiagnosticsState.markCaptureError(error);
        BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
        return;
      }
      context.resume().then(function() {
        BasisWebAudioDiagnosticsState.markCaptureStage('context-running');
        return BasisWebAudio.ensureInitialized();
      }).then(function() {
        if (BasisWebAudio.captureRequested) BasisWebAudio.requestCapture();
      }).catch(function(error) {
        BasisWebAudioDiagnosticsState.markCaptureError(error);
        BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
      });
    },
    handleVisibilityChanged: function() {
      if (!BasisWebAudio.context || document.hidden) return;
      BasisWebAudio.context.resume().then(function() {
        if (BasisWebAudio.stream) {
          BasisWebAudio.stream.getAudioTracks().forEach(function(track) { track.enabled = true; });
          BasisWebAudio.notifyState(BasisWebAudio.State.Running);
        }
      }).catch(function() {
        BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
      });
    },
  },

  BasisWebAudioInitialize__deps: ['$BasisWebAudio', '$BasisWebAudioDiagnosticsState'],
  BasisWebAudioInitialize: function(onStateChanged, onPcm, onDevicesChanged) {
    BasisWebAudioDiagnosticsState.ensureInstalled();
    BasisWebAudio.onStateChanged = function(state) {
      {{{ makeDynCall('vi', 'onStateChanged') }}}(state);
    };
    BasisWebAudio.onPcm = function(samples, sampleCount) {
      {{{ makeDynCall('vii', 'onPcm') }}}(samples, sampleCount);
    };
    BasisWebAudio.onDevicesChanged = function(devicesJson) {
      {{{ makeDynCall('vi', 'onDevicesChanged') }}}(devicesJson);
    };
    if (!BasisWebAudio.initialized && !BasisWebAudio.initializing) {
      document.addEventListener('visibilitychange', BasisWebAudio.handleVisibilityChanged);
      document.addEventListener('pointerdown', BasisWebAudio.resumeFromGesture);
      document.addEventListener('keydown', BasisWebAudio.resumeFromGesture);
      navigator.mediaDevices.addEventListener('devicechange', BasisWebAudio.refreshDevices);
    }
    BasisWebAudio.refreshDevices().catch(function(error) {
      BasisWebAudioDiagnosticsState.markCaptureError(error);
    });
    BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
  },

  BasisWebAudioCaptureRequestFromUserGesture__deps: ['$BasisWebAudio'],
  BasisWebAudioCaptureRequestFromUserGesture: function() {
    BasisWebAudio.captureRequested = true;
    BasisWebAudio.requestCapture();
    return 1;
  },

  BasisWebAudioCaptureStop__deps: ['$BasisWebAudio'],
  BasisWebAudioCaptureStop: function() {
    BasisWebAudio.stopCapture();
  },

  BasisWebAudioSetCaptureDevice__deps: ['$BasisWebAudio'],
  BasisWebAudioSetCaptureDevice: function(deviceName) {
    BasisWebAudio.selectDevice(UTF8ToString(deviceName));
  },

  BasisWebAudioPlayUiSound__deps: ['$BasisWebAudioFeedback'],
  BasisWebAudioPlayUiSound: function(sound, volume) {
    BasisWebAudioFeedback.play(sound, volume);
  },

  BasisWebAudioPlayMicrophoneToggleSound__deps: ['$BasisWebAudioFeedback'],
  BasisWebAudioPlayMicrophoneToggleSound: function(muted, volume) {
    BasisWebAudioFeedback.play(muted ? 1 : 2, volume);
  },

  BasisWebAudioPlaybackCreateSink__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackCreateSink: function() {
    return BasisWebAudio.nextSinkId++;
  },

  BasisWebAudioPlaybackPush__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackPush: function(sinkId, samples, sampleCount, peak) {
    if (!BasisWebAudio.playbackNode || sampleCount <= 0) return;
    var copy = HEAPF32.slice(samples >> 2, (samples >> 2) + sampleCount);
    var source = BasisWebAudio.playbackSources.get(sinkId);
    if (!source) {
      source = { chunks: [], offset: 0 };
      BasisWebAudio.playbackSources.set(sinkId, source);
    }
    source.chunks.push(copy);
    if (source.chunks.length > 10) {
      source.chunks.shift();
      source.offset = 0;
    }
    BasisWebAudioDiagnosticsState.markPlaybackPushed(sampleCount, peak);
  },

  BasisWebAudioPlaybackRemoveSink__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackRemoveSink: function(sinkId) {
    BasisWebAudio.playbackSources.delete(sinkId);
  },

  BasisWebAudioDiagnosticsMarkOpusEncoded__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkOpusEncoded: function(encodedBytes) {
    BasisWebAudioDiagnosticsState.markOpusEncoded(encodedBytes);
  },

  BasisWebAudioDiagnosticsMarkNetworkSent__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkNetworkSent: function(encodedBytes) {
    BasisWebAudioDiagnosticsState.markNetworkSent(encodedBytes);
  },

  BasisWebAudioDiagnosticsMarkNetworkReceived__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkNetworkReceived: function(encodedBytes) {
    BasisWebAudioDiagnosticsState.markNetworkReceived(encodedBytes);
  },

  BasisWebAudioDiagnosticsMarkOpusDecoded__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkOpusDecoded: function(sampleCount) {
    BasisWebAudioDiagnosticsState.markOpusDecoded(sampleCount);
  },

  BasisWebAudioDiagnosticsMarkMuted__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkMuted: function(muted) {
    BasisWebAudioDiagnosticsState.markMuted(muted);
  },

  BasisWebAudioDiagnosticsMarkTalkMode__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkTalkMode: function(talkMode) {
    BasisWebAudioDiagnosticsState.markTalkMode(talkMode);
  },

  BasisWebAudioDiagnosticsMarkRemoteMuted__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkRemoteMuted: function(muted) {
    BasisWebAudioDiagnosticsState.markRemoteMuted(muted);
  },

  BasisWebAudioDiagnosticsMarkRemoteTalkMode__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkRemoteTalkMode: function(talkMode) {
    BasisWebAudioDiagnosticsState.markRemoteTalkMode(talkMode);
  },

  BasisWebAudioDiagnosticsMarkVisemeProcessed__deps: ['$BasisWebAudioDiagnosticsState'],
  BasisWebAudioDiagnosticsMarkVisemeProcessed: function(isLocal, peak) {
    BasisWebAudioDiagnosticsState.markVisemeProcessed(isLocal, peak);
  },
});
