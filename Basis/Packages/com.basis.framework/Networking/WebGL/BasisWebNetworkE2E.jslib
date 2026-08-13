mergeInto(LibraryManager.library, {
  BasisWebNetworkE2EReport: function(jsonPointer) {
    var event = JSON.parse(UTF8ToString(jsonPointer));
    window.basisNetworkE2EEvents = window.basisNetworkE2EEvents || [];
    window.basisNetworkE2ESendChat = function(message) {
      Module.SendMessage('Basis Web Network E2E', 'SendChat', message);
    };
    window.basisNetworkE2EReconnect = function() {
      Module.SendMessage('Basis Web Network E2E', 'Reconnect');
    };
    window.basisNetworkE2EEvents.push(event);
    window.dispatchEvent(new CustomEvent('basis-network-e2e', { detail: event }));
  },
});
