function buildControlConfigFromEnvValues(envValues, runtimeConfig) {
  const allowedChatIds = envValues.ALLOWED_CHAT_IDS ?? runtimeConfig.access.allowedChatIds.join(',');
  const botSystemPrompt = normalizeBotSystemPromptValue(
    envValues.BOT_SYSTEM_PROMPT,
    runtimeConfig.bot.systemPrompt ?? ''
  );

  return {
    openAiApiKey:
      envValues.OPENAI_ADVANCED_API_KEY ??
      envValues.OPENAI_API_KEY ??
      runtimeConfig.openai.advancedRoute.apiKey ??
      '',
    openAiDefaultApiKey:
      envValues.OPENAI_DEFAULT_API_KEY ?? runtimeConfig.openai.defaultRoute.apiKey ?? '',
    openAiDefaultModel:
      envValues.OPENAI_DEFAULT_MODEL ?? runtimeConfig.openai.defaultRoute.model ?? '',
    openAiModel:
      envValues.OPENAI_ADVANCED_MODEL ??
      envValues.OPENAI_MODEL ??
      runtimeConfig.openai.advancedRoute.model ??
      '',
    openAiBaseUrl:
      envValues.OPENAI_ADVANCED_BASE_URL ??
      envValues.OPENAI_BASE_URL ??
      runtimeConfig.openai.advancedRoute.baseURL ??
      '',
    openAiDefaultBaseUrl:
      envValues.OPENAI_DEFAULT_BASE_URL ?? runtimeConfig.openai.defaultRoute.baseURL ?? '',
    openAiDefaultReasoningEffort:
      envValues.OPENAI_DEFAULT_REASONING_EFFORT ??
      runtimeConfig.openai.defaultRoute.reasoningEffort ??
      '',
    openAiAdvancedReasoningEffort:
      envValues.OPENAI_ADVANCED_REASONING_EFFORT ??
      runtimeConfig.openai.advancedRoute.reasoningEffort ??
      '',
    openAiDefaultTextVerbosity:
      envValues.OPENAI_DEFAULT_TEXT_VERBOSITY ??
      runtimeConfig.openai.defaultRoute.textVerbosity ??
      '',
    openAiAdvancedTextVerbosity:
      envValues.OPENAI_ADVANCED_TEXT_VERBOSITY ??
      runtimeConfig.openai.advancedRoute.textVerbosity ??
      '',
    openAiDefaultEnableWebSearch:
      envValues.OPENAI_DEFAULT_ENABLE_WEB_SEARCH ??
      String(runtimeConfig.openai.defaultRoute.enableWebSearch === true),
    openAiAdvancedEnableWebSearch:
      envValues.OPENAI_ADVANCED_ENABLE_WEB_SEARCH ??
      String(runtimeConfig.openai.advancedRoute.enableWebSearch === true),
    openAiDefaultEnableCodeInterpreter:
      envValues.OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER ??
      String(runtimeConfig.openai.defaultRoute.enableCodeInterpreter === true),
    openAiAdvancedEnableCodeInterpreter:
      envValues.OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER ??
      String(runtimeConfig.openai.advancedRoute.enableCodeInterpreter === true),
    openAiAdvancedTriggerPrefixes:
      envValues.OPENAI_ADVANCED_TRIGGER_PREFIXES ??
      runtimeConfig.openai.advancedTriggerPrefixes.join(','),
    napCatWsUrl: envValues.NAPCAT_WS_URL ?? runtimeConfig.napcat.wsUrl ?? '',
    napCatToken: envValues.NAPCAT_TOKEN ?? runtimeConfig.napcat.token ?? '',
    wechatBridgeUrl: envValues.WECHAT_BRIDGE_URL ?? runtimeConfig.wechat.bridgeUrl ?? '',
    wechatBridgeToken: envValues.WECHAT_BRIDGE_TOKEN ?? runtimeConfig.wechat.token ?? '',
    wechatBotPrefix: envValues.WECHAT_BOT_PREFIX ?? runtimeConfig.wechat.botPrefix ?? '',
    botPrefix: envValues.BOT_PREFIX ?? runtimeConfig.bot.prefix ?? '',
    botSystemPrompt,
    botPersona: envValues.BOT_PERSONA ?? runtimeConfig.bot.persona ?? '',
    maxOutputChars:
      envValues.MAX_OUTPUT_CHARS ?? String(runtimeConfig.bot.maxOutputChars ?? 800),
    allowedChatIds,
    allowedUserIds: envValues.ALLOWED_USER_IDS ?? runtimeConfig.access.allowedUserIds.join(',')
  };
}

function normalizeBotSystemPromptValue(value, fallbackValue = '') {
  const normalizedValue = typeof value === 'string' ? value : '';

  return normalizedValue.trim() ? normalizedValue : fallbackValue;
}

function normalizeControlConfigInput(config, fallbackConfig) {
  const source = config && typeof config === 'object' ? config : {};
  const fallback = buildControlConfigFromEnvValues({}, fallbackConfig);

  return {
    openAiApiKey: String(source.openAiApiKey ?? fallback.openAiApiKey ?? '').trim(),
    openAiDefaultApiKey: String(
      source.openAiDefaultApiKey ?? fallback.openAiDefaultApiKey ?? ''
    ).trim(),
    openAiDefaultModel: String(
      source.openAiDefaultModel ?? fallback.openAiDefaultModel ?? ''
    ).trim(),
    openAiModel: String(source.openAiModel ?? fallback.openAiModel ?? '').trim(),
    openAiBaseUrl: String(source.openAiBaseUrl ?? fallback.openAiBaseUrl ?? '').trim(),
    openAiDefaultBaseUrl: String(
      source.openAiDefaultBaseUrl ?? fallback.openAiDefaultBaseUrl ?? ''
    ).trim(),
    openAiDefaultReasoningEffort: String(
      source.openAiDefaultReasoningEffort ?? fallback.openAiDefaultReasoningEffort ?? ''
    ).trim(),
    openAiAdvancedReasoningEffort: String(
      source.openAiAdvancedReasoningEffort ?? fallback.openAiAdvancedReasoningEffort ?? ''
    ).trim(),
    openAiDefaultTextVerbosity: String(
      source.openAiDefaultTextVerbosity ?? fallback.openAiDefaultTextVerbosity ?? ''
    ).trim(),
    openAiAdvancedTextVerbosity: String(
      source.openAiAdvancedTextVerbosity ?? fallback.openAiAdvancedTextVerbosity ?? ''
    ).trim(),
    openAiDefaultEnableWebSearch: String(
      source.openAiDefaultEnableWebSearch ?? fallback.openAiDefaultEnableWebSearch ?? 'false'
    ).trim(),
    openAiAdvancedEnableWebSearch: String(
      source.openAiAdvancedEnableWebSearch ?? fallback.openAiAdvancedEnableWebSearch ?? 'false'
    ).trim(),
    openAiDefaultEnableCodeInterpreter: String(
      source.openAiDefaultEnableCodeInterpreter ?? fallback.openAiDefaultEnableCodeInterpreter ?? 'false'
    ).trim(),
    openAiAdvancedEnableCodeInterpreter: String(
      source.openAiAdvancedEnableCodeInterpreter ?? fallback.openAiAdvancedEnableCodeInterpreter ?? 'false'
    ).trim(),
    openAiAdvancedTriggerPrefixes: String(
      source.openAiAdvancedTriggerPrefixes ?? fallback.openAiAdvancedTriggerPrefixes ?? ''
    ).trim(),
    napCatWsUrl: String(source.napCatWsUrl ?? fallback.napCatWsUrl ?? '').trim(),
    napCatToken: String(source.napCatToken ?? fallback.napCatToken ?? '').trim(),
    wechatBridgeUrl: String(source.wechatBridgeUrl ?? fallback.wechatBridgeUrl ?? '').trim(),
    wechatBridgeToken: String(source.wechatBridgeToken ?? fallback.wechatBridgeToken ?? '').trim(),
    wechatBotPrefix: String(source.wechatBotPrefix ?? fallback.wechatBotPrefix ?? '').trim(),
    botPrefix: String(source.botPrefix ?? fallback.botPrefix ?? '').trim(),
    botSystemPrompt: normalizeBotSystemPromptValue(
      source.botSystemPrompt,
      fallback.botSystemPrompt ?? ''
    ),
    botPersona: String(source.botPersona ?? fallback.botPersona ?? ''),
    maxOutputChars: String(source.maxOutputChars ?? fallback.maxOutputChars ?? '800').trim(),
    allowedChatIds: String(source.allowedChatIds ?? fallback.allowedChatIds ?? '').trim(),
    allowedUserIds: String(source.allowedUserIds ?? fallback.allowedUserIds ?? '').trim()
  };
}

function buildControlEnvValues({
  existingValues,
  runtimeConfig,
  config
}) {
  const normalizedConfig = normalizeControlConfigInput(config, runtimeConfig);
  const nextValues = {
    ...existingValues,
    OPENAI_API_KEY: normalizedConfig.openAiApiKey,
    OPENAI_DEFAULT_API_KEY: normalizedConfig.openAiDefaultApiKey,
    OPENAI_ADVANCED_API_KEY: normalizedConfig.openAiApiKey,
    OPENAI_MODEL: normalizedConfig.openAiModel,
    OPENAI_DEFAULT_MODEL: normalizedConfig.openAiDefaultModel,
    OPENAI_ADVANCED_MODEL: normalizedConfig.openAiModel,
    OPENAI_BASE_URL: normalizedConfig.openAiBaseUrl,
    OPENAI_DEFAULT_BASE_URL: normalizedConfig.openAiDefaultBaseUrl,
    OPENAI_ADVANCED_BASE_URL: normalizedConfig.openAiBaseUrl,
    OPENAI_DEFAULT_API_STYLE: existingValues.OPENAI_DEFAULT_API_STYLE ?? 'responses',
    OPENAI_ADVANCED_API_STYLE: existingValues.OPENAI_ADVANCED_API_STYLE ?? 'responses',
    OPENAI_DEFAULT_REASONING_EFFORT: normalizedConfig.openAiDefaultReasoningEffort,
    OPENAI_ADVANCED_REASONING_EFFORT: normalizedConfig.openAiAdvancedReasoningEffort,
    OPENAI_DEFAULT_TEXT_VERBOSITY: normalizedConfig.openAiDefaultTextVerbosity,
    OPENAI_ADVANCED_TEXT_VERBOSITY: normalizedConfig.openAiAdvancedTextVerbosity,
    OPENAI_DEFAULT_ENABLE_WEB_SEARCH: normalizedConfig.openAiDefaultEnableWebSearch,
    OPENAI_ADVANCED_ENABLE_WEB_SEARCH: normalizedConfig.openAiAdvancedEnableWebSearch,
    OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER: normalizedConfig.openAiDefaultEnableCodeInterpreter,
    OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER: normalizedConfig.openAiAdvancedEnableCodeInterpreter,
    OPENAI_ADVANCED_TRIGGER_PREFIXES: normalizedConfig.openAiAdvancedTriggerPrefixes,
    NAPCAT_WS_URL: normalizedConfig.napCatWsUrl,
    NAPCAT_TOKEN: normalizedConfig.napCatToken,
    WECHAT_BRIDGE_URL: normalizedConfig.wechatBridgeUrl,
    WECHAT_BRIDGE_TOKEN: normalizedConfig.wechatBridgeToken,
    WECHAT_BOT_PREFIX: normalizedConfig.wechatBotPrefix,
    BOT_PREFIX: normalizedConfig.botPrefix,
    BOT_SYSTEM_PROMPT: normalizedConfig.botSystemPrompt,
    BOT_PERSONA: normalizedConfig.botPersona,
    MAX_OUTPUT_CHARS: normalizedConfig.maxOutputChars,
    ALLOWED_CHAT_IDS: normalizedConfig.allowedChatIds,
    ALLOWED_USER_IDS: normalizedConfig.allowedUserIds
  };

  return {
    normalizedConfig,
    nextValues
  };
}

export {
  buildControlConfigFromEnvValues,
  buildControlEnvValues
};
