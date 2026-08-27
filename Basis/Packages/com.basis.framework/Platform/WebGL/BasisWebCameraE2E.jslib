mergeInto(LibraryManager.library, {
  BasisWebCameraE2EPublish: function(resultJsonPointer) {
    window.basisCameraE2E = JSON.parse(UTF8ToString(resultJsonPointer));
  }
});
