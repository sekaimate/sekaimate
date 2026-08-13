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
    markPlaybackPushed: function(sampleCount) {
      if (sampleCount <= 0) return;
      BasisWebAudioDiagnosticsState.ensureInstalled();
      BasisWebAudioDiagnosticsState.values.playbackFramesPushed++;
      BasisWebAudioDiagnosticsState.values.playbackSamplesPushed += sampleCount;
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
        await BasisWebAudio.ensureInitialized();
        await BasisWebAudio.context.resume();
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

  BasisWebAudioPlaybackCreateSink__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackCreateSink: function() {
    return BasisWebAudio.nextSinkId++;
  },

  BasisWebAudioPlaybackPush__deps: ['$BasisWebAudio'],
  BasisWebAudioPlaybackPush: function(sinkId, samples, sampleCount) {
    if (!BasisWebAudio.playbackNode || sampleCount <= 0) return;
    var copy = HEAPF32.slice(samples >> 2, (samples >> 2) + sampleCount);
    BasisWebAudio.playbackNode.port.postMessage({ type: 'samples', sinkId: sinkId, samples: copy }, [copy.buffer]);
    BasisWebAudioDiagnosticsState.markPlaybackPushed(sampleCount);
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
});
