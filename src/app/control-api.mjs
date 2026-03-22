import http from 'node:http';

export const DEFAULT_CONTROL_API_HOST = '127.0.0.1';
export const DEFAULT_CONTROL_API_PORT = 3199;

function createJsonResponse(res, statusCode, payload) {
  const body = JSON.stringify(payload);
  res.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body)
  });
  res.end(body);
}

async function readJsonBody(req) {
  const chunks = [];

  for await (const chunk of req) {
    chunks.push(chunk);
  }

  const raw = Buffer.concat(chunks).toString('utf8').trim();

  if (!raw) {
    return {};
  }

  return JSON.parse(raw);
}

export function createControlApiServer({
  host = DEFAULT_CONTROL_API_HOST,
  port = DEFAULT_CONTROL_API_PORT,
  logger,
  getStatus,
  getConfig,
  updateConfig,
  startRuntime,
  stopRuntime
}) {
  const server = http.createServer(async (req, res) => {
    try {
      const method = req.method ?? 'GET';
      const url = new URL(req.url ?? '/', `http://${host}:${port}`);

      if (method === 'GET' && url.pathname === '/status') {
        createJsonResponse(res, 200, await getStatus());
        return;
      }

      if (method === 'GET' && url.pathname === '/config') {
        createJsonResponse(res, 200, await getConfig());
        return;
      }

      if (method === 'PUT' && url.pathname === '/config') {
        const body = await readJsonBody(req);
        createJsonResponse(res, 200, await updateConfig(body));
        return;
      }

      if (method === 'POST' && url.pathname === '/start') {
        createJsonResponse(res, 200, await startRuntime());
        return;
      }

      if (method === 'POST' && url.pathname === '/stop') {
        createJsonResponse(res, 200, await stopRuntime());
        return;
      }

      createJsonResponse(res, 404, {
        error: 'Not found'
      });
    } catch (error) {
      logger.error?.(`[control] Request failed: ${error instanceof Error ? error.message : String(error)}`);
      createJsonResponse(res, 500, {
        error: error instanceof Error ? error.message : String(error)
      });
    }
  });

  async function start() {
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(port, host, () => {
        server.off('error', reject);
        resolve();
      });
    });
  }

  async function stop() {
    await new Promise((resolve, reject) => {
      server.close((error) => {
        if (error) {
          reject(error);
          return;
        }

        resolve();
      });
    });
  }

  return {
    host,
    port,
    start,
    stop
  };
}
