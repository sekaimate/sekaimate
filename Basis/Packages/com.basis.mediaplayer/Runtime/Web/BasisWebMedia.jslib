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
    var sourceGain = audioContext.createGain();
    var directGain = audioContext.createGain();
    var spatialGain = audioContext.createGain();
    var panner = audioContext.createPanner();
    source.connect(gain);
    gain.connect(sourceGain);
    sourceGain.connect(directGain);
    sourceGain.connect(spatialGain);
    directGain.connect(audioContext.destination);
    spatialGain.connect(panner);
    panner.connect(audioContext.destination);

    var id = BasisWebMedia.nextId++;
    var player = {
      video: video,
      audioContext: audioContext,
      source: source,
      gain: gain,
      sourceGain: sourceGain,
      directGain: directGain,
      spatialGain: spatialGain,
      panner: panner,
      error: 0,
      framePending: false,
      textureInitialized: false,
      lastTime: -1,
      frameCallbackId: 0,
      destroyed: false,
    };
    video.onerror = function() {
      video.pause();
      gain.gain.value = 0;
      player.error = 2;
    };
    BasisWebMedia.players[id] = player;
    if (video.requestVideoFrameCallback) {
      var markFrame = function() {
        if (player.destroyed) return;
        player.framePending = true;
        player.frameCallbackId = video.requestVideoFrameCallback(markFrame);
      };
      player.frameCallbackId = video.requestVideoFrameCallback(markFrame);
    }
    video.load();
    return id;
  },

  BasisWebMediaDestroy__deps: ['$BasisWebMedia'],
  BasisWebMediaDestroy: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    if (!player) return;
    player.destroyed = true;
    if (player.video.cancelVideoFrameCallback && player.frameCallbackId) {
      player.video.cancelVideoFrameCallback(player.frameCallbackId);
    }
    player.video.pause();
    player.video.removeAttribute('src');
    player.video.load();
    player.source.disconnect();
    player.gain.disconnect();
    player.sourceGain.disconnect();
    player.directGain.disconnect();
    player.spatialGain.disconnect();
    player.panner.disconnect();
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
    if (player.error !== 0) return 6;
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
    if (player.video.requestVideoFrameCallback) {
      if (!player.framePending) return 0;
      player.framePending = false;
    } else {
      if (player.lastTime === player.video.currentTime) return 0;
      player.lastTime = player.video.currentTime;
    }
    var previousFlip = GLctx.getParameter(GLctx.UNPACK_FLIP_Y_WEBGL);
    var previousTexture = GLctx.getParameter(GLctx.TEXTURE_BINDING_2D);
    try {
      var texture = GL.textures[textureId];
      if (!texture) return 0;
      GLctx.bindTexture(GLctx.TEXTURE_2D, texture);
      GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, true);
      GLctx.texSubImage2D(GLctx.TEXTURE_2D, 0, 0, 0, GLctx.RGBA, GLctx.UNSIGNED_BYTE, player.video);
      return 1;
    } catch (error) {
      player.video.pause();
      player.gain.gain.value = 0;
      player.error = 3;
      return 0;
    } finally {
      GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, previousFlip);
      GLctx.bindTexture(GLctx.TEXTURE_2D, previousTexture);
    }
  },

  BasisWebMediaSetPlaybackSettings__deps: ['$BasisWebMedia'],
  BasisWebMediaSetPlaybackSettings: function(mediaId, volume, mute, playbackRate, loop) {
    var player = BasisWebMedia.players[mediaId];
    if (!player || player.error !== 0) return;
    player.video.muted = mute !== 0;
    player.video.defaultMuted = mute !== 0;
    player.gain.gain.value = mute ? 0 : Math.max(0, Math.min(1, volume));
    player.video.playbackRate = Math.max(0.25, Math.min(4, playbackRate));
    player.video.loop = false;
  },

  BasisWebMediaSetSpatialSettings__deps: ['$BasisWebMedia'],
  BasisWebMediaSetSpatialSettings: function(mediaId, spatialBlend, sourceVolume, sourceMuted, minDistance, maxDistance, rolloffMode, sourceX, sourceY, sourceZ, listenerX, listenerY, listenerZ, forwardX, forwardY, forwardZ, upX, upY, upZ) {
    var player = BasisWebMedia.players[mediaId];
    if (!player || player.error !== 0) return;
    var blend = Math.max(0, Math.min(1, spatialBlend));
    player.directGain.gain.value = 1 - blend;
    player.spatialGain.gain.value = blend;
    player.sourceGain.gain.value = sourceMuted ? 0 : Math.max(0, Math.min(1, sourceVolume));
    player.panner.distanceModel = rolloffMode === 1 ? 'linear' : 'inverse';
    player.panner.refDistance = Math.max(0.01, minDistance);
    player.panner.maxDistance = Math.max(player.panner.refDistance, maxDistance);
    player.panner.rolloffFactor = 1;
    player.panner.positionX.value = sourceX;
    player.panner.positionY.value = sourceY;
    player.panner.positionZ.value = -sourceZ;
    var listener = player.audioContext.listener;
    listener.positionX.value = listenerX;
    listener.positionY.value = listenerY;
    listener.positionZ.value = -listenerZ;
    listener.forwardX.value = forwardX;
    listener.forwardY.value = forwardY;
    listener.forwardZ.value = -forwardZ;
    listener.upX.value = upX;
    listener.upY.value = upY;
    listener.upZ.value = -upZ;
  },
});
