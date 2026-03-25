export function splitText(text, maxLen = 800) {
  const normalizedText = typeof text === 'string' ? text : String(text ?? '');
  const limit = Number.isInteger(maxLen) && maxLen > 0 ? maxLen : 800;

  if (!normalizedText) {
    return [];
  }

  const parts = [];

  for (let index = 0; index < normalizedText.length; index += limit) {
    parts.push(normalizedText.slice(index, index + limit));
  }

  return parts;
}

export function sleep(ms) {
  const delay = Number.isFinite(ms) && ms >= 0 ? ms : 0;
  return new Promise((resolve) => {
    setTimeout(resolve, delay);
  });
}

export function safeJsonParse(str, fallback = null) {
  try {
    return JSON.parse(str);
  } catch {
    return fallback;
  }
}

export function toUtf8String(data) {
  if (typeof data === 'string') {
    return data;
  }

  if (Buffer.isBuffer(data)) {
    return data.toString('utf8');
  }

  if (Array.isArray(data)) {
    return Buffer.concat(
      data.map((item) => (Buffer.isBuffer(item) ? item : Buffer.from(item)))
    ).toString('utf8');
  }

  if (data instanceof ArrayBuffer) {
    return Buffer.from(data).toString('utf8');
  }

  if (ArrayBuffer.isView(data)) {
    return Buffer.from(data.buffer, data.byteOffset, data.byteLength).toString('utf8');
  }

  return String(data ?? '');
}

export function summarizeText(text, maxLen = 60) {
  const normalizedText = typeof text === 'string' ? text : String(text ?? '');
  const summary = normalizedText.replace(/\s+/g, ' ').trim();

  if (!summary) {
    return '';
  }

  if (summary.length <= maxLen) {
    return summary;
  }

  return `${summary.slice(0, maxLen)}...`;
}

export function parsePositiveInt(value, fallback) {
  const parsed = Number.parseInt(value, 10);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

export function parseCsvList(value) {
  if (typeof value !== 'string') {
    return [];
  }

  return value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
}

export function parseBoolean(value, fallback = false) {
  if (typeof value === 'boolean') {
    return value;
  }

  if (typeof value !== 'string') {
    return fallback;
  }

  const normalized = value.trim().toLowerCase();

  if (['1', 'true', 'yes', 'on'].includes(normalized)) {
    return true;
  }

  if (['0', 'false', 'no', 'off'].includes(normalized)) {
    return false;
  }

  return fallback;
}

export function formatError(error) {
  if (error instanceof Error) {
    return error.message;
  }

  return String(error ?? 'Unknown error');
}

export async function withTimeout(taskFactory, timeoutMs, timeoutMessage) {
  let timeoutId = null;

  const timeoutPromise = new Promise((_, reject) => {
    timeoutId = setTimeout(() => {
      reject(new Error(timeoutMessage));
    }, timeoutMs);
  });

  try {
    return await Promise.race([taskFactory(), timeoutPromise]);
  } finally {
    if (timeoutId) {
      clearTimeout(timeoutId);
    }
  }
}
