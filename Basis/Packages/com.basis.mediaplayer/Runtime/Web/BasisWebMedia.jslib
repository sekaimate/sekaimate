mergeInto(LibraryManager.library, {
  $BasisWebMedia: {
    nextId: 1,
    players: {},
  },

  BasisWebMediaCreate__deps: ['$BasisWebMedia'],
  BasisWebMediaCreate: function(urlPointer) {
    var url = UTF8ToString(urlPointer);
    if (window.location.protocol === 'https:' && url.indexOf('http:') === 0) {
      return -1;
    }

    var video = document.createElement('video');
    video.crossOrigin = 'anonymous';
    video.preload = 'auto';
    video.playsInline = true;
    video.src = url;

    var audioContext = new (window.AudioContext || window.webkitAudioContext)();
    var source = audioContext.createMediaElementSource(video);
    var gain = audioContext.createGain();
    source.connect(gain);
    gain.connect(audioContext.destination);

    var id = BasisWebMedia.nextId++;
    var player = {
      video: video,
      audioContext: audioContext,
      source: source,
      gain: gain,
      error: 0,
    };
    video.onerror = function() { player.error = 2; };
    BasisWebMedia.players[id] = player;
    video.load();
    return id;
  },

  BasisWebMediaDestroy__deps: ['$BasisWebMedia'],
  BasisWebMediaDestroy: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    if (!player) return;
    player.video.pause();
    player.video.removeAttribute('src');
    player.video.load();
    player.source.disconnect();
    player.gain.disconnect();
    player.audioContext.close();
    delete BasisWebMedia.players[mediaId];
  },

  BasisWebMediaPlay__deps: ['$BasisWebMedia'],
  BasisWebMediaPlay: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    if (!player) return;
    player.error = 0;
    var resumeRequest = player.audioContext.resume();
    if (resumeRequest) {
      resumeRequest.catch(function() { player.error = 1; });
    }
    var playRequest = player.video.play();
    if (playRequest) {
      playRequest.catch(function(error) {
        player.error = error && error.name === 'NotAllowedError' ? 1 : 2;
      });
    }
  },

  BasisWebMediaPause__deps: ['$BasisWebMedia'],
  BasisWebMediaPause: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    if (player) player.video.pause();
  },

  BasisWebMediaSeek__deps: ['$BasisWebMedia'],
  BasisWebMediaSeek: function(mediaId, seconds) {
    var player = BasisWebMedia.players[mediaId];
    if (!player || !isFinite(seconds)) return 0;
    try {
      player.video.currentTime = Math.max(0, seconds);
      return 1;
    } catch (error) {
      player.error = 5;
      return 0;
    }
  },

  BasisWebMediaGetPosition__deps: ['$BasisWebMedia'],
  BasisWebMediaGetPosition: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    return player && isFinite(player.video.currentTime) ? player.video.currentTime : 0;
  },

  BasisWebMediaGetDuration__deps: ['$BasisWebMedia'],
  BasisWebMediaGetDuration: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    return player ? player.video.duration : 0;
  },

  BasisWebMediaGetWidth__deps: ['$BasisWebMedia'],
  BasisWebMediaGetWidth: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    return player ? player.video.videoWidth : 0;
  },

  BasisWebMediaGetHeight__deps: ['$BasisWebMedia'],
  BasisWebMediaGetHeight: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    return player ? player.video.videoHeight : 0;
  },

  BasisWebMediaGetState__deps: ['$BasisWebMedia'],
  BasisWebMediaGetState: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    if (!player) return 0;
    if (player.error === 2 || player.error === 3 || player.error === 4) return 6;
    if (player.video.ended) return 5;
    if (!player.video.paused) return player.video.readyState >= 3 ? 3 : 2;
    if (player.video.readyState >= 2) return 4;
    return player.video.networkState === 2 ? 1 : 0;
  },

  BasisWebMediaGetError__deps: ['$BasisWebMedia'],
  BasisWebMediaGetError: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    return player ? player.error : 0;
  },

  BasisWebMediaUpdateTexture__deps: ['$BasisWebMedia'],
  BasisWebMediaUpdateTexture: function(mediaId, textureId) {
    var player = BasisWebMedia.players[mediaId];
    if (!player || player.video.readyState < 2) return 0;
    try {
      var texture = GL.textures[textureId];
      if (!texture) return 0;
      GLctx.bindTexture(GLctx.TEXTURE_2D, texture);
      GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, true);
      GLctx.texImage2D(GLctx.TEXTURE_2D, 0, GLctx.RGBA, GLctx.RGBA, GLctx.UNSIGNED_BYTE, player.video);
      GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, false);
      return 1;
    } catch (error) {
      player.error = 3;
      return 0;
    }
  },

  BasisWebMediaSetPlaybackSettings__deps: ['$BasisWebMedia'],
  BasisWebMediaSetPlaybackSettings: function(mediaId, volume, mute, playbackRate, loop) {
    var player = BasisWebMedia.players[mediaId];
    if (!player) return;
    player.gain.gain.value = mute ? 0 : Math.max(0, Math.min(1, volume));
    player.video.playbackRate = Math.max(0.25, Math.min(4, playbackRate));
    player.video.loop = loop !== 0;
  },
});
