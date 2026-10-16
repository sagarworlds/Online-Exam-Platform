// Serves the production build of the web app for the browser tests, the way a static host does: a file that exists is sent as it is, a
// path with no file extension falls back to index.html so the router can handle it, and anything else that is missing is a 404. The
// service worker, the manifest and the icons are plain files, so they are served from their real paths.
//
// It is a test helper, not a deployment: it listens on the loopback address only and never serves a path outside the build folder.
import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer, request as httpRequest } from 'node:http';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(fileURLToPath(new URL('../dist/web/browser', import.meta.url)));
const port = Number(process.env.E2E_PORT ?? 4300);
// When set, a request under /api goes to the real API at this address, as a static host's rewrite rule sends it in a deployment; the
// path loses its /api prefix on the way. When not set, nothing answers for the API (see below).
const apiUrl = process.env.E2E_API_URL ? new URL(process.env.E2E_API_URL) : null;

const contentTypes = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
};

if (!existsSync(join(root, 'index.html'))) {
  console.error(`No build found at ${root}. Run "npm run build" in apps/web first.`);
  process.exit(1);
}

/** Sends a request on to the real API and its answer back, unchanged. */
function forwardToApi(request, response, pathname, search) {
  const upstream = httpRequest(
    {
      hostname: apiUrl.hostname,
      port: apiUrl.port,
      method: request.method,
      path: pathname.slice('/api'.length) + search,
      headers: { ...request.headers, host: apiUrl.host },
    },
    (answer) => {
      response.writeHead(answer.statusCode ?? 502, answer.headers);
      answer.pipe(response);
    },
  );
  upstream.on('error', () => {
    response.writeHead(502).end();
  });
  request.pipe(upstream);
}

/** The file a request path names inside the build folder, or null when it would leave the folder. */
function resolveFile(urlPath) {
  const requested = normalize(join(root, decodeURIComponent(urlPath)));
  return requested === root || requested.startsWith(root + sep) ? requested : null;
}

const server = createServer((request, response) => {
  const { pathname, search } = new URL(request.url ?? '/', 'http://localhost');

  if (apiUrl !== null && pathname.startsWith('/api/')) {
    forwardToApi(request, response, pathname, search);
    return;
  }

  let file;
  try {
    file = resolveFile(pathname);
  } catch {
    // A path that is not valid percent-encoding cannot name a file.
    file = null;
  }

  if (file === null) {
    response.writeHead(400).end();
    return;
  }

  // The API lives on another service behind the same address in a real deployment. With no API address given, nothing here answers for
  // it, so a test that wants API answers stubs them in the browser, and one that does not sees the API as unreachable.
  if (pathname.startsWith('/api/')) {
    response.writeHead(404).end();
    return;
  }

  const isFile = existsSync(file) && statSync(file).isFile();
  if (!isFile && extname(pathname) !== '') {
    response.writeHead(404).end();
    return;
  }

  const served = isFile ? file : join(root, 'index.html');
  response.writeHead(200, {
    'Content-Type': contentTypes[extname(served)] ?? 'application/octet-stream',
    // The service worker and its manifest must never be served stale, or a test could run against the previous build.
    'Cache-Control': 'no-cache',
  });
  createReadStream(served).pipe(response);
});

server.listen(port, '127.0.0.1', () => console.log(`Serving ${root} at http://127.0.0.1:${port}`));
