mergeInto(LibraryManager.library, {
  BasisWebDownloadFile: function(data, length, filename, contentType) {
    var bytes = HEAPU8.slice(data, data + length);
    var blob = new Blob([bytes], { type: UTF8ToString(contentType) });
    var objectUrl = URL.createObjectURL(blob);
    var anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = UTF8ToString(filename);
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    setTimeout(function() {
      URL.revokeObjectURL(objectUrl);
    }, 0);
  }
});
