var AnalyzerLink = {
  SetupAnalyzerSpace: function () {
    if (typeof window["_WebALPeerAnalyzers"] === "undefined") {
      window["_WebALPeerAnalyzers"] = {};
    }
  },
  LinkAnalyzer: function (ID, duration, bufferSize) {
    setTimeout(function () {
      var tolerableLength = 0.075;
      var name = btoa(ID);

      if (
        window["_WebALPeerAnalyzers"][name] === null ||
        typeof window["_WebALPeerAnalyzers"][name] === "undefined"
      ) {
        var splitter = null;
        var analyzerLeft = null;
        var analyzerRight = null;
        var source = null;

        try {
          if (typeof WEBAudio === "undefined") return;

          var audioInstanceKeys = Object.keys(WEBAudio.audioInstances);
          if (audioInstanceKeys.length > 1) {
            for (var index = audioInstanceKeys.length - 1; index >= 0; index--) {
              var audioInstance = WEBAudio.audioInstances[audioInstanceKeys[index]];

              if (audioInstance != null) {
                var rootSource = audioInstance.source;

                if (
                  rootSource != null &&
                  rootSource.buffer != null &&
                  Math.abs(rootSource.buffer.duration - duration) < tolerableLength
                ) {
                  source = rootSource;
                  break;
                }
              }
            }

            if (source !== null && typeof source.context !== "undefined") {
              var audioContext = source.context;

              splitter = audioContext.createChannelSplitter(2);
              analyzerLeft = audioContext.createAnalyser();
              analyzerRight = audioContext.createAnalyser();

              analyzerLeft.fftSize = analyzerRight.fftSize = bufferSize * 2;
              analyzerLeft.smoothingTimeConstant = analyzerRight.smoothingTimeConstant = 0;

              source.connect(splitter);
              splitter.connect(analyzerLeft, 0, 0);
              splitter.connect(analyzerRight, 1, 0);

              window["_WebALPeerAnalyzers"][name] = {
                source: source,
                splitter: splitter,
                analyzerLeft: analyzerLeft,
                analyzerRight: analyzerRight,
              };
            }
          }
        } catch (error) {
          if (source !== null && splitter !== null) source.disconnect(splitter);
          if (splitter !== null && analyzerLeft !== null) splitter.disconnect(analyzerLeft);
          if (splitter !== null && analyzerRight !== null) splitter.disconnect(analyzerRight);
          throw error;
        }
      }
    }, 250);
  },
  UnlinkAnalyzer: function (ID) {
    var name = btoa(ID);
    var analyzers = window["_WebALPeerAnalyzers"][name];

    if (analyzers !== null && typeof analyzers !== "undefined") {
      try {
        analyzers.splitter.disconnect(analyzers.analyzerLeft);
        analyzers.splitter.disconnect(analyzers.analyzerRight);
        analyzers.source.disconnect(analyzers.splitter);
      } finally {
        delete window["_WebALPeerAnalyzers"][name];
      }
    }
  },
  FetchAnalyzerLeft: function (ID, bufferPointer, bufferSize) {
    var name = btoa(ID);
    var analyzers = window["_WebALPeerAnalyzers"][name];

    if (analyzers !== null && typeof analyzers !== "undefined") {
      var buffer = new Float32Array(HEAPU8.buffer, bufferPointer, bufferSize);
      analyzers.analyzerLeft.getFloatTimeDomainData(buffer);
    }
  },
  FetchAnalyzerRight: function (ID, bufferPointer, bufferSize) {
    var name = btoa(ID);
    var analyzers = window["_WebALPeerAnalyzers"][name];

    if (analyzers !== null && typeof analyzers !== "undefined") {
      var buffer = new Float32Array(HEAPU8.buffer, bufferPointer, bufferSize);
      analyzers.analyzerRight.getFloatTimeDomainData(buffer);
    }
  },
};

mergeInto(LibraryManager.library, AnalyzerLink);
