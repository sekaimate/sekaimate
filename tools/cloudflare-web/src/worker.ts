import {
  BROWSER_CACHE_CLEAR_PATH,
  browserCacheClearResponse,
  injectBrowserCacheControls,
} from './browser-cache.ts';
import { rewriteBuildArtifactReferences } from './build-artifacts.ts';

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
  if (key === 'index.html') return 'no-cache';
  if (key.startsWith('Build/')) {
    return 'public, max-age=0, s-maxage=31536000, must-revalidate, no-transform';
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

function responseHeaders(object: R2ObjectMetadata, key: string): Headers {
  const headers = new Headers();
  object.writeHttpMetadata(headers);
  headers.set('etag', object.httpEtag);
  headers.set('accept-ranges', 'bytes');
  headers.set('cache-control', cacheControlFor(key));
  headers.set('x-content-type-options', 'nosniff');
  const contentEncoding = contentEncodingFor(key);
  if (contentEncoding !== null) headers.set('content-encoding', contentEncoding);
  return headers;
}

export default {
  async fetch(request: Request, environment: Environment): Promise<Response> {
    const requestUrl = new URL(request.url);
    if (requestUrl.pathname === BROWSER_CACHE_CLEAR_PATH) {
      return browserCacheClearResponse(request);
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
    if (key === 'index.html') {
      headers.delete('content-length');
      const html = await new Response(object.body).text();
      return new Response(
        injectBrowserCacheControls(rewriteBuildArtifactReferences(html, object.httpEtag)),
        { headers },
      );
    }

    headers.set('content-length', object.size.toString());
    return new Response(object.body, responseInitFor(key, headers));
  },
};
