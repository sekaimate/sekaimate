mergeInto(LibraryManager.library, {
  $BasisWebSettingsE2E: {
    api: null,
    pending: new Map(),
    nextRequestId: 1,
  },

  BasisWebSettingsE2EInitialize__deps: ["$BasisWebSettingsE2E"],
  BasisWebSettingsE2EInitialize: function(gameObjectNamePointer) {
    var gameObjectName = UTF8ToString(gameObjectNamePointer);
    var api = {
      ready: false,
      request: function(operation, restoreValues) {
        var requestId = BasisWebSettingsE2E.nextRequestId++;
        var request = JSON.stringify({
          requestId: requestId,
          operation: operation,
          restoreValues: restoreValues || [],
        });
        return new Promise(function(resolve, reject) {
          BasisWebSettingsE2E.pending.set(requestId, { resolve: resolve, reject: reject });
          try {
            SendMessage(gameObjectName, "HandleRequest", request);
          } catch (error) {
            BasisWebSettingsE2E.pending.delete(requestId);
            reject(error);
          }
        });
      },
    };
    BasisWebSettingsE2E.api = api;
    window.basisSettingsE2E = api;
  },

  BasisWebSettingsE2EPublish__deps: ["$BasisWebSettingsE2E"],
  BasisWebSettingsE2EPublish: function(resultJsonPointer) {
    var result = JSON.parse(UTF8ToString(resultJsonPointer));
    if (result.operation === "ready") {
      BasisWebSettingsE2E.api.ready = result.succeeded;
      BasisWebSettingsE2E.api.error = result.error;
      return;
    }

    var pending = BasisWebSettingsE2E.pending.get(result.requestId);
    if (!pending) {
      return;
    }
    BasisWebSettingsE2E.pending.delete(result.requestId);
    pending.resolve(result);
  },
});
