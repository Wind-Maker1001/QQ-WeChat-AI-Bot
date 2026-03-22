function cloneRouteConfig(route = {}) {
  return Object.freeze({
    apiKey: typeof route.apiKey === 'string' ? route.apiKey : '',
    model: typeof route.model === 'string' ? route.model : '',
    baseURL: typeof route.baseURL === 'string' ? route.baseURL : '',
    apiStyle: typeof route.apiStyle === 'string' ? route.apiStyle : ''
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

export function createRuntimeConfig({
  openai = {},
  napcat = {},
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
    bot: Object.freeze({
      prefix: typeof bot.prefix === 'string' ? bot.prefix : '',
      persona: typeof bot.persona === 'string' ? bot.persona : '',
      maxOutputChars: Number.isInteger(bot.maxOutputChars) ? bot.maxOutputChars : 800
    }),
    access: Object.freeze({
      allowedGroupIds: cloneStringList(access.allowedGroupIds),
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

export function validateRuntimeConfig(config) {
  const missing = listMissingRequiredRuntimeConfig(config);

  if (missing.length > 0) {
    throw new Error(`缺少环境变量: ${missing.join(', ')}`);
  }
}
