mergeInto(LibraryManager.library, {
  $BasisWebClipboard: {
    complete: function(callback, requestId, succeeded, value) {
      var length = lengthBytesUTF8(value) + 1;
      var valuePointer = _malloc(length);
      stringToUTF8(value, valuePointer, length);
      {{{ makeDynCall('viii', 'callback') }}}(requestId, succeeded, valuePointer);
      _free(valuePointer);
    },
    unavailableReason: function(operation) {
      if (!window.isSecureContext) {
        return 'Clipboard ' + operation + ' requires a secure context.';
      }
      if (!navigator.clipboard) {
        return 'Clipboard API is unavailable in this browser.';
      }
      if (navigator.userActivation && !navigator.userActivation.isActive) {
        return 'Clipboard ' + operation + ' requires an active user gesture.';
      }
      return '';
    },
    describeError: function(operation, error) {
      var name = error && error.name ? error.name : 'Error';
      var message = error && error.message ? error.message : String(error);
      return 'Clipboard ' + operation + ' failed: ' + name + ': ' + message;
    },
  },

  BasisWebClipboardWrite__deps: ['$BasisWebClipboard'],
  BasisWebClipboardWrite: function(textPointer, requestId, onCompleted) {
    var reason = BasisWebClipboard.unavailableReason('write');
    if (reason) {
      BasisWebClipboard.complete(onCompleted, requestId, 0, reason);
      return;
    }

    var text = UTF8ToString(textPointer);
    navigator.clipboard.writeText(text).then(function() {
      BasisWebClipboard.complete(onCompleted, requestId, 1, '');
    }).catch(function(error) {
      BasisWebClipboard.complete(
        onCompleted,
        requestId,
        0,
        BasisWebClipboard.describeError('write', error));
    });
  },

  BasisWebClipboardRead__deps: ['$BasisWebClipboard'],
  BasisWebClipboardRead: function(requestId, onCompleted) {
    var reason = BasisWebClipboard.unavailableReason('read');
    if (reason) {
      BasisWebClipboard.complete(onCompleted, requestId, 0, reason);
      return;
    }

    navigator.clipboard.readText().then(function(text) {
      BasisWebClipboard.complete(onCompleted, requestId, 1, text);
    }).catch(function(error) {
      BasisWebClipboard.complete(
        onCompleted,
        requestId,
        0,
        BasisWebClipboard.describeError('read', error));
    });
  },
});
