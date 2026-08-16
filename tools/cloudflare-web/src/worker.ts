import {
  BROWSER_CACHE_CLEAR_PATH,
  browserCacheClearResponse,
  injectBrowserCacheControls,
} from './browser-cache.ts';
import { rewriteBuildArtifactReferences } from './build-artifacts.ts';

export const BROWSER_SSO_CALLBACK_PATH = '/sso-callback';

interface R2ObjectBody {
  body: ReadableStream;
  size: number;
  httpEtag: string;
  writeHttpMetadata(headers: Headers): void;
}

interface R2ObjectMetadata {
  size: number;
  httpEtag: string;
  writeHttpMetadata(headers: Headers): void;
}

interface R2Bucket {
  get(key: string): Promise<R2ObjectBody | null>;
  head(key: string): Promise<R2ObjectMetadata | null>;
}

interface Environment {
  WEB_BUILD: R2Bucket;
  SSO_CONFIG_URL: string;
}

interface CloudflareResponseInit extends ResponseInit {
  encodeBody?: 'manual';
}

function keyFromRequest(request: Request): string | null {
  const pathname = new URL(request.url).pathname;
  try {
    const key = decodeURIComponent(pathname.slice(1));
    if (key.split('/').includes('..')) return null;
    return key || 'index.html';
  } catch {
    return null;
  }
}

export function cacheControlFor(key: string): string {
  if (key === 'index.html') return 'no-store';
  if (key.startsWith('Build/')) {
    return 'public, max-age=31536000, s-maxage=31536000, immutable, no-transform';
  }
  if (key.endsWith('/catalog.bin') || key.endsWith('/catalog.hash') || key.endsWith('/settings.json')) {
    return 'public, max-age=300, s-maxage=300, must-revalidate';
  }
  return 'public, max-age=86400, s-maxage=31536000, immutable, no-transform';
}

export function contentEncodingFor(key: string): 'gzip' | 'br' | null {
  if (key.endsWith('.gz')) return 'gzip';
  if (key.endsWith('.br')) return 'br';
  return null;
}

export function responseInitFor(key: string, headers: Headers): CloudflareResponseInit {
  return contentEncodingFor(key) === null
    ? { headers }
    : { headers, encodeBody: 'manual' };
}

export function enableUnityBuildBrowserCache(loader: string): string {
  const defaultCacheControl = 'return (url == Module.dataUrl || url.match(/\\.bundle/)) ? "must-revalidate" : "no-store";';
  const cachedBuildCacheControl = 'return url.indexOf("/Build/") >= 0 ? "immutable" : (url == Module.dataUrl || url.match(/\\.bundle/)) ? "must-revalidate" : "no-store";';
  return loader.replace(defaultCacheControl, cachedBuildCacheControl);
}

function responseHeaders(object: R2ObjectMetadata, key: string): Headers {
  const headers = new Headers();
  object.writeHttpMetadata(headers);
  if (key !== 'index.html') headers.set('etag', object.httpEtag);
  headers.set('accept-ranges', 'bytes');
  headers.set('cache-control', cacheControlFor(key));
  headers.set('x-content-type-options', 'nosniff');
  const contentEncoding = contentEncodingFor(key);
  if (contentEncoding !== null) headers.set('content-encoding', contentEncoding);
  return headers;
}

export function browserSsoCallbackResponse(): Response {
  const body = `<!doctype html>
<meta charset="utf-8">
<title>Basis SSO</title>
<p>認証結果をBasisへ戻しています。</p>
<script>
(() => {
  const params = new URLSearchParams(window.location.search);
  const result = {};
  for (const key of ['code', 'state', 'error', 'error_description']) {
    const value = params.get(key);
    if (value !== null) result[key] = value;
  }
  const returnUrl = sessionStorage.getItem('basis.sso.returnUrl') || '/';
  sessionStorage.removeItem('basis.sso.returnUrl');
  sessionStorage.setItem('basis.sso.callback', JSON.stringify(result));
  window.location.replace(returnUrl);
})();
</script>`;
  return new Response(body, {
    headers: {
      'cache-control': 'no-store',
      'content-security-policy': "default-src 'none'; script-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'",
      'content-type': 'text/html; charset=utf-8',
      'x-content-type-options': 'nosniff',
    },
  });
}

export async function webSsoConfigurationResponse(
  url: string,
  fetchConfiguration: typeof fetch = fetch,
): Promise<Response> {
  const upstream = await fetchConfiguration(url, { headers: { accept: 'application/json' } });
  if (!upstream.ok) return new Response('SSO configuration unavailable', { status: 503 });
  return new Response(upstream.body, {
    headers: {
      'cache-control': 'no-store',
      'content-type': 'application/json; charset=utf-8',
      'x-content-type-options': 'nosniff',
    },
  });
}

export default {
  async fetch(request: Request, environment: Environment): Promise<Response> {
    const requestUrl = new URL(request.url);
    if (requestUrl.pathname === BROWSER_SSO_CALLBACK_PATH) {
      return request.method === 'GET' ? browserSsoCallbackResponse() : new Response('Method Not Allowed', {
        status: 405,
        headers: { allow: 'GET' },
      });
    }
    if (requestUrl.pathname === BROWSER_CACHE_CLEAR_PATH) {
      return browserCacheClearResponse(request);
    }
    if (requestUrl.pathname === '/StreamingAssets/basis-sso.json') {
      return request.method === 'GET'
        ? webSsoConfigurationResponse(environment.SSO_CONFIG_URL)
        : new Response('Method Not Allowed', { status: 405, headers: { allow: 'GET' } });
    }

    if (request.method !== 'GET' && request.method !== 'HEAD') {
      return new Response('Method Not Allowed', {
        status: 405,
        headers: { allow: 'GET, HEAD' },
      });
    }

    const key = keyFromRequest(request);
    if (key === null) return new Response('Bad Request', { status: 400 });

    if (request.method === 'HEAD') {
      const object = await environment.WEB_BUILD.head(key);
      if (object === null) return new Response('Not Found', { status: 404 });
      const headers = responseHeaders(object, key);
      headers.set('content-length', object.size.toString());
      return new Response(null, { headers });
    }

    const object = await environment.WEB_BUILD.get(key);
    if (object === null) return new Response('Not Found', { status: 404 });

    const headers = responseHeaders(object, key);
    if (key === 'Build/basis.loader.js') {
      headers.delete('content-length');
      headers.delete('etag');
      return new Response(enableUnityBuildBrowserCache(await new Response(object.body).text()), { headers });
    }
    if (key === 'index.html') {
      headers.delete('content-length');
      const html = await new Response(object.body).text();
      const versionedHtml = await rewriteBuildArtifactReferences(html, async artifactKey => {
        const artifact = await environment.WEB_BUILD.head(artifactKey);
        if (artifact === null) throw new Error(`Build artifact not found: ${artifactKey}`);
        return artifact.httpEtag;
      });
      return new Response(
        injectBrowserCacheControls(versionedHtml),
        { headers },
      );
    }

    headers.set('content-length', object.size.toString());
    return new Response(object.body, responseInitFor(key, headers));
  },
};
