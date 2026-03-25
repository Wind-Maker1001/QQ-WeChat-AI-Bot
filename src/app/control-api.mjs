import http from 'node:http';

import { RuntimeConfigValidationError } from '../domain/runtime-config.mjs';

export const DEFAULT_CONTROL_API_HOST = '127.0.0.1';
export const DEFAULT_CONTROL_API_PORT = 3199;
export const DEFAULT_CONTROL_API_HOST_ENV_KEY = 'QQ_AI_BOT_CONTROL_API_HOST';
export const DEFAULT_CONTROL_API_PORT_ENV_KEY = 'QQ_AI_BOT_CONTROL_API_PORT';
export const DEFAULT_CONTROL_API_TOKEN_ENV_KEY = 'QQ_AI_BOT_CONTROL_API_TOKEN';

class ControlApiRequestError extends Error {
  constructor(message, statusCode = 400) {
    super(message);
    this.name = 'ControlApiRequestError';
    this.statusCode = statusCode;
  }
}

export function resolveDefaultControlApiHost() {
  const host = process.env[DEFAULT_CONTROL_API_HOST_ENV_KEY];
  return typeof host === 'string' && host.trim() ? host.trim() : DEFAULT_CONTROL_API_HOST;
}

export function resolveDefaultControlApiPort() {
  const rawPort = process.env[DEFAULT_CONTROL_API_PORT_ENV_KEY];
  const parsedPort = Number.parseInt(rawPort || '', 10);
  return Number.isInteger(parsedPort) && parsedPort > 0 ? parsedPort : DEFAULT_CONTROL_API_PORT;
}

export function resolveDefaultControlApiToken() {
  const token = process.env[DEFAULT_CONTROL_API_TOKEN_ENV_KEY];
  return typeof token === 'string' && token.trim() ? token.trim() : '';
}

function createJsonResponse(res, statusCode, payload, headers = {}) {
  const body = JSON.stringify(payload);
  res.writeHead(statusCode, {
    ...headers,
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body)
  });
  res.end(body);
}

function resolveAccessTokenValue(accessToken) {
  if (typeof accessToken === 'function') {
    return resolveAccessTokenValue(accessToken());
  }

  return typeof accessToken === 'string' && accessToken.trim() ? accessToken.trim() : '';
}

function isAuthorizedRequest(req, accessToken) {
  const expectedAccessToken = resolveAccessTokenValue(accessToken);

  if (!expectedAccessToken) {
    return true;
  }

  const authorization = req.headers.authorization;
  return typeof authorization === 'string' && authorization.trim() === `Bearer ${expectedAccessToken}`;
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

  try {
    return JSON.parse(raw);
  } catch {
    throw new ControlApiRequestError('Request body must be valid JSON.', 400);
  }
}

function resolveErrorStatusCode(error) {
  if (Number.isInteger(error?.statusCode) && error.statusCode >= 400 && error.statusCode < 600) {
    return error.statusCode;
  }

  if (error instanceof ControlApiRequestError) {
    return error.statusCode;
  }

  if (error instanceof RuntimeConfigValidationError) {
    return 400;
  }

  return 500;
}

export function createControlApiServer({
  host = resolveDefaultControlApiHost(),
  port = resolveDefaultControlApiPort(),
  accessToken = resolveDefaultControlApiToken(),
  logger,
  getStatus,
  getConfig,
  updateConfig,
  startRuntime,
  stopRuntime
}) {
  const server = http.createServer(async (req, res) => {
    try {
      if (!isAuthorizedRequest(req, accessToken)) {
        createJsonResponse(
          res,
          401,
          {
            error: 'Control API authentication failed.'
          },
          {
            'WWW-Authenticate': 'Bearer realm="qq-ai-bot-control-api"'
          }
        );
        return;
      }

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
      createJsonResponse(res, resolveErrorStatusCode(error), {
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

  function getAddress() {
    const address = server.address();

    if (!address || typeof address === 'string') {
      return null;
    }

    return {
      address: address.address,
      family: address.family,
      port: address.port
    };
  }

  return {
    host,
    port,
    start,
    stop,
    getAddress
  };
}
