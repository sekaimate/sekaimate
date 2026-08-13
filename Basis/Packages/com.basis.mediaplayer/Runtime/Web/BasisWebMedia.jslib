mergeInto(LibraryManager.library, {
  $BasisWebMedia: {
    nextId: 1,
    players: {},
    diagnostics: null,
    ensureDiagnostics: function() {
      if (BasisWebMedia.diagnostics) return BasisWebMedia.diagnostics;
      var enabled = false;
      try {
        enabled = new URLSearchParams(window.location.search).get('basisMediaE2E') === '1';
      } catch (error) {
        return null;
      }
      if (!enabled) return null;
      BasisWebMedia.diagnostics = {
        phase: 'initializing',
        mediaId: 0,
        htmlVideoElement: false,
        sourceUrl: '',
        corsMode: '',
        crossOriginRequest: false,
        codecSupport: '',
        videoWidth: 0,
        videoHeight: 0,
        currentTime: 0,
        paused: true,
        playbackStarted: false,
        playRequestCount: 0,
        pauseRequestCount: 0,
        seekRequestCount: 0,
        lastSeekSeconds: -1,
        textureUploadCount: 0,
        audioContextCreated: false,
        mediaElementSourceCreated: false,
        gainConnected: false,
        destinationConnected: false,
        audioContextState: '',
        errorCode: 0,
      };
      window.__basisWebMediaE2E = BasisWebMedia.diagnostics;
      return BasisWebMedia.diagnostics;
    },
    updateDiagnostics: function(player, phase) {
      var diagnostics = BasisWebMedia.ensureDiagnostics();
      if (!diagnostics || !player) return;
      diagnostics.phase = phase;
      diagnostics.mediaId = player.id;
      diagnostics.htmlVideoElement = player.video instanceof HTMLVideoElement;
      diagnostics.sourceUrl = player.video.currentSrc || player.video.src;
      diagnostics.corsMode = player.video.crossOrigin || '';
      try {
        diagnostics.crossOriginRequest = new URL(diagnostics.sourceUrl).origin !== window.location.origin;
      } catch (error) {
        diagnostics.crossOriginRequest = false;
      }
      diagnostics.codecSupport = player.video.canPlayType('video/webm; codecs="vp8,opus"');
      diagnostics.videoWidth = player.video.videoWidth;
      diagnostics.videoHeight = player.video.videoHeight;
      diagnostics.currentTime = player.video.currentTime;
      diagnostics.paused = player.video.paused;
      diagnostics.audioContextState = player.audioContext.state;
      diagnostics.errorCode = player.error;
    },
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
      id: id,
      video: video,
      audioContext: audioContext,
      source: source,
      gain: gain,
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
      BasisWebMedia.updateDiagnostics(player, 'error');
    };
    video.addEventListener('loadedmetadata', function() {
      BasisWebMedia.updateDiagnostics(player, 'metadata');
    });
    video.addEventListener('playing', function() {
      var diagnostics = BasisWebMedia.ensureDiagnostics();
      if (diagnostics) diagnostics.playbackStarted = true;
      BasisWebMedia.updateDiagnostics(player, 'playing');
    });
    audioContext.addEventListener('statechange', function() {
      BasisWebMedia.updateDiagnostics(player, 'audio-state');
    });
    BasisWebMedia.players[id] = player;
    var diagnostics = BasisWebMedia.ensureDiagnostics();
    if (diagnostics) {
      diagnostics.audioContextCreated = window.AudioContext && audioContext instanceof window.AudioContext ||
        (window.webkitAudioContext && audioContext instanceof window.webkitAudioContext);
      diagnostics.mediaElementSourceCreated = source instanceof MediaElementAudioSourceNode;
      diagnostics.gainConnected = true;
      diagnostics.destinationConnected = true;
    }
    BasisWebMedia.updateDiagnostics(player, 'created');
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
    player.audioContext.close();
    delete BasisWebMedia.players[mediaId];
    BasisWebMedia.updateDiagnostics(player, 'destroyed');
  },

  BasisWebMediaPlay__deps: ['$BasisWebMedia'],
  BasisWebMediaPlay: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    if (!player) return;
    player.error = 0;
    var diagnostics = BasisWebMedia.ensureDiagnostics();
    if (diagnostics) diagnostics.playRequestCount++;
    if (!player.video.muted) {
      var resumeRequest = player.audioContext.resume();
      if (resumeRequest) {
        resumeRequest.catch(function() { player.error = 1; });
      }
    }
    var playRequest = player.video.play();
    if (playRequest) {
      playRequest.catch(function(error) {
        player.error = error && error.name === 'NotAllowedError' ? 1 : 2;
        BasisWebMedia.updateDiagnostics(player, 'play-error');
      });
    }
    BasisWebMedia.updateDiagnostics(player, 'play-requested');
  },

  BasisWebMediaPause__deps: ['$BasisWebMedia'],
  BasisWebMediaPause: function(mediaId) {
    var player = BasisWebMedia.players[mediaId];
    if (!player) return;
    player.video.pause();
    var diagnostics = BasisWebMedia.ensureDiagnostics();
    if (diagnostics) diagnostics.pauseRequestCount++;
    BasisWebMedia.updateDiagnostics(player, 'paused');
  },

  BasisWebMediaSeek__deps: ['$BasisWebMedia'],
  BasisWebMediaSeek: function(mediaId, seconds) {
    var player = BasisWebMedia.players[mediaId];
    if (!player || !isFinite(seconds)) return 0;
    try {
      player.video.currentTime = Math.max(0, seconds);
      var diagnostics = BasisWebMedia.ensureDiagnostics();
      if (diagnostics) {
        diagnostics.seekRequestCount++;
        diagnostics.lastSeekSeconds = Math.max(0, seconds);
      }
      BasisWebMedia.updateDiagnostics(player, 'seeked');
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
      var diagnostics = BasisWebMedia.ensureDiagnostics();
      if (diagnostics) diagnostics.textureUploadCount++;
      BasisWebMedia.updateDiagnostics(player, 'texture-uploaded');
      return 1;
    } catch (error) {
      player.video.pause();
      player.gain.gain.value = 0;
      player.error = 3;
      BasisWebMedia.updateDiagnostics(player, 'texture-error');
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
});
