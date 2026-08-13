mergeInto(LibraryManager.library, {
  $BasisWebStreamingMetaApi: function() {
    if (globalThis.BasisStreamingMeta) {
      return globalThis.BasisStreamingMeta;
    }

    var listeners = [];
    var api = {
      snapshot: null,
      subscribe: function(listener) {
        if (typeof listener !== 'function') {
          throw new TypeError('BasisStreamingMeta subscriber must be a function');
        }
        listeners.push(listener);
        return function() {
          var index = listeners.indexOf(listener);
          if (index >= 0) listeners.splice(index, 1);
        };
      }
    };
    api.publish = function(snapshot) {
      api.snapshot = snapshot;
      listeners.slice().forEach(function(listener) {
        listener(snapshot);
      });
    };
    globalThis.BasisStreamingMeta = api;
    return api;
  },

  BasisWebStreamingMetaPublish__deps: ['$BasisWebStreamingMetaApi'],
  BasisWebStreamingMetaPublish: function(fps, ccu, peerLimit, roundTripMs, pingMs, connected) {
    BasisWebStreamingMetaApi().publish({
      fps: fps,
      ccu: ccu,
      peerLimit: peerLimit,
      rtt: roundTripMs,
      ping: pingMs,
      connected: connected !== 0,
      timeUtc: new Date().toISOString()
    });
  },

  BasisWebStreamingMetaClear__deps: ['$BasisWebStreamingMetaApi'],
  BasisWebStreamingMetaClear: function() {
    BasisWebStreamingMetaApi().publish(null);
  }
});
