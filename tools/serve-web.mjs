import { createServer } from "node:http";
import { promises as fs } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(process.argv[2]);
const port = Number(process.argv[3] || 4173);
// Keep the local default loopback-only, but allow container deployments to
// listen on the pod interface. Kubernetes Services cannot reach 127.0.0.1.
const host = process.env.HOST || "127.0.0.1";
const callbackKey = "basis.sso.callback";
const returnUrlKey = "basis.sso.returnUrl";

const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".wasm": "application/wasm",
  ".data": "application/octet-stream",
  ".bundle": "application/octet-stream",
  ".bee": "application/octet-stream",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
};

const callbackHtml = `<!doctype html>
<meta charset="utf-8">
<title>Returning to Basis…</title>
<script>
  const query = new URLSearchParams(window.location.search);
  const result = Object.fromEntries(query.entries());
  sessionStorage.setItem(${JSON.stringify(callbackKey)}, JSON.stringify(result));
  const storedReturnUrl = sessionStorage.getItem(${JSON.stringify(returnUrlKey)}) || "/";
  const returnUrlObject = new URL(storedReturnUrl, window.location.origin);
  const returnUrl = returnUrlObject.toString();
  sessionStorage.removeItem(${JSON.stringify(returnUrlKey)});
  window.location.replace(returnUrl);
</script>
<p>Returning to Basis…</p>`;

// Browsers reuse a stored response only when the server offers a validator.
// The size and mtime pair changes whenever a new build lands in the image, and
// hashing 120MB of wasm on every request would not be worth the accuracy.
function etagFor(stat) {
  return `"${stat.size.toString(16)}-${Math.trunc(stat.mtimeMs).toString(16)}"`;
}

function matchesEntityTag(header, entityTag) {
  if (!header) return false;
  return header.split(",").some((candidate) => candidate.trim().replace(/^W\//, "") === entityTag);
}

// tools/publish-web-image.sh precompresses the large Unity artifacts next to
// the originals. Range requests keep using the uncompressed file, because a
// byte range of a gzip stream is not the range the client asked for.
async function precompressedVariant(filePath, request) {
  if (request.headers.range) return null;
  if (filePath.endsWith(".gz") || filePath.endsWith(".br")) return null;
  const acceptsGzip = (request.headers["accept-encoding"] || "")
    .split(",")
    .some((value) => value.trim().split(";")[0] === "gzip");
  if (!acceptsGzip) return null;

  const candidate = `${filePath}.gz`;
  try {
    return { path: candidate, stat: await fs.stat(candidate) };
  } catch {
    return null;
  }
}

function safePath(urlPath) {
  const pathname = decodeURIComponent(urlPath.split("?")[0]);
  const candidate = path.resolve(root, `.${pathname}`);
  return candidate === root || candidate.startsWith(`${root}${path.sep}`) ? candidate : null;
}

const server = createServer(async (request, response) => {
  try {
    if (request.method === "OPTIONS") {
      response.writeHead(204, {
        "access-control-allow-origin": "*",
        "access-control-allow-methods": "GET, HEAD, OPTIONS",
        "access-control-allow-headers": "Range, Content-Type",
        "access-control-max-age": "86400",
      });
      response.end();
      return;
    }

    const requestUrl = new URL(request.url || "/", `http://${request.headers.host}`);
    if (requestUrl.pathname === "/sso-callback") {
      response.writeHead(200, { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" });
      response.end(callbackHtml);
      return;
    }

    let filePath = safePath(requestUrl.pathname);
    if (!filePath) {
      response.writeHead(403);
      response.end("Forbidden");
      return;
    }

    let stat;
    try {
      stat = await fs.stat(filePath);
    } catch {
      // Only extensionless paths are SPA routes. Returning index.html for a missing
      // .js/.wasm/.data/.bundle/BEE file makes Unity fail with a misleading blank page,
      // because the browser receives HTML with a successful 200 status.
      if (path.extname(requestUrl.pathname) !== "") {
        response.writeHead(404, {
          "content-type": "text/plain; charset=utf-8",
          "access-control-allow-origin": "*",
        });
        response.end("Not found");
        return;
      }
      filePath = path.join(root, "index.html");
      stat = await fs.stat(filePath);
    }
    if (stat.isDirectory()) {
      filePath = path.join(filePath, "index.html");
      stat = await fs.stat(filePath);
    }

    const precompressed = await precompressedVariant(filePath, request);
    if (precompressed) {
      filePath = precompressed.path;
      stat = precompressed.stat;
    }

    const entityTag = etagFor(stat);
    if (matchesEntityTag(request.headers["if-none-match"], entityTag)) {
      response.writeHead(304, {
        "cache-control": "no-cache",
        etag: entityTag,
        vary: "accept-encoding",
        "access-control-allow-origin": "*",
      });
      response.end();
      return;
    }

    const fileSize = stat.size;
    const range = request.headers.range;
    let statusCode = 200;
    let contentLength = fileSize;
    let contentRange;
    let body;

    if (range) {
      const match = /^bytes=(\d+)-(\d*)$/.exec(range.trim());
      if (!match) {
        response.writeHead(416, {
          "content-range": `bytes */${fileSize}`,
          "access-control-allow-origin": "*",
        });
        response.end();
        return;
      }

      const start = Number(match[1]);
      const requestedEnd = match[2] === "" ? fileSize - 1 : Number(match[2]);
      const end = Math.min(requestedEnd, fileSize - 1);
      if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start >= fileSize || start > end) {
        response.writeHead(416, {
          "content-range": `bytes */${fileSize}`,
          "access-control-allow-origin": "*",
        });
        response.end();
        return;
      }

      statusCode = 206;
      contentLength = end - start + 1;
      contentRange = `bytes ${start}-${end}/${fileSize}`;
      if (request.method !== "HEAD") {
        body = await fs.open(filePath).then(async (handle) => {
          try {
            const buffer = Buffer.alloc(contentLength);
            await handle.read(buffer, 0, contentLength, start);
            return buffer;
          } finally {
            await handle.close();
          }
        });
      }
    } else if (request.method !== "HEAD") {
      body = await fs.readFile(filePath);
    }

    const extension = path.extname(filePath).toLowerCase();
    const contentExtension = extension === ".gz" || extension === ".br"
      ? path.extname(filePath.slice(0, -extension.length)).toLowerCase()
      : extension;
    const headers = {
      "content-type": contentTypes[contentExtension] || "application/octet-stream",
      "content-length": String(contentLength),
      "accept-ranges": "bytes",
      "cache-control": "no-cache",
      "etag": entityTag,
      "vary": "accept-encoding",
      "access-control-allow-origin": "*",
      "access-control-expose-headers": "Content-Range, Content-Length, Accept-Ranges, ETag",
    };
    if (extension === ".gz") headers["content-encoding"] = "gzip";
    if (extension === ".br") headers["content-encoding"] = "br";
    if (contentRange) headers["content-range"] = contentRange;
    response.writeHead(statusCode, headers);
    response.end(body);
  } catch (error) {
    response.writeHead(500);
    response.end(String(error));
  }
});

server.listen(port, host, () => {
  console.log(`Serving ${root} at http://${host}:${port}/`);
  console.log(`World BEE: http://${host}:${port}/BEE/world.BEE`);
});
