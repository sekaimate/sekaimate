mergeInto(LibraryManager.library, {
  $BasisWebPointerLock: {
    canvas: null,
    notify: null,
    initialized: false,
    requestPending: false,
    emitState: function() {
      if (document.pointerLockElement === BasisWebPointerLock.canvas) {
        BasisWebPointerLock.requestPending = false;
      }
      if (BasisWebPointerLock.notify) {
        BasisWebPointerLock.notify(document.pointerLockElement === BasisWebPointerLock.canvas ? 1 : 0);
      }
    },
    requestIfPending: function() {
      if (!BasisWebPointerLock.requestPending || !BasisWebPointerLock.canvas ||
          document.pointerLockElement === BasisWebPointerLock.canvas) {
        return;
      }
      try {
        var request = BasisWebPointerLock.canvas.requestPointerLock();
        if (request && request.catch) {
          request.catch(BasisWebPointerLock.emitState);
        }
      } catch (error) {
        BasisWebPointerLock.emitState();
      }
    },
    release: function() {
      BasisWebPointerLock.requestPending = false;
      if (document.pointerLockElement === BasisWebPointerLock.canvas) {
        document.exitPointerLock();
      }
      if (BasisWebPointerLock.notify) {
        BasisWebPointerLock.notify(0);
      }
    },
  },

  BasisWebPointerLockInitialize__deps: ['$BasisWebPointerLock'],
  BasisWebPointerLockInitialize: function(onStateChanged) {
    BasisWebPointerLock.canvas = Module['canvas'];
    BasisWebPointerLock.notify = function(isLocked) {
      {{{ makeDynCall('vi', 'onStateChanged') }}}(isLocked);
    };
    if (!BasisWebPointerLock.initialized) {
      document.addEventListener('pointerlockchange', BasisWebPointerLock.emitState);
      document.addEventListener('pointerlockerror', BasisWebPointerLock.emitState);
      BasisWebPointerLock.canvas.addEventListener('pointerdown', BasisWebPointerLock.requestIfPending);
      document.addEventListener('keydown', BasisWebPointerLock.requestIfPending);
      document.addEventListener('visibilitychange', function() {
        if (document.hidden) {
          BasisWebPointerLock.release();
        }
      });
      window.addEventListener('blur', BasisWebPointerLock.release);
      BasisWebPointerLock.initialized = true;
    }
    BasisWebPointerLock.emitState();
  },

  BasisWebPointerLockRequestFromUserGesture__deps: ['$BasisWebPointerLock'],
  BasisWebPointerLockRequestFromUserGesture: function() {
    if (!BasisWebPointerLock.initialized || !BasisWebPointerLock.canvas) {
      return 0;
    }
    if (document.pointerLockElement === BasisWebPointerLock.canvas) {
      BasisWebPointerLock.emitState();
      return 1;
    }
    BasisWebPointerLock.requestPending = true;
    if (navigator.userActivation && navigator.userActivation.isActive) {
      BasisWebPointerLock.requestIfPending();
    }
    return 1;
  },

  BasisWebPointerLockExit__deps: ['$BasisWebPointerLock'],
  BasisWebPointerLockExit: function() {
    BasisWebPointerLock.release();
  },
});
