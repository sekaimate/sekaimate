mergeInto(LibraryManager.library, {
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
        permissionGranted: captureState === 3,
        capturePcmFrames: 0,
        capturePcmSamples: 0,
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
          if (values.playbackFramesPushed <= 0 || values.playbackSamplesPushed <= 0) failures.push('audio-worklet-push');
          if (values.playbackNonSilentFramesPushed <= 0 || values.playbackPeak <= 0) failures.push('audible-pcm');
          return { passed: failures.length === 0, failures: failures, snapshot: Object.assign({}, values) };
        },
      };
    },
    markCaptureState: function(state) {
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.captureState = state;
      if (state === 3) {
        BasisWebAudioDiagnosticsState.values.permissionGranted = true;
      }
    },
    markCapturePcm: function(sampleCount) {
      if (sampleCount <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.capturePcmFrames++;
      BasisWebAudioDiagnosticsState.values.capturePcmSamples += sampleCount;
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
    stream: null,
    source: null,
    onStateChanged: null,
    onPcm: null,
    initialized: false,
    initializing: null,
    captureRequested: false,
    captureRequesting: false,
    nextSinkId: 1,
    hiddenState: 0,
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
    createWorkletUrl: function() {
      var source = [
        "class BasisCaptureProcessor extends AudioWorkletProcessor {",
        "  process(inputs) {",
        "    const input = inputs[0];",
        "    if (input.length > 0 && input[0].length > 0) {",
        "      const samples = new Float32Array(input[0]);",
        "      this.port.postMessage(samples, [samples.buffer]);",
        "    }",
        "    return true;",
        "  }",
        "}",
        "registerProcessor('basis-capture-processor', BasisCaptureProcessor);",
        "class BasisPlaybackProcessor extends AudioWorkletProcessor {",
        "  constructor() {",
        "    super();",
        "    this.sources = new Map();",
        "    this.port.onmessage = (event) => {",
        "      const message = event.data;",
        "      if (message.type === 'clear') {",
        "        this.sources.clear();",
        "        return;",
        "      }",
        "      if (message.type === 'remove') {",
        "        this.sources.delete(message.sinkId);",
        "        return;",
        "      }",
        "      let source = this.sources.get(message.sinkId);",
        "      if (!source) {",
        "        source = { chunks: [], offset: 0 };",
        "        this.sources.set(message.sinkId, source);",
        "      }",
        "      source.chunks.push(message.samples);",
        "      if (source.chunks.length > 10) { source.chunks.shift(); source.offset = 0; }",
        "    };",
        "  }",
        "  process(inputs, outputs) {",
        "    const output = outputs[0][0];",
        "    output.fill(0);",
        "    for (const source of this.sources.values()) {",
        "      for (let index = 0; index < output.length; index++) {",
        "        while (source.chunks.length > 0 && source.offset >= source.chunks[0].length) {",
        "          source.chunks.shift();",
        "          source.offset = 0;",
        "        }",
        "        if (source.chunks.length === 0) break;",
        "        output[index] += source.chunks[0][source.offset++];",
        "      }",
        "    }",
        "    return true;",
        "  }",
        "}",
        "registerProcessor('basis-playback-processor', BasisPlaybackProcessor);",
      ].join("\n");
      return URL.createObjectURL(new Blob([source], { type: 'application/javascript' }));
    },
    ensureInitialized: function() {
      if (BasisWebAudio.initialized) {
        return Promise.resolve();
      }
      if (BasisWebAudio.initializing) {
        return BasisWebAudio.initializing;
      }
      if (!window.AudioContext || !window.AudioWorkletNode || !navigator.mediaDevices) {
        BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
        return Promise.reject(new Error('Required Web Audio APIs are unavailable'));
      }
      BasisWebAudio.initializing = (async function() {
        BasisWebAudio.context = new AudioContext({ sampleRate: 48000 });
        if (BasisWebAudio.context.sampleRate !== BasisWebAudio.sampleRate) {
          BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
          throw new Error('A 48000 Hz AudioContext is required');
        }
        var workletUrl = BasisWebAudio.createWorkletUrl();
        try {
          await BasisWebAudio.context.audioWorklet.addModule(workletUrl);
        } finally {
          URL.revokeObjectURL(workletUrl);
        }
        BasisWebAudio.captureNode = new AudioWorkletNode(BasisWebAudio.context, 'basis-capture-processor', {
          numberOfInputs: 1,
          numberOfOutputs: 1,
          outputChannelCount: [1],
          channelCount: 1,
          channelCountMode: 'explicit',
        });
        BasisWebAudio.captureNode.port.onmessage = function(event) {
          if (!BasisWebAudio.onPcm) return;
          var samples = event.data;
          BasisWebAudioDiagnosticsState.markCapturePcm(samples.length);
          var pointer = _malloc(samples.length * 4);
          HEAPF32.set(samples, pointer >> 2);
          BasisWebAudio.onPcm(pointer, samples.length);
          _free(pointer);
        };
        BasisWebAudio.captureNode.connect(BasisWebAudio.context.destination);
        BasisWebAudio.playbackNode = new AudioWorkletNode(BasisWebAudio.context, 'basis-playback-processor', {
          numberOfInputs: 0,
          numberOfOutputs: 1,
          outputChannelCount: [1],
        });
        BasisWebAudio.playbackNode.connect(BasisWebAudio.context.destination);
        BasisWebAudio.initialized = true;
        BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
      })().catch(function(error) {
        BasisWebAudio.initializing = null;
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
      if (!navigator.userActivation || !navigator.userActivation.isActive) {
        BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
        return;
      }
      BasisWebAudio.captureRequesting = true;
      BasisWebAudio.notifyState(BasisWebAudio.State.RequestingPermission);
      try {
        var resumePromise = BasisWebAudio.context ? BasisWebAudio.context.resume() : null;
        await BasisWebAudio.ensureInitialized();
        if (!resumePromise) {
          BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
          return;
        }
        await resumePromise;
        if (BasisWebAudio.stream) {
          BasisWebAudio.stream.getAudioTracks().forEach(function(track) { track.enabled = true; });
          BasisWebAudio.notifyState(BasisWebAudio.State.Running);
          return;
        }
        var stream = await navigator.mediaDevices.getUserMedia({
          audio: {
            sampleRate: 48000,
            channelCount: 1,
            echoCancellation: false,
            noiseSuppression: false,
            autoGainControl: false,
          },
          video: false,
        });
        BasisWebAudio.stream = stream;
        BasisWebAudio.source = BasisWebAudio.context.createMediaStreamSource(stream);
        BasisWebAudio.source.connect(BasisWebAudio.captureNode);
        BasisWebAudio.notifyState(BasisWebAudio.State.Running);
      } catch (error) {
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
      if (BasisWebAudio.captureRequested) {
        BasisWebAudio.requestCapture();
        return;
      }
      BasisWebAudio.ensureInitialized().then(function() {
        return BasisWebAudio.context.resume();
      }).catch(function() {
        BasisWebAudio.notifyState(BasisWebAudio.State.Unavailable);
      });
    },
    handleVisibilityChanged: function() {
      if (!BasisWebAudio.context) return;
      if (document.hidden) {
        BasisWebAudio.hiddenState = BasisWebAudio.stream ? 1 : 0;
        if (BasisWebAudio.stream) {
          BasisWebAudio.stream.getAudioTracks().forEach(function(track) { track.enabled = false; });
        }
        BasisWebAudio.context.suspend();
        if (BasisWebAudio.playbackNode) {
          BasisWebAudio.playbackNode.port.postMessage({ type: 'clear' });
        }
        BasisWebAudio.notifyState(BasisWebAudio.State.Suspended);
        return;
      }
      BasisWebAudio.context.resume().then(function() {
        if (BasisWebAudio.hiddenState && BasisWebAudio.stream) {
          BasisWebAudio.stream.getAudioTracks().forEach(function(track) { track.enabled = true; });
          BasisWebAudio.notifyState(BasisWebAudio.State.Running);
        }
        BasisWebAudio.hiddenState = 0;
      }).catch(function() {
        BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
      });
    },
  },

  BasisWebAudioInitialize__deps: ['$BasisWebAudio', '$BasisWebAudioDiagnosticsState'],
  BasisWebAudioInitialize: function(onStateChanged, onPcm) {
    BasisWebAudioDiagnosticsState.ensureInstalled();
    BasisWebAudio.onStateChanged = function(state) {
      {{{ makeDynCall('vi', 'onStateChanged') }}}(state);
    };
    BasisWebAudio.onPcm = function(samples, sampleCount) {
      {{{ makeDynCall('vii', 'onPcm') }}}(samples, sampleCount);
    };
    if (!BasisWebAudio.initialized && !BasisWebAudio.initializing) {
      document.addEventListener('visibilitychange', BasisWebAudio.handleVisibilityChanged);
      document.addEventListener('pointerdown', BasisWebAudio.resumeFromGesture);
      document.addEventListener('keydown', BasisWebAudio.resumeFromGesture);
    }
    BasisWebAudio.ensureInitialized().catch(function() {});
  },

  BasisWebAudioCaptureRequestFromUserGesture__deps: ['$BasisWebAudio'],
  BasisWebAudioCaptureRequestFromUserGesture: function() {
    BasisWebAudio.captureRequested = true;
    if (!navigator.userActivation || !navigator.userActivation.isActive) {
      BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);
      return 0;
    }
    BasisWebAudio.requestCapture();
    return 1;
  },

  BasisWebAudioCaptureStop__deps: ['$BasisWebAudio'],
  BasisWebAudioCaptureStop: function() {
    BasisWebAudio.stopCapture();
  },

  BasisWebAudioPlayMicrophoneToggleSound__deps: ['$BasisWebAudio'],
  BasisWebAudioPlayMicrophoneToggleSound: function(muted, volume) {
    BasisWebAudio.ensureInitialized().then(function() {
      var context = BasisWebAudio.context;
      var oscillator = context.createOscillator();
      var gain = context.createGain();
      var startTime = context.currentTime;
      var duration = 0.08;
      oscillator.type = 'sine';
      oscillator.frequency.setValueAtTime(muted ? 440 : 660, startTime);
      oscillator.frequency.exponentialRampToValueAtTime(muted ? 330 : 880, startTime + duration);
      gain.gain.setValueAtTime(Math.max(0, Math.min(1, volume)) * 0.12, startTime);
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
        context.resume();
      }
    }).catch(function() {});
  },

  BasisWebAudioPlaybackCreateSink__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackCreateSink: function() {
    return BasisWebAudio.nextSinkId++;
  },

  BasisWebAudioPlaybackPush__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackPush: function(sinkId, samples, sampleCount, peak) {
    if (!BasisWebAudio.playbackNode || sampleCount <= 0) return;
    var copy = HEAPF32.slice(samples >> 2, (samples >> 2) + sampleCount);
    BasisWebAudio.playbackNode.port.postMessage({ type: 'samples', sinkId: sinkId, samples: copy }, [copy.buffer]);
    BasisWebAudioDiagnosticsState.markPlaybackPushed(sampleCount, peak);
  },

  BasisWebAudioPlaybackRemoveSink__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackRemoveSink: function(sinkId) {
    if (!BasisWebAudio.playbackNode) return;
    BasisWebAudio.playbackNode.port.postMessage({ type: 'remove', sinkId: sinkId });
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
