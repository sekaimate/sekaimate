mergeInto(LibraryManager.library, {
  BasisWebLibraryE2EReport: function(jsonPointer) {
    var snapshot = JSON.parse(UTF8ToString(jsonPointer));
    window.basisLibraryE2E = window.basisLibraryE2E || {};
    window.basisLibraryE2E.command = function(command) {
      Module.SendMessage('Basis Web Library E2E', 'Command', JSON.stringify(command));
    };
    window.basisLibraryE2E.snapshot = snapshot;
    window.dispatchEvent(new CustomEvent('basis-library-e2e', { detail: snapshot }));
  },
});
