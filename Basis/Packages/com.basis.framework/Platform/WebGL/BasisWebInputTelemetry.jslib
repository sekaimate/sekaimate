mergeInto(LibraryManager.library, {
  BasisWebInputTelemetryPublish: function(snapshotJsonPointer) {
    var snapshot = JSON.parse(UTF8ToString(snapshotJsonPointer));
    window.basisInputE2E = Object.freeze(snapshot);
  },
});
