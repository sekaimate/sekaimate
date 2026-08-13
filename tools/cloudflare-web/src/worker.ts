interface R2Range {
  offset: number;
  length: number;
}

interface R2ObjectBody {
  body: ReadableStream;
  size: number;
  range?: R2Range;
  httpEtag: string;
  writeHttpMetadata(headers: Headers): void;
}

interface R2ObjectMetadata {
  size: number;
  httpEtag: string;
  writeHttpMetadata(headers: Headers): void;
}

interface R2Bucket {
  get(key: string, options: { range: Headers }): Promise<R2ObjectBody | null>;
  head(key: string): Promise<R2ObjectMetadata | null>;
}

interface Environment {
  WEB_BUILD: R2Bucket;
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

function responseHeaders(object: R2ObjectMetadata): Headers {
  const headers = new Headers();
  object.writeHttpMetadata(headers);
  headers.set('etag', object.httpEtag);
  headers.set('accept-ranges', 'bytes');
  headers.set('x-content-type-options', 'nosniff');
  return headers;
}

export default {
  async fetch(request: Request, environment: Environment): Promise<Response> {
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
      const headers = responseHeaders(object);
      headers.set('content-length', object.size.toString());
      return new Response(null, { headers });
    }

    const object = await environment.WEB_BUILD.get(key, { range: request.headers });
    if (object === null) return new Response('Not Found', { status: 404 });

    const headers = responseHeaders(object);
    if (object.range !== undefined) {
      const end = object.range.offset + object.range.length - 1;
      headers.set('content-range', `bytes ${object.range.offset}-${end}/${object.size}`);
      headers.set('content-length', object.range.length.toString());
      return new Response(object.body, { status: 206, headers });
    }

    headers.set('content-length', object.size.toString());
    return new Response(object.body, { headers });
  },
};
