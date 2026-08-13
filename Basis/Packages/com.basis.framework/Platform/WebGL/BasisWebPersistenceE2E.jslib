mergeInto(LibraryManager.library, {
  BasisWebPersistenceE2EPublish: function(resultJsonPointer) {
    window.basisPersistenceE2E = JSON.parse(UTF8ToString(resultJsonPointer));
  }
});
