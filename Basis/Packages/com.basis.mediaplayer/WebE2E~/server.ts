import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { extname, normalize, resolve, sep } from "node:path";
import { readFile, stat } from "node:fs/promises";

const buildPath = process.env.BASIS_WEB_BUILD_PATH;
if (!buildPath) throw new Error("BASIS_WEB_BUILD_PATH must point to a development WebGL build.");

const buildRoot = resolve(buildPath);
const port = Number.parseInt(process.env.BASIS_WEB_MEDIA_E2E_PORT ?? "4176", 10);
const appOrigin = `http://app.lvh.me:${port}`;
let mediaFixture: Buffer | null = null;

const fixturePage = `<!doctype html>
<meta charset="utf-8">
<script>
window.fixtureReady = false;
(async () => {
  const canvas = document.createElement('canvas');
  canvas.width = 64;
  canvas.height = 64;
  const context = canvas.getContext('2d');
  let frame = 0;
  const timer = setInterval(() => {
    context.fillStyle = frame++ % 2 === 0 ? '#ff0055' : '#00aaff';
    context.fillRect(0, 0, 64, 64);
  }, 50);
  const audioContext = new AudioContext();
  const oscillator = audioContext.createOscillator();
  const destination = audioContext.createMediaStreamDestination();
  oscillator.frequency.value = 880;
  oscillator.connect(destination);
  oscillator.start();
  const stream = canvas.captureStream(15);
  stream.addTrack(destination.stream.getAudioTracks()[0]);
  const mimeType = 'video/webm;codecs=vp8,opus';
  if (!MediaRecorder.isTypeSupported(mimeType)) throw new Error('Chromium does not support the VP8/Opus E2E codec.');
  const recorder = new MediaRecorder(stream, { mimeType });
  const chunks = [];
  recorder.ondataavailable = event => chunks.push(event.data);
  const stopped = new Promise(resolve => recorder.onstop = resolve);
  recorder.start(100);
  await new Promise(resolve => setTimeout(resolve, 8000));
  recorder.stop();
  await stopped;
  clearInterval(timer);
  oscillator.stop();
  await audioContext.close();
  const response = await fetch('/__basis/media-fixture', {
    method: 'POST',
    body: new Blob(chunks, { type: mimeType }),
  });
  if (!response.ok) throw new Error('Media fixture upload failed.');
  window.fixtureReady = true;
})();
</script>`;

function contentType(pathname: string): string {
  const uncompressedPath = pathname.replace(/\.(br|gz)$/, "");
  switch (extname(uncompressedPath)) {
    case ".html": return "text/html; charset=utf-8";
    case ".js": return "text/javascript; charset=utf-8";
    case ".css": return "text/css; charset=utf-8";
    case ".json": return "application/json";
    case ".wasm": return "application/wasm";
    case ".data": return "application/octet-stream";
    case ".png": return "image/png";
    case ".jpg":
    case ".jpeg": return "image/jpeg";
    default: return "application/octet-stream";
  }
}

function sendMedia(request: IncomingMessage, response: ServerResponse, media: Buffer): void {
  response.setHeader("Access-Control-Allow-Origin", appOrigin);
  response.setHeader("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
  response.setHeader("Cross-Origin-Resource-Policy", "cross-origin");
  response.setHeader("Vary", "Origin");
  response.setHeader("Accept-Ranges", "bytes");
  response.setHeader("Content-Type", "video/webm");

  const range = request.headers.range;
  if (!range) {
    response.writeHead(200, { "Content-Length": media.byteLength });
    if (request.method === "HEAD") response.end();
    else response.end(media);
    return;
  }

  const match = /^bytes=(\d+)-(\d*)$/.exec(range);
  if (!match) {
    response.writeHead(416, { "Content-Range": `bytes */${media.byteLength}` });
    response.end();
    return;
  }
  const start = Number.parseInt(match[1], 10);
  const requestedEnd = match[2] ? Number.parseInt(match[2], 10) : media.byteLength - 1;
  const end = Math.min(requestedEnd, media.byteLength - 1);
  if (start > end || start >= media.byteLength) {
    response.writeHead(416, { "Content-Range": `bytes */${media.byteLength}` });
    response.end();
    return;
  }
  const body = media.subarray(start, end + 1);
  response.writeHead(206, {
    "Content-Length": body.byteLength,
    "Content-Range": `bytes ${start}-${end}/${media.byteLength}`,
  });
  if (request.method === "HEAD") response.end();
  else response.end(body);
}

async function receiveBody(request: IncomingMessage): Promise<Buffer> {
  const chunks: Buffer[] = [];
  let length = 0;
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    length += buffer.byteLength;
    if (length > 4 * 1024 * 1024) throw new Error("Media fixture exceeds 4 MiB.");
    chunks.push(buffer);
  }
  return Buffer.concat(chunks);
}

async function serveBuild(pathname: string, response: ServerResponse): Promise<void> {
  const normalizedPath = normalize(decodeURIComponent(pathname === "/" ? "/index.html" : pathname));
  const filePath = resolve(buildRoot, `.${normalizedPath}`);
  if (filePath !== buildRoot && !filePath.startsWith(`${buildRoot}${sep}`)) {
    response.writeHead(403).end();
    return;
  }
  try {
    const fileStat = await stat(filePath);
    if (!fileStat.isFile()) throw new Error("Not a file");
    const body = await readFile(filePath);
    response.setHeader("Content-Type", contentType(filePath));
    if (filePath.endsWith(".gz")) response.setHeader("Content-Encoding", "gzip");
    if (filePath.endsWith(".br")) response.setHeader("Content-Encoding", "br");
    response.setHeader("Content-Length", body.byteLength);
    response.writeHead(200).end(body);
  } catch {
    response.writeHead(404).end();
  }
}

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? "/", appOrigin);
    const isMediaHost = (request.headers.host ?? "").startsWith("media.lvh.me:");
    if (isMediaHost && url.pathname === "/__basis/media-fixture.html") {
      response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" }).end(fixturePage);
      return;
    }
    if (isMediaHost && url.pathname === "/__basis/media-fixture" && request.method === "POST") {
      mediaFixture = await receiveBody(request);
      response.writeHead(204).end();
      return;
    }
    if (isMediaHost && url.pathname === "/__basis/media-fixture.webm" && mediaFixture) {
      sendMedia(request, response, mediaFixture);
      return;
    }
    await serveBuild(url.pathname, response);
  } catch (error) {
    response.writeHead(500, { "Content-Type": "text/plain; charset=utf-8" }).end(String(error));
  }
});

server.listen(port, "0.0.0.0");
