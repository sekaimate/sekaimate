mergeInto(LibraryManager.library, {
  $BasisWebSockets: {},

  BasisWebSocketOpen__deps: ['$BasisWebSockets'],
  BasisWebSocketOpen: function(connectionId, uriPointer, maximumBufferedAmount, onOpen, onMessage, onError, onClose) {
    var uri = UTF8ToString(uriPointer);
    try {
      var socket = new WebSocket(uri);
      socket.binaryType = 'arraybuffer';
      BasisWebSockets[connectionId] = {
        socket: socket,
        maximumBufferedAmount: maximumBufferedAmount
      };
      socket.onopen = function() {
        {{{ makeDynCall('vi', 'onOpen') }}}(connectionId);
      };
      socket.onmessage = function(event) {
        if (!(event.data instanceof ArrayBuffer)) {
          {{{ makeDynCall('vi', 'onError') }}}(connectionId);
          return;
        }
        var payload = new Uint8Array(event.data);
        var payloadPointer = _malloc(payload.length);
        HEAPU8.set(payload, payloadPointer);
        {{{ makeDynCall('viii', 'onMessage') }}}(connectionId, payloadPointer, payload.length);
        _free(payloadPointer);
      };
      socket.onerror = function() {
        {{{ makeDynCall('vi', 'onError') }}}(connectionId);
      };
      socket.onclose = function(event) {
        delete BasisWebSockets[connectionId];
        var reasonLength = lengthBytesUTF8(event.reason);
        var reasonPointer = _malloc(reasonLength + 1);
        stringToUTF8(event.reason, reasonPointer, reasonLength + 1);
        {{{ makeDynCall('viiii', 'onClose') }}}(connectionId, event.code, reasonPointer, reasonLength);
        _free(reasonPointer);
      };
    } catch (error) {
      {{{ makeDynCall('vi', 'onError') }}}(connectionId);
      {{{ makeDynCall('viiii', 'onClose') }}}(connectionId, 1006, 0, 0);
    }
  },

  BasisWebSocketSend__deps: ['$BasisWebSockets'],
  BasisWebSocketSend: function(connectionId, payloadPointer, payloadLength) {
    var connection = BasisWebSockets[connectionId];
    if (!connection || connection.socket.readyState !== WebSocket.OPEN) {
      return 0;
    }
    if (connection.socket.bufferedAmount + payloadLength > connection.maximumBufferedAmount) {
      return 2;
    }
    try {
      connection.socket.send(HEAPU8.slice(payloadPointer, payloadPointer + payloadLength));
      return 1;
    } catch (error) {
      return 0;
    }
  },

  BasisWebSocketClose__deps: ['$BasisWebSockets'],
  BasisWebSocketClose: function(connectionId, code, reasonPointer) {
    var connection = BasisWebSockets[connectionId];
    if (!connection) {
      return;
    }
    var browserCode = code === 1000 || code >= 3000 && code <= 4999
      ? code
      : 4000 + Math.max(0, Math.min(999, code - 1000));
    var reason = UTF8ToString(reasonPointer);
    while (lengthBytesUTF8(reason) > 123) {
      reason = reason.slice(0, -1);
    }
    try {
      connection.socket.close(browserCode, reason);
    } catch (error) {
      connection.socket.close();
    }
  },
});
