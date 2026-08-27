mergeInto(LibraryManager.library, {
  BasisWebServersUIE2EReport: function(jsonPointer) {
    var state = JSON.parse(UTF8ToString(jsonPointer));
    window.basisServersUIE2E = window.basisServersUIE2E || {};
    window.basisServersUIE2E.state = state;
    window.basisServersUIE2E.command = function(command) {
      Module.SendMessage('Basis Web Servers UI E2E', 'Command', JSON.stringify(command));
    };
    window.dispatchEvent(new CustomEvent('basis-servers-ui-e2e', { detail: state }));
  },
});
