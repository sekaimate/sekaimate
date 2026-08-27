mergeInto(LibraryManager.library, {
  BasisWebBeeRuntimeCapabilityPublish: function(snapshotJsonPointer) {
    var snapshot = JSON.parse(UTF8ToString(snapshotJsonPointer));
    var diagnostics = globalThis.BasisBeeRuntimeCapabilityDiagnostics;
    if (!diagnostics) {
      diagnostics = {
        snapshots: {},
        latest: {}
      };
      globalThis.BasisBeeRuntimeCapabilityDiagnostics = diagnostics;
    }

    var formatSnapshots = diagnostics.snapshots[snapshot.format];
    if (!formatSnapshots) {
      formatSnapshots = [];
      diagnostics.snapshots[snapshot.format] = formatSnapshots;
    }
    formatSnapshots.push(snapshot);
    if (formatSnapshots.length > 256) {
      formatSnapshots.shift();
    }
    diagnostics.latest[snapshot.format] = snapshot;
  }
});
