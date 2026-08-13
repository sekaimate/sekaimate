mergeInto(LibraryManager.library, {
  BasisWebInputTelemetryPublish: function(snapshotJsonPointer) {
    var snapshot = JSON.parse(UTF8ToString(snapshotJsonPointer));
    window.BasisWebInputTelemetry = Object.freeze(snapshot);
  },
});
