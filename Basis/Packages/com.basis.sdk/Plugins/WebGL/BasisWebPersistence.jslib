mergeInto(LibraryManager.library, {
  $BasisPersistenceSync: {
    queue: [],
    running: false,

    enqueue: function(populate, requestId, callback) {
      BasisPersistenceSync.queue.push({
        populate: populate,
        requestId: requestId,
        callback: callback
      });
      BasisPersistenceSync.drain();
    },

    drain: function() {
      if (BasisPersistenceSync.running || BasisPersistenceSync.queue.length === 0) {
        return;
      }

      BasisPersistenceSync.running = true;
      var operation = BasisPersistenceSync.queue.shift();
      FS.syncfs(operation.populate, function(error) {
        var callback = operation.callback;
        {{{ makeDynCall('vii', 'callback') }}}(operation.requestId, error ? 0 : 1);
        BasisPersistenceSync.running = false;
        BasisPersistenceSync.drain();
      });
    }
  },

  BasisWebPersistenceSync__deps: ['$BasisPersistenceSync'],
  BasisWebPersistenceSync: function(requestId, populate, callback) {
    BasisPersistenceSync.enqueue(populate !== 0, requestId, callback);
  }
});
