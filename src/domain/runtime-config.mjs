export class RuntimeConfigValidationError extends Error {
  constructor(message) {
    super(message);
    this.name = 'RuntimeConfigValidationError';
  }
}

function cloneRouteConfig(route = {}) {
  return Object.freeze({
    apiKey: typeof route.apiKey === 'string' ? route.apiKey : '',
    model: typeof route.model === 'string' ? route.model : '',
    baseURL: typeof route.baseURL === 'string' ? route.baseURL : '',
    apiStyle: typeof route.apiStyle === 'string' ? route.apiStyle : '',
    reasoningEffort: typeof route.reasoningEffort === 'string' ? route.reasoningEffort : '',
    textVerbosity: typeof route.textVerbosity === 'string' ? route.textVerbosity : '',
    enableWebSearch: route.enableWebSearch === true,
    enableCodeInterpreter: route.enableCodeInterpreter === true
  });
}

function cloneStringList(values) {
  if (!Array.isArray(values)) {
    return Object.freeze([]);
  }

  return Object.freeze(
    values.filter((item) => typeof item === 'string').map((item) => item.trim()).filter(Boolean)
  );
}

function validateWebSocketUrl(label, value, { allowEmpty = false } = {}) {
  const normalizedValue = typeof value === 'string' ? value.trim() : '';

  if (!normalizedValue) {
    if (allowEmpty) {
      return;
    }

    throw new RuntimeConfigValidationError(`${label} is required.`);
  }

  let parsedUrl;

  try {
    parsedUrl = new URL(normalizedValue);
  } catch {
    throw new RuntimeConfigValidationError(`${label} must be a valid ws:// or wss:// URL.`);
  }

  if (parsedUrl.protocol !== 'ws:' && parsedUrl.protocol !== 'wss:') {
    throw new RuntimeConfigValidationError(`${label} must use ws:// or wss://.`);
  }
}

export function createRuntimeConfig({
  openai = {},
  napcat = {},
  wechat = {},
  bot = {},
  access = {},
  runtime = {},
  paths = {}
} = {}) {
  return Object.freeze({
    openai: Object.freeze({
      defaultRoute: cloneRouteConfig(openai.defaultRoute),
      advancedRoute: cloneRouteConfig(openai.advancedRoute),
      advancedTriggerPrefixes: cloneStringList(openai.advancedTriggerPrefixes)
    }),
    napcat: Object.freeze({
      wsUrl: typeof napcat.wsUrl === 'string' ? napcat.wsUrl : '',
      token: typeof napcat.token === 'string' ? napcat.token : ''
    }),
    wechat: Object.freeze({
      bridgeUrl: typeof wechat.bridgeUrl === 'string' ? wechat.bridgeUrl : '',
      token: typeof wechat.token === 'string' ? wechat.token : '',
      botPrefix: typeof wechat.botPrefix === 'string' ? wechat.botPrefix : ''
    }),
    bot: Object.freeze({
      prefix: typeof bot.prefix === 'string' ? bot.prefix : '',
      persona: typeof bot.persona === 'string' ? bot.persona : '',
      maxOutputChars: Number.isInteger(bot.maxOutputChars) ? bot.maxOutputChars : 800
    }),
    access: Object.freeze({
      allowedChatIds: cloneStringList(access.allowedChatIds),
      allowedUserIds: cloneStringList(access.allowedUserIds)
    }),
    runtime: Object.freeze({
      reconnectDelayMs: Number.isInteger(runtime.reconnectDelayMs) ? runtime.reconnectDelayMs : 3000
    }),
    paths: Object.freeze({
      imageCacheDir: typeof paths.imageCacheDir === 'string' ? paths.imageCacheDir : ''
    })
  });
}

export function listMissingRequiredRuntimeConfig(config) {
  const missing = [];
  const defaultApiKey = config?.openai?.defaultRoute?.apiKey ?? '';
  const advancedApiKey = config?.openai?.advancedRoute?.apiKey ?? '';
  const napcatToken = config?.napcat?.token ?? '';

  if (!defaultApiKey && !advancedApiKey) {
    missing.push('OPENAI_API_KEY or OPENAI_DEFAULT_API_KEY');
  }

  if (!napcatToken) {
    missing.push('NAPCAT_TOKEN');
  }

  return missing;
}

export function validateRuntimeConfig(config, { validateWechatBridge = false } = {}) {
  const missing = listMissingRequiredRuntimeConfig(config);

  if (missing.length > 0) {
    throw new RuntimeConfigValidationError(`Missing runtime config: ${missing.join(', ')}`);
  }

  validateWebSocketUrl('NAPCAT_WS_URL', config?.napcat?.wsUrl);

  if (validateWechatBridge) {
    validateWebSocketUrl('WECHAT_BRIDGE_URL', config?.wechat?.bridgeUrl, {
      allowEmpty: true
    });
  }
}
