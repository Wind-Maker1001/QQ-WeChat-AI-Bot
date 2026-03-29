import fs from 'node:fs/promises';
import path from 'node:path';

import dotenv from 'dotenv';

const ENV_FILE_NAME = '.env';
const ENV_EXAMPLE_FILE_NAME = '.env.example';

const KNOWN_KEY_ORDER = [
  'OPENAI_API_KEY',
  'OPENAI_DEFAULT_API_KEY',
  'OPENAI_ADVANCED_API_KEY',
  'OPENAI_MODEL',
  'OPENAI_DEFAULT_MODEL',
  'OPENAI_ADVANCED_MODEL',
  'OPENAI_BASE_URL',
  'OPENAI_DEFAULT_BASE_URL',
  'OPENAI_ADVANCED_BASE_URL',
  'OPENAI_DEFAULT_API_STYLE',
  'OPENAI_ADVANCED_API_STYLE',
  'OPENAI_DEFAULT_REASONING_EFFORT',
  'OPENAI_ADVANCED_REASONING_EFFORT',
  'OPENAI_DEFAULT_TEXT_VERBOSITY',
  'OPENAI_ADVANCED_TEXT_VERBOSITY',
  'OPENAI_DEFAULT_ENABLE_WEB_SEARCH',
  'OPENAI_ADVANCED_ENABLE_WEB_SEARCH',
  'OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER',
  'OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER',
  'OPENAI_ADVANCED_TRIGGER_PREFIXES',
  'DEEPSEEK_FALLBACK_ENABLED',
  'DEEPSEEK_API_KEY',
  'DEEPSEEK_MODEL',
  'DEEPSEEK_BASE_URL',
  'NAPCAT_WS_URL',
  'NAPCAT_TOKEN',
  'WECHAT_BRIDGE_URL',
  'WECHAT_BRIDGE_TOKEN',
  'WECHAT_BOT_PREFIX',
  'BOT_PREFIX',
  'BOT_SYSTEM_PROMPT',
  'BOT_PERSONA',
  'MAX_OUTPUT_CHARS',
  'ALLOWED_CHAT_IDS',
  'ALLOWED_USER_IDS'
];

const LEGACY_ENV_KEYS = ['ALLOWED_GROUP_IDS'];
const ENV_LOCK_TIMEOUT_MS = 5000;
const STALE_ENV_LOCK_AGE_MS = 15000;
const ATOMIC_RENAME_RETRY_DELAYS_MS = [10, 25, 50, 100];
const RETRYABLE_ATOMIC_RENAME_ERROR_CODES = new Set(['EACCES', 'EBUSY', 'EPERM']);

export const APP_ENV_KEYS = Object.freeze([...KNOWN_KEY_ORDER, ...LEGACY_ENV_KEYS]);

function encodeEnvValue(value) {
  return (value ?? '')
    .replace(/\\/g, '\\\\')
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .replace(/\n/g, '\\n');
}

function decodeEnvValue(value) {
  return (value ?? '').replace(/\\n/g, '\n').replace(/\\\\/g, '\\');
}

function unquote(value) {
  if (value.length >= 2) {
    const isDoubleQuoted = value.startsWith('"') && value.endsWith('"');
    const isSingleQuoted = value.startsWith("'") && value.endsWith("'");

    if (isDoubleQuoted || isSingleQuoted) {
      return value.slice(1, -1);
    }
  }

  return value;
}

function migrateLegacyAllowedChatIds(raw) {
  if (typeof raw !== 'string' || !raw.includes('ALLOWED_GROUP_IDS')) {
    return {
      raw,
      migrated: false
    };
  }

  const newline = raw.includes('\r\n') ? '\r\n' : '\n';
  const lines = raw.split(/\r?\n/);
  const nextLines = [];
  let hasAllowedChatIds = false;
  let legacyAllowedChatIdsValue = null;
  let migrated = false;

  for (const line of lines) {
    const trimmed = line.trim();

    if (!trimmed || trimmed.startsWith('#')) {
      nextLines.push(line);
      continue;
    }

    const separatorIndex = line.indexOf('=');

    if (separatorIndex <= 0) {
      nextLines.push(line);
      continue;
    }

    const key = line.slice(0, separatorIndex).trim();
    const rawValue = line.slice(separatorIndex + 1);

    if (key === 'ALLOWED_CHAT_IDS') {
      hasAllowedChatIds = true;
      nextLines.push(line);
      continue;
    }

    if (key === 'ALLOWED_GROUP_IDS') {
      if (!hasAllowedChatIds && legacyAllowedChatIdsValue === null) {
        legacyAllowedChatIdsValue = rawValue;
      }

      migrated = true;
      continue;
    }

    nextLines.push(line);
  }

  if (!hasAllowedChatIds && legacyAllowedChatIdsValue !== null) {
    nextLines.push(`ALLOWED_CHAT_IDS=${legacyAllowedChatIdsValue}`);
    migrated = true;
  }

  if (!migrated) {
    return {
      raw,
      migrated: false
    };
  }

  const normalizedRaw = `${nextLines.join(newline).replace(new RegExp(`${newline}+$`), '')}${newline}`;

  return {
    raw: normalizedRaw,
    migrated: true
  };
}

async function writeTextFileAtomically(filePath, content) {
  const tempPath = `${filePath}.tmp`;
  await fs.writeFile(tempPath, content, 'utf8');
  let lastError = null;

  for (let attempt = 0; attempt <= ATOMIC_RENAME_RETRY_DELAYS_MS.length; attempt += 1) {
    try {
      await fs.rename(tempPath, filePath);
      return;
    } catch (error) {
      lastError = error;

      if (
        !RETRYABLE_ATOMIC_RENAME_ERROR_CODES.has(error?.code) ||
        attempt === ATOMIC_RENAME_RETRY_DELAYS_MS.length
      ) {
        break;
      }

      await new Promise((resolve) => setTimeout(resolve, ATOMIC_RENAME_RETRY_DELAYS_MS[attempt]));
    }
  }

  await fs.rm(tempPath, { force: true }).catch(() => {});
  throw lastError;
}

function buildEnvLockPayload() {
  return JSON.stringify(
    {
      pid: process.pid,
      acquiredAt: new Date().toISOString()
    },
    null,
    2
  );
}

function parseEnvLockPayload(raw) {
  const safeParsed = (() => {
    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  })();

  if (!safeParsed || typeof safeParsed !== 'object' || Array.isArray(safeParsed)) {
    return {
      pid: null,
      acquiredAt: null
    };
  }

  return {
    pid: Number.isInteger(safeParsed.pid) && safeParsed.pid > 0 ? safeParsed.pid : null,
    acquiredAt:
      typeof safeParsed.acquiredAt === 'string' && safeParsed.acquiredAt.trim()
        ? safeParsed.acquiredAt
        : null
  };
}

function isProcessAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return false;
  }

  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function withEnvFileLock(envPath, task, timeoutMs = ENV_LOCK_TIMEOUT_MS) {
  const lockPath = `${envPath}.lock`;
  const startedAt = Date.now();

  while (true) {
    try {
      const lockHandle = await fs.open(lockPath, 'wx');

      try {
        await lockHandle.writeFile(`${buildEnvLockPayload()}\n`, 'utf8');
        return await task();
      } finally {
        await lockHandle.close();
        await fs.rm(lockPath, { force: true });
      }
    } catch (error) {
      if (error?.code !== 'EEXIST') {
        throw error;
      }

      try {
        const rawLockFile = await fs.readFile(lockPath, 'utf8');
        const stats = await fs.stat(lockPath);
        const lockAgeMs = Date.now() - stats.mtimeMs;
        const lockState = parseEnvLockPayload(rawLockFile);
        const ownerAlive = isProcessAlive(lockState.pid);

        if (lockAgeMs >= STALE_ENV_LOCK_AGE_MS && !ownerAlive) {
          await fs.rm(lockPath, { force: true });
          continue;
        }
      } catch (statError) {
        if (statError?.code === 'ENOENT') {
          continue;
        }

        throw statError;
      }

      if (Date.now() - startedAt >= timeoutMs) {
        throw new Error(`Timed out waiting for env file lock: ${lockPath}`);
      }

      await new Promise((resolve) => setTimeout(resolve, 25));
    }
  }
}

async function ensureEnvFile(cwd) {
  const envPath = path.join(cwd, ENV_FILE_NAME);
  const examplePath = path.join(cwd, ENV_EXAMPLE_FILE_NAME);

  try {
    await fs.access(envPath);
  } catch {
    try {
      await fs.copyFile(examplePath, envPath);
    } catch {
      await writeTextFileAtomically(envPath, '');
    }
  }

  return envPath;
}

function parseEnvValues(raw) {
  const migrationResult = migrateLegacyAllowedChatIds(raw);
  const parsed = dotenv.parse(migrationResult.raw);

  return {
    raw: migrationResult.raw,
    migrated: migrationResult.migrated,
    values: Object.fromEntries(
      Object.entries(parsed).map(([key, value]) => [key, decodeEnvValue(unquote(value.trim()))])
    )
  };
}

function buildEnvLines(values) {
  const extraKeys = Object.keys(values)
    .filter((key) => !KNOWN_KEY_ORDER.includes(key))
    .sort((left, right) => left.localeCompare(right));
  const orderedKeys = [...KNOWN_KEY_ORDER, ...extraKeys];

  return orderedKeys.map((key) => {
    const rawValue = values[key] ?? '';
    const encodedValue =
      key === 'BOT_PERSONA' || key === 'BOT_SYSTEM_PROMPT' ? encodeEnvValue(rawValue) : rawValue;
    return `${key}=${encodedValue}`;
  });
}

export async function readEnvFileValues({ cwd = process.cwd() } = {}) {
  const envPath = await ensureEnvFile(cwd);
  const raw = await fs.readFile(envPath, 'utf8');
  const parsedResult = parseEnvValues(raw);

  if (parsedResult.migrated) {
    return withEnvFileLock(envPath, async () => {
      const lockedRaw = await fs.readFile(envPath, 'utf8');
      const lockedParsedResult = parseEnvValues(lockedRaw);

      if (lockedParsedResult.migrated) {
        await writeTextFileAtomically(envPath, lockedParsedResult.raw);
      }

      return {
        envPath,
        values: lockedParsedResult.values
      };
    });
  }

  return {
    envPath,
    values: parsedResult.values
  };
}

export async function updateEnvFileValues({
  cwd = process.cwd(),
  mapValues
} = {}) {
  const envPath = await ensureEnvFile(cwd);

  return withEnvFileLock(envPath, async () => {
    const raw = await fs.readFile(envPath, 'utf8');
    const parsedResult = parseEnvValues(raw);
    const nextValues = await mapValues(parsedResult.values);

    if (!nextValues || typeof nextValues !== 'object' || Array.isArray(nextValues)) {
      throw new Error('updateEnvFileValues requires mapValues to return an object.');
    }

    const lines = buildEnvLines(nextValues);
    await writeTextFileAtomically(envPath, `${lines.join('\n')}\n`);

    return {
      envPath,
      previousValues: parsedResult.values,
      nextValues
    };
  });
}
