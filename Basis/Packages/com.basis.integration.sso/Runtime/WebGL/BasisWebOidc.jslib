mergeInto(LibraryManager.library, {
  $BasisWebOidc: {
    callbackKey: 'basis.sso.callback',
    pendingKey: 'basis.sso.pending',
    returnUrlKey: 'basis.sso.returnUrl',
    enrollmentConfigKey: 'basis.sso.enrollmentConfig',

    storeEnrollmentConfig: function(json) {
      sessionStorage.setItem(BasisWebOidc.enrollmentConfigKey, json);
    },

    readEnrollmentConfig: function() {
      return sessionStorage.getItem(BasisWebOidc.enrollmentConfigKey) || '';
    },

    publish: function(gameObjectName, result) {
      SendMessage(gameObjectName, 'HandleResult', JSON.stringify(result));
    },

    randomToken: function(byteLength) {
      var bytes = new Uint8Array(byteLength);
      window.crypto.getRandomValues(bytes);
      var binary = '';
      for (var index = 0; index < bytes.length; index += 1) binary += String.fromCharCode(bytes[index]);
      return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
    },

    base64Url: function(bytes) {
      var binary = '';
      for (var index = 0; index < bytes.length; index += 1) binary += String.fromCharCode(bytes[index]);
      return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
    },

    codeChallenge: async function(verifier) {
      var encoded = new TextEncoder().encode(verifier);
      var digest = await window.crypto.subtle.digest('SHA-256', encoded);
      return BasisWebOidc.base64Url(new Uint8Array(digest));
    },

    normalizePath: function(path) {
      if (!path || path === '/') return '/sso-callback';
      return path.charAt(0) === '/' ? path : '/' + path;
    },

    redirectUri: function(config) {
      return window.location.origin + BasisWebOidc.normalizePath(config.redirect && config.redirect.path);
    },

    fetchJson: async function(url, init) {
      var response = await fetch(url, init);
      var body = await response.text();
      var json;
      try { json = body ? JSON.parse(body) : {}; } catch (_) { throw new Error('OIDC endpoint returned non-JSON (' + response.status + ').'); }
      if (!response.ok) {
        throw new Error(json.error_description || json.error || ('OIDC endpoint returned ' + response.status + '.'));
      }
      return json;
    },

    discovery: async function(config) {
      var issuer = config.issuer.replace(/\/$/u, '');
      return await BasisWebOidc.fetchJson(issuer + '/.well-known/openid-configuration');
    },

    exchangeCode: async function(config, code, verifier, redirectUri) {
      if (!config.tokenEndpoint) throw new Error('Web SSO tokenEndpoint is not configured.');
      var body = new URLSearchParams();
      body.set('grant_type', 'authorization_code');
      body.set('code', code);
      body.set('redirect_uri', redirectUri);
      body.set('code_verifier', verifier);
      return await BasisWebOidc.fetchJson(config.tokenEndpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString(),
      });
    },

    refresh: async function(config, refreshToken) {
      if (!config.tokenEndpoint) throw new Error('Web SSO tokenEndpoint is not configured.');
      var body = new URLSearchParams();
      body.set('grant_type', 'refresh_token');
      body.set('refresh_token', refreshToken);
      return await BasisWebOidc.fetchJson(config.tokenEndpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString(),
      });
    },

    optionalJson: async function(url, accessToken) {
      if (!url || !accessToken) return null;
      try {
        return await BasisWebOidc.fetchJson(url, { headers: { Authorization: 'Bearer ' + accessToken } });
      } catch (_) {
        return null;
      }
    },

    complete: async function(gameObjectName, config, pending, callback) {
      var discovery = await BasisWebOidc.discovery(config);
      var tokenResponse = await BasisWebOidc.exchangeCode(
        config,
        callback.code,
        pending.verifier,
        pending.redirectUri,
      );
      var accessToken = tokenResponse.access_token || '';
      var jwks = await BasisWebOidc.fetchJson(discovery.jwks_uri);
      var userInfo = await BasisWebOidc.optionalJson(discovery.userinfo_endpoint, accessToken);
      sessionStorage.removeItem(BasisWebOidc.pendingKey);
      BasisWebOidc.publish(gameObjectName, {
        success: true,
        discovery: discovery,
        jwks: jwks.keys || [],
        nonce: pending.nonce,
        tokenResponse: tokenResponse,
        userInfo: userInfo,
      });
    },

    start: async function(gameObjectName, config, prompt) {
      var callbackJson = sessionStorage.getItem(BasisWebOidc.callbackKey);
      if (callbackJson) {
        sessionStorage.removeItem(BasisWebOidc.callbackKey);
        var callback = JSON.parse(callbackJson);
        var pendingJson = sessionStorage.getItem(BasisWebOidc.pendingKey);
        var pending = pendingJson ? JSON.parse(pendingJson) : null;
        if (!pending || callback.state !== pending.state) {
          throw new Error('OIDC state mismatch; sign-in was aborted.');
        }
        if (callback.error) throw new Error(callback.error_description || callback.error);
        if (!callback.code) throw new Error('OIDC callback contained no authorization code.');
        await BasisWebOidc.complete(gameObjectName, config, pending, callback);
        return;
      }

      var discovery = await BasisWebOidc.discovery(config);
      var verifier = BasisWebOidc.randomToken(32);
      var state = BasisWebOidc.randomToken(16);
      var nonce = BasisWebOidc.randomToken(16);
      var redirectUri = BasisWebOidc.redirectUri(config);
      var challenge = await BasisWebOidc.codeChallenge(verifier);
      var params = new URLSearchParams();
      params.set('response_type', 'code');
      params.set('client_id', config.clientId);
      params.set('redirect_uri', redirectUri);
      params.set('scope', (config.scopes || ['openid', 'profile', 'email']).join(' '));
      params.set('state', state);
      params.set('nonce', nonce);
      params.set('code_challenge', challenge);
      params.set('code_challenge_method', 'S256');
      for (var key in (config.extraAuthParams || {})) {
        if (Object.prototype.hasOwnProperty.call(config.extraAuthParams, key)) params.set(key, config.extraAuthParams[key] || '');
      }
      if (prompt) params.set('prompt', prompt);
      var pending = {
        state: state,
        nonce: nonce,
        verifier: verifier,
        redirectUri: redirectUri,
      };
      sessionStorage.setItem(BasisWebOidc.pendingKey, JSON.stringify(pending));
      // Web enrollment URLs carry a one-shot configUrl. Do not restore that URL
      // after OAuth, otherwise the WebGL bootstrap downloads the same enrollment
      // token a second time and the broker correctly returns 410 Gone.
      var returnUrl = new URL(window.location.href);
      returnUrl.searchParams.delete('basisEnrollment');
      returnUrl.searchParams.delete('configUrl');
      sessionStorage.setItem(BasisWebOidc.returnUrlKey, returnUrl.toString());
      window.location.assign(discovery.authorization_endpoint + (discovery.authorization_endpoint.indexOf('?') >= 0 ? '&' : '?') + params.toString());
    },

    refreshSession: async function(gameObjectName, config, refreshToken) {
      var discovery = await BasisWebOidc.discovery(config);
      var tokenResponse = await BasisWebOidc.refresh(config, refreshToken);
      var accessToken = tokenResponse.access_token || '';
      var jwks = await BasisWebOidc.fetchJson(discovery.jwks_uri);
      var userInfo = await BasisWebOidc.optionalJson(discovery.userinfo_endpoint, accessToken);
      BasisWebOidc.publish(gameObjectName, {
        success: true,
        discovery: discovery,
        jwks: jwks.keys || [],
        nonce: null,
        tokenResponse: tokenResponse,
        userInfo: userInfo,
      });
    },
  },

  BasisWebOidcBegin__deps: ['$BasisWebOidc'],
  BasisWebOidcBegin: function(gameObjectNamePointer, configJsonPointer, promptPointer) {
    var gameObjectName = UTF8ToString(gameObjectNamePointer);
    var config = JSON.parse(UTF8ToString(configJsonPointer));
    var prompt = UTF8ToString(promptPointer);
    BasisWebOidc.start(gameObjectName, config, prompt).catch(function(error) {
      BasisWebOidc.publish(gameObjectName, { success: false, error: error && error.message ? error.message : String(error) });
    });
  },

  BasisWebOidcRefresh__deps: ['$BasisWebOidc'],
  BasisWebOidcRefresh: function(gameObjectNamePointer, configJsonPointer, refreshTokenPointer) {
    var gameObjectName = UTF8ToString(gameObjectNamePointer);
    var config = JSON.parse(UTF8ToString(configJsonPointer));
    var refreshToken = UTF8ToString(refreshTokenPointer);
    BasisWebOidc.refreshSession(gameObjectName, config, refreshToken).catch(function(error) {
      BasisWebOidc.publish(gameObjectName, { success: false, error: error && error.message ? error.message : String(error) });
    });
  },

  BasisWebOidcHasPendingCallback__deps: ['$BasisWebOidc'],
  BasisWebOidcHasPendingCallback: function() {
    return sessionStorage.getItem(BasisWebOidc.callbackKey) ? 1 : 0;
  },

  BasisWebEnrollmentStoreConfig__deps: ['$BasisWebOidc'],
  BasisWebEnrollmentStoreConfig: function(jsonPointer) {
    BasisWebOidc.storeEnrollmentConfig(UTF8ToString(jsonPointer));
  },

  BasisWebEnrollmentReadConfig__deps: ['$BasisWebOidc'],
  BasisWebEnrollmentReadConfig: function() {
    return stringToNewUTF8(BasisWebOidc.readEnrollmentConfig());
  },
});
