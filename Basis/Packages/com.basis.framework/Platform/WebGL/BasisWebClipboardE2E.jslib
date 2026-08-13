mergeInto(LibraryManager.library, {
  $BasisWebClipboardE2E: {
    api: null,
    createButton: function(id, label, left, onClick) {
      var button = document.createElement('button');
      button.id = id;
      button.type = 'button';
      button.textContent = label;
      button.style.position = 'fixed';
      button.style.left = left + 'px';
      button.style.bottom = '8px';
      button.style.zIndex = '2147483647';
      button.style.width = '104px';
      button.style.height = '32px';
      button.addEventListener('click', onClick);
      document.body.appendChild(button);
    },
  },

  BasisWebClipboardE2EInitialize__deps: ['$BasisWebClipboardE2E'],
  BasisWebClipboardE2EInitialize: function(onWriteRequested, onReadRequested) {
    if (BasisWebClipboardE2E.api) {
      return;
    }

    var api = {
      ready: false,
      secureContext: window.isSecureContext,
      clipboardAvailable: Boolean(navigator.clipboard),
      results: [],
      writeText: '',
      setWriteText: function(text) {
        api.writeText = String(text);
      },
    };
    BasisWebClipboardE2E.api = api;
    window.basisClipboardE2E = api;

    BasisWebClipboardE2E.createButton(
      'basis-clipboard-e2e-write',
      'Clipboard write',
      8,
      function() {
        var textLength = lengthBytesUTF8(api.writeText);
        var textPointer = _malloc(textLength + 1);
        stringToUTF8(api.writeText, textPointer, textLength + 1);
        {{{ makeDynCall('vii', 'onWriteRequested') }}}(textPointer, textLength);
        _free(textPointer);
      });
    BasisWebClipboardE2E.createButton(
      'basis-clipboard-e2e-read',
      'Clipboard read',
      120,
      function() {
        {{{ makeDynCall('v', 'onReadRequested') }}}();
      });

    api.ready = true;
    window.dispatchEvent(new CustomEvent('basis-clipboard-e2e-ready'));
  },

  BasisWebClipboardE2EPublish__deps: ['$BasisWebClipboardE2E'],
  BasisWebClipboardE2EPublish: function(resultJsonPointer) {
    if (!BasisWebClipboardE2E.api) {
      return;
    }
    var result = JSON.parse(UTF8ToString(resultJsonPointer));
    BasisWebClipboardE2E.api.results.push(result);
    window.dispatchEvent(new CustomEvent('basis-clipboard-e2e-result', { detail: result }));
  },
});
