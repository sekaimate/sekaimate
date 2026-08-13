mergeInto(LibraryManager.library, {
  $BasisMediaPipeWeb: {
    worker: null,
    stream: null,
    video: null,
    busy: false,
    ready: false,
    lastFrameTime: 0,
    frameInterval: 1000 / 30,
    onStateChanged: null,
    onResult: null,

    assetUrl: function(fileName) {
      return new URL("BasisMediaPipeWeb/" + fileName, document.baseURI).href;
    },

    notifyState: function(state) {
      if (BasisMediaPipeWeb.onStateChanged) {
        BasisMediaPipeWeb.onStateChanged(state);
      }
    },

    stop: function() {
      BasisMediaPipeWeb.ready = false;
      BasisMediaPipeWeb.busy = false;
      if (BasisMediaPipeWeb.worker) {
        BasisMediaPipeWeb.worker.terminate();
        BasisMediaPipeWeb.worker = null;
      }
      if (BasisMediaPipeWeb.stream) {
        BasisMediaPipeWeb.stream.getTracks().forEach(function(track) { track.stop(); });
        BasisMediaPipeWeb.stream = null;
      }
      if (BasisMediaPipeWeb.video) {
        BasisMediaPipeWeb.video.pause();
        BasisMediaPipeWeb.video.srcObject = null;
        BasisMediaPipeWeb.video.remove();
        BasisMediaPipeWeb.video = null;
      }
    },

    initialize: async function(config) {
      try {
        if (!window.isSecureContext || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia
            || !window.Worker || !window.createImageBitmap) {
          BasisMediaPipeWeb.notifyState(2);
          return;
        }

        BasisMediaPipeWeb.stream = await navigator.mediaDevices.getUserMedia({
          audio: false,
          video: {
            width: { ideal: config.width },
            height: { ideal: config.height },
            frameRate: { ideal: config.targetFps },
          },
        });
        var video = document.createElement("video");
        video.muted = true;
        video.playsInline = true;
        video.srcObject = BasisMediaPipeWeb.stream;
        video.style.display = "none";
        document.body.appendChild(video);
        BasisMediaPipeWeb.video = video;
        await video.play();

        var worker = new Worker(BasisMediaPipeWeb.assetUrl("BasisMediaPipeWorker.mjs"), { type: "module" });
        BasisMediaPipeWeb.worker = worker;
        worker.onmessage = function(event) {
          var message = event.data;
          if (message.type === "ready") {
            BasisMediaPipeWeb.ready = true;
            BasisMediaPipeWeb.notifyState(1);
            return;
          }
          if (message.type === "result") {
            BasisMediaPipeWeb.busy = false;
            var values = new Float32Array(message.values);
            var pointer = _malloc(values.byteLength);
            HEAPF32.set(values, pointer >> 2);
            BasisMediaPipeWeb.onResult(pointer, values.length);
            _free(pointer);
            return;
          }
          if (message.type === "error") {
            console.error("BasisMediaPipe(web): " + message.message);
            BasisMediaPipeWeb.notifyState(3);
            BasisMediaPipeWeb.stop();
          }
        };
        worker.onerror = function(event) {
          console.error("BasisMediaPipe(web) worker: " + event.message);
          BasisMediaPipeWeb.notifyState(3);
          BasisMediaPipeWeb.stop();
        };
        worker.postMessage({
          type: "initialize",
          config: config,
          assetRoot: BasisMediaPipeWeb.assetUrl(""),
        });
      } catch (error) {
        console.error("BasisMediaPipe(web): " + error);
        BasisMediaPipeWeb.notifyState(error && error.name === "NotAllowedError" ? 4 : 3);
        BasisMediaPipeWeb.stop();
      }
    },
  },

  BasisMediaPipeWebInitialize: function(
      enableFace, enableHands, enablePose, mirror, swapHands, width, height, targetFps, onStateChanged, onResult) {
    BasisMediaPipeWeb.stop();
    BasisMediaPipeWeb.onStateChanged = {{{ makeDynCall('vi', 'onStateChanged') }}};
    BasisMediaPipeWeb.onResult = {{{ makeDynCall('vii', 'onResult') }}};
    BasisMediaPipeWeb.frameInterval = 1000 / Math.max(1, targetFps);
    BasisMediaPipeWeb.lastFrameTime = 0;
    BasisMediaPipeWeb.initialize({
      enableFace: enableFace !== 0,
      enableHands: enableHands !== 0,
      enablePose: enablePose !== 0,
      mirror: mirror !== 0,
      swapHands: swapHands !== 0,
      width: width,
      height: height,
      targetFps: targetFps,
    });
  },

  BasisMediaPipeWebPump: function(timestampMs) {
    if (!BasisMediaPipeWeb.ready || BasisMediaPipeWeb.busy || !BasisMediaPipeWeb.video
        || BasisMediaPipeWeb.video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA
        || timestampMs - BasisMediaPipeWeb.lastFrameTime < BasisMediaPipeWeb.frameInterval) {
      return;
    }

    BasisMediaPipeWeb.busy = true;
    BasisMediaPipeWeb.lastFrameTime = timestampMs;
    createImageBitmap(BasisMediaPipeWeb.video).then(function(bitmap) {
      if (!BasisMediaPipeWeb.worker) {
        bitmap.close();
        BasisMediaPipeWeb.busy = false;
        return;
      }
      BasisMediaPipeWeb.worker.postMessage({ type: "frame", bitmap: bitmap, timestampMs: timestampMs }, [bitmap]);
    }).catch(function(error) {
      BasisMediaPipeWeb.busy = false;
      console.error("BasisMediaPipe(web) frame capture: " + error);
    });
  },

  BasisMediaPipeWebShutdown: function() {
    BasisMediaPipeWeb.stop();
  },
});
