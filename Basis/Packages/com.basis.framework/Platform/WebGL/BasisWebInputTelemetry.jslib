mergeInto(LibraryManager.library, {
  BasisWebInputTelemetryPublish: function(snapshotJsonPointer) {
    var snapshot = JSON.parse(UTF8ToString(snapshotJsonPointer));
    var deepFreeze = function(value) {
      Object.values(value).forEach(function(child) {
        if (child && typeof child === 'object') {
          deepFreeze(child);
        }
      });
      return Object.freeze(value);
    };
    window.basisInputE2E = deepFreeze(snapshot);
  },
});
