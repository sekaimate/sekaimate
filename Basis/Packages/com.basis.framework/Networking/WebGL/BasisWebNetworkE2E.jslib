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
    window.basisNetworkE2ESetMuted = function(muted) {
      Module.SendMessage('Basis Web Network E2E', 'SetMuted', muted ? 'true' : 'false');
    };
    window.basisNetworkE2ESetTalkMode = function(talkMode) {
      Module.SendMessage('Basis Web Network E2E', 'SetTalkMode', talkMode);
    };
    window.basisNetworkE2ESetAvatar = function(input) {
      Module.SendMessage('Basis Web Network E2E', 'SetAvatar', JSON.stringify(input));
    };
    window.basisNetworkE2EShareContent = function(input) {
      Module.SendMessage('Basis Web Network E2E', 'ShareContent', JSON.stringify(input));
    };
    window.basisNetworkE2ERemoveContent = function(sphereId) {
      Module.SendMessage('Basis Web Network E2E', 'RemoveContent', sphereId);
    };
    window.basisNetworkE2ELoadContent = function(sphereId) {
      Module.SendMessage('Basis Web Network E2E', 'LoadContent', sphereId);
    };
    window.basisNetworkE2EOpenPlayerList = function() {
      Module.SendMessage('Basis Web Network E2E', 'OpenPlayerList');
    };
    window.basisNetworkE2EPlayerSearch = function(query) {
      Module.SendMessage('Basis Web Network E2E', 'SetPlayerSearch', query);
    };
    window.basisNetworkE2EPlayerSort = function(sort) {
      Module.SendMessage('Basis Web Network E2E', 'SetPlayerSort', sort);
    };
    window.basisNetworkE2EOpenPlayer = function(displayName) {
      Module.SendMessage('Basis Web Network E2E', 'OpenPlayer', displayName);
    };
    window.basisNetworkE2EPlayerUiAction = function(localizationKey) {
      Module.SendMessage('Basis Web Network E2E', 'PlayerUiAction', localizationKey);
    };
    window.basisNetworkE2EPlayerVolume = function(volume) {
      Module.SendMessage('Basis Web Network E2E', 'SetPlayerVolume', String(volume));
    };
    window.basisNetworkE2EConfirmDialogue = function(accepted) {
      Module.SendMessage('Basis Web Network E2E', 'ConfirmDialogue', accepted ? '1' : '0');
    };
    window.basisNetworkE2EPlayerState = function() {
      Module.SendMessage('Basis Web Network E2E', 'ReportPlayerState');
    };
    window.basisNetworkE2EEvents.push(event);
    window.dispatchEvent(new CustomEvent('basis-network-e2e', { detail: event }));
  },
});
