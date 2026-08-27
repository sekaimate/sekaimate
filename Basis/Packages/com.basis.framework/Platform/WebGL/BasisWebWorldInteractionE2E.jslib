mergeInto(LibraryManager.library, {
  BasisWebWorldInteractionE2EPublish: function(snapshotJsonPointer) {
    window.basisWorldInteractionE2E = Object.freeze(JSON.parse(UTF8ToString(snapshotJsonPointer)));
  },
});
