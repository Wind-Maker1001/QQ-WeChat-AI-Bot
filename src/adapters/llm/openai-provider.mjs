import fs from 'node:fs/promises';

import OpenAI from 'openai';

import { withTimeout } from '../../utils.mjs';

export const DEFAULT_OPENAI_BASE_URL = 'https://api.openai.com/v1';
export const DEFAULT_DEFAULT_MODEL = 'gpt-5.4';
export const DEFAULT_ADVANCED_MODEL = 'gpt-5.4';

const API_STYLE_RESPONSES = 'responses';
const API_STYLE_CHAT_COMPLETIONS = 'chat_completions';
const MAX_SHARED_HISTORY_MESSAGES = 24;
const MAX_SHARED_CONTEXT_MESSAGES = 10;
const IMAGE_FETCH_TIMEOUT_MS = 15000;
const ADVANCED_IMAGE_REQUEST_TIMEOUT_MS = 20000;

export const BOT_INSTRUCTIONS = [
  '\u4f60\u662f QQ \u7fa4\u52a9\u624b\u3002',
  '\u9ed8\u8ba4\u4f7f\u7528\u7b80\u4f53\u4e2d\u6587\u3002',
  '\u56de\u7b54\u76f4\u63a5\u3001\u51c6\u786e\u3001\u7b80\u6d01\u3002',
  '\u4e0d\u8981\u8bf4\u6559\u3002',
  '\u4e0d\u8981\u8f93\u51fa\u591a\u4f59\u514d\u8d23\u58f0\u660e\u3002',
  '\u4e0d\u786e\u5b9a\u65f6\u660e\u786e\u8bf4\u4e0d\u786e\u5b9a\u3002'
].join('\n');

function buildBotInstructions(botPersona = '') {
  const normalizedPersona =
    typeof botPersona === 'string' ? botPersona.replace(/\\n/g, '\n').trim() : '';

  if (!normalizedPersona) {
    return BOT_INSTRUCTIONS;
  }

  return `${BOT_INSTRUCTIONS}\n\n\u9644\u52a0\u4eba\u683c\u8bbe\u5b9a:\n${normalizedPersona}`;
}

function normalizeBaseUrl(baseURL) {
  if (typeof baseURL !== 'string') {
    return null;
  }

  const trimmed = baseURL.trim();

  if (!trimmed) {
    return null;
  }

  try {
    const parsedUrl = new URL(trimmed);
    return parsedUrl.toString().replace(/\/$/, '');
  } catch {
    throw new Error(`OPENAI_BASE_URL invalid: ${trimmed}`);
  }
}

function normalizeApiStyle(style, routeName, model, baseURL) {
  const normalizedStyle = typeof style === 'string' ? style.trim().toLowerCase() : '';

  if (normalizedStyle === 'responses' || normalizedStyle === 'response') {
    return API_STYLE_RESPONSES;
  }

  if (
    normalizedStyle === 'chat' ||
    normalizedStyle === 'chat_completions' ||
    normalizedStyle === 'chat-completions' ||
    normalizedStyle === 'chatcompletions'
  ) {
    return API_STYLE_CHAT_COMPLETIONS;
  }

  const normalizedModel = typeof model === 'string' ? model.trim().toLowerCase() : '';
  const normalizedBaseURL = typeof baseURL === 'string' ? baseURL.toLowerCase() : '';

  if (
    routeName === 'default' &&
    (normalizedModel.startsWith('deepseek-') || normalizedBaseURL.includes('api.deepseek.com'))
  ) {
    return API_STYLE_CHAT_COMPLETIONS;
  }

  return API_STYLE_RESPONSES;
}

function inferDefaultReasoningEffort(routeName, model, apiStyle) {
  if (apiStyle !== API_STYLE_RESPONSES) {
    return '';
  }

  const normalizedModel = typeof model === 'string' ? model.trim().toLowerCase() : '';

  if (!normalizedModel.startsWith('gpt-5')) {
    return '';
  }

  return routeName === 'advanced' ? 'high' : 'medium';
}

function normalizeReasoningEffort(value, routeName, model, apiStyle) {
  const normalizedValue = typeof value === 'string' ? value.trim().toLowerCase() : '';

  if (
    normalizedValue === 'none' ||
    normalizedValue === 'low' ||
    normalizedValue === 'medium' ||
    normalizedValue === 'high'
  ) {
    return normalizedValue;
  }

  return inferDefaultReasoningEffort(routeName, model, apiStyle);
}

function inferDefaultTextVerbosity(routeName, model, apiStyle) {
  if (apiStyle !== API_STYLE_RESPONSES) {
    return '';
  }

  const normalizedModel = typeof model === 'string' ? model.trim().toLowerCase() : '';

  if (!normalizedModel.startsWith('gpt-5')) {
    return '';
  }

  return routeName === 'advanced' ? 'high' : 'medium';
}

function normalizeTextVerbosity(value, routeName, model, apiStyle) {
  const normalizedValue = typeof value === 'string' ? value.trim().toLowerCase() : '';

  if (normalizedValue === 'low' || normalizedValue === 'medium' || normalizedValue === 'high') {
    return normalizedValue;
  }

  return inferDefaultTextVerbosity(routeName, model, apiStyle);
}

function normalizeBooleanFlag(value, fallback = false) {
  if (value === true || value === false) {
    return value;
  }

  if (typeof value !== 'string') {
    return fallback;
  }

  const normalizedValue = value.trim().toLowerCase();

  if (['1', 'true', 'yes', 'on'].includes(normalizedValue)) {
    return true;
  }

  if (['0', 'false', 'no', 'off'].includes(normalizedValue)) {
    return false;
  }

  return fallback;
}

function buildResponsesTools({ enableWebSearch = false, enableCodeInterpreter = false } = {}) {
  const tools = [];

  if (enableWebSearch) {
    tools.push({
      type: 'web_search'
    });
  }

  if (enableCodeInterpreter) {
    tools.push({
      type: 'code_interpreter',
      container: {
        type: 'auto'
      }
    });
  }

  return tools;
}

function buildEnabledToolKinds({ enableWebSearch = false, enableCodeInterpreter = false } = {}) {
  const toolKinds = [];

  if (enableWebSearch) {
    toolKinds.push('web_search');
  }

  if (enableCodeInterpreter) {
    toolKinds.push('code_interpreter');
  }

  return toolKinds;
}

function resolveEffectiveReasoningEffort(overrideValue, selectedClient) {
  if (typeof overrideValue !== 'string' || !overrideValue.trim()) {
    return selectedClient.reasoningEffort;
  }

  return (
    normalizeReasoningEffort(
      overrideValue.trim(),
      selectedClient.routeName,
      selectedClient.model,
      selectedClient.apiStyle
    ) || selectedClient.reasoningEffort
  );
}

function resolveEffectiveTextVerbosity(overrideValue, selectedClient) {
  if (typeof overrideValue !== 'string' || !overrideValue.trim()) {
    return selectedClient.textVerbosity;
  }

  return (
    normalizeTextVerbosity(
      overrideValue.trim(),
      selectedClient.routeName,
      selectedClient.model,
      selectedClient.apiStyle
    ) || selectedClient.textVerbosity
  );
}

function resolveEffectiveBooleanFlag(overrideValue, fallback) {
  return typeof overrideValue === 'boolean' ? overrideValue : fallback;
}

function normalizeRouteConfig({
  routeName,
  apiKey,
  model,
  baseURL,
  apiStyle,
  reasoningEffort,
  textVerbosity,
  enableWebSearch,
  enableCodeInterpreter,
  fallback
}) {
  const resolvedApiKey = apiKey || fallback?.apiKey || '';
  const resolvedModel = model || fallback?.model || '';
  const resolvedBaseURL = normalizeBaseUrl(baseURL ?? fallback?.baseURL ?? null);
  const resolvedApiStyle = normalizeApiStyle(
    apiStyle ?? fallback?.apiStyle ?? '',
    routeName,
    resolvedModel,
    resolvedBaseURL || ''
  );
  const resolvedReasoningEffort = normalizeReasoningEffort(
    reasoningEffort ?? fallback?.reasoningEffort ?? '',
    routeName,
    resolvedModel,
    resolvedApiStyle
  );
  const resolvedTextVerbosity = normalizeTextVerbosity(
    textVerbosity ?? fallback?.textVerbosity ?? '',
    routeName,
    resolvedModel,
    resolvedApiStyle
  );
  const resolvedEnableWebSearch = normalizeBooleanFlag(
    enableWebSearch ?? fallback?.enableWebSearch ?? false,
    false
  );
  const resolvedEnableCodeInterpreter = normalizeBooleanFlag(
    enableCodeInterpreter ?? fallback?.enableCodeInterpreter ?? false,
    false
  );

  if (typeof resolvedApiKey !== 'string' || !resolvedApiKey) {
    throw new Error(`${routeName} route requires an API key.`);
  }

  if (typeof resolvedModel !== 'string' || !resolvedModel) {
    throw new Error(`${routeName} route requires a model name.`);
  }

  const clientOptions = {
    apiKey: resolvedApiKey
  };

  if (resolvedBaseURL) {
    clientOptions.baseURL = resolvedBaseURL;
  }

  return {
    routeName,
    model: resolvedModel,
    apiStyle: resolvedApiStyle,
    reasoningEffort: resolvedReasoningEffort,
    textVerbosity: resolvedTextVerbosity,
    enableWebSearch: resolvedEnableWebSearch,
    enableCodeInterpreter: resolvedEnableCodeInterpreter,
    baseURL: resolvedBaseURL || DEFAULT_OPENAI_BASE_URL,
    client: new OpenAI(clientOptions)
  };
}

function normalizeMimeType(contentType, sourceUrl = '') {
  const normalizedContentType = typeof contentType === 'string' ? contentType.toLowerCase() : '';

  if (normalizedContentType.startsWith('image/')) {
    return normalizedContentType.split(';')[0];
  }

  const lowerSourceUrl = typeof sourceUrl === 'string' ? sourceUrl.toLowerCase() : '';

  if (lowerSourceUrl.endsWith('.png')) {
    return 'image/png';
  }

  if (lowerSourceUrl.endsWith('.webp')) {
    return 'image/webp';
  }

  if (lowerSourceUrl.endsWith('.gif')) {
    return 'image/gif';
  }

  return 'image/jpeg';
}

function looksLikeLocalPath(value) {
  if (typeof value !== 'string') {
    return false;
  }

  return /^[a-zA-Z]:\\/.test(value) || value.startsWith('\\\\') || value.startsWith('file:///');
}

function looksLikeRemoteHttpUrl(value) {
  return typeof value === 'string' && /^https?:\/\//i.test(value.trim());
}

function safeFileUrlToPath(fileUrl) {
  try {
    const url = new URL(fileUrl);
    const pathname = decodeURIComponent(url.pathname || '');
    return process.platform === 'win32' && pathname.startsWith('/')
      ? pathname.slice(1).replace(/\//g, '\\')
      : pathname;
  } catch {
    return fileUrl;
  }
}

function normalizeLocalPath(value) {
  if (typeof value !== 'string' || !value.trim()) {
    return '';
  }

  if (value.startsWith('file:///')) {
    return safeFileUrlToPath(value);
  }

  return value;
}

async function readLocalImageAsDataUrl(localPath) {
  const normalizedPath = normalizeLocalPath(localPath);
  const fileBuffer = await fs.readFile(normalizedPath);
  const mimeType = normalizeMimeType('', normalizedPath);
  return `data:${mimeType};base64,${fileBuffer.toString('base64')}`;
}

async function normalizeImageInput(imageInput) {
  if (!imageInput || typeof imageInput !== 'object') {
    return null;
  }

  if (typeof imageInput.imageUrl !== 'string' || !imageInput.imageUrl) {
    return null;
  }

  if (imageInput.imageUrl.startsWith('data:image/')) {
    return imageInput;
  }

  if (looksLikeRemoteHttpUrl(imageInput.imageUrl)) {
    throw new Error(
      `Remote image URLs are not allowed: ${imageInput.imageUrl}`
    );
  }

  const canReadLocalPath =
    looksLikeLocalPath(imageInput.imageUrl) && imageInput.trustedLocalPath === true;
  const resolvedImageUrl = canReadLocalPath
    ? await readLocalImageAsDataUrl(imageInput.imageUrl)
    : null;

  if (!resolvedImageUrl) {
    throw new Error(`Unsupported image reference: ${imageInput.imageUrl}`);
  }

  return {
    ...imageInput,
    imageUrl: resolvedImageUrl
  };
}

export async function prepareImageInputs(imageInputs = []) {
  if (!Array.isArray(imageInputs) || imageInputs.length === 0) {
    return [];
  }

  const preparedInputs = [];

  for (const imageInput of imageInputs) {
    const preparedInput = await Promise.race([
      normalizeImageInput(imageInput),
      new Promise((_, reject) => {
        setTimeout(() => {
          reject(
            new Error(`Image preparation timed out: ${imageInput?.imageUrl || imageInput?.fileId || 'unknown'}`)
          );
        }, IMAGE_FETCH_TIMEOUT_MS);
      })
    ]);

    if (preparedInput) {
      preparedInputs.push(preparedInput);
    }
  }

  return preparedInputs;
}

function normalizeSharedMessages(sharedMessages) {
  if (!Array.isArray(sharedMessages)) {
    return [];
  }

  return sharedMessages
    .filter((message) => message && typeof message === 'object')
    .map((message) => {
      const role = message.role === 'assistant' ? 'assistant' : 'user';
      const content = typeof message.content === 'string' ? message.content.trim() : '';

      if (!content) {
        return null;
      }

      return {
        role,
        content
      };
    })
    .filter(Boolean)
    .slice(-MAX_SHARED_HISTORY_MESSAGES);
}

function appendSharedMessages(sharedMessages, userText, assistantText) {
  const nextMessages = [
    ...normalizeSharedMessages(sharedMessages),
    {
      role: 'user',
      content: userText
    },
    ...(assistantText
      ? [
          {
            role: 'assistant',
            content: assistantText
          }
        ]
      : [])
  ];

  return normalizeSharedMessages(nextMessages).slice(-MAX_SHARED_HISTORY_MESSAGES);
}

function formatSharedContext(sharedMessages) {
  const recentMessages = normalizeSharedMessages(sharedMessages).slice(-MAX_SHARED_CONTEXT_MESSAGES);

  if (recentMessages.length === 0) {
    return '';
  }

  const transcript = recentMessages
    .map((message) => `${message.role === 'assistant' ? '\u52a9\u624b' : '\u7528\u6237'}: ${message.content}`)
    .join('\n');

  return `\u4ee5\u4e0b\u662f\u6700\u8fd1\u5bf9\u8bdd\u4e0a\u4e0b\u6587\uff0c\u8bf7\u56de\u7b54\u5f53\u524d\u95ee\u9898\u65f6\u53c2\u8003\uff1a\n${transcript}`;
}

function buildResponsesContent(userText, imageInputs, sharedMessages, previousResponseId) {
  const content = [];
  const sharedContextText =
    typeof previousResponseId === 'string' && previousResponseId
      ? ''
      : formatSharedContext(sharedMessages);

  if (sharedContextText) {
    content.push({
      type: 'input_text',
      text: sharedContextText
    });
  }

  if (typeof userText === 'string' && userText.trim()) {
    content.push({
      type: 'input_text',
      text: userText
    });
  }

  for (const imageInput of imageInputs) {
    if (!imageInput || typeof imageInput !== 'object') {
      continue;
    }

    if (typeof imageInput.imageUrl !== 'string' || !imageInput.imageUrl) {
      continue;
    }

    content.push({
      type: 'input_image',
      image_url: imageInput.imageUrl
    });
  }

  if (content.length === 0) {
    content.push({
      type: 'input_text',
      text: '\u8bf7\u7ee7\u7eed\u3002'
    });
  }

  return content;
}

function buildChatContent(userText, imageInputs, allowImages = false) {
  if (!allowImages || !Array.isArray(imageInputs) || imageInputs.length === 0) {
    return userText;
  }

  const content = [];

  if (typeof userText === 'string' && userText.trim()) {
    content.push({
      type: 'text',
      text: userText
    });
  }

  for (const imageInput of imageInputs) {
    if (!imageInput || typeof imageInput !== 'object') {
      continue;
    }

    if (typeof imageInput.imageUrl !== 'string' || !imageInput.imageUrl) {
      continue;
    }

    content.push({
      type: 'image_url',
      image_url: {
        url: imageInput.imageUrl
      }
    });
  }

  if (content.length === 0) {
    return '\u8bf7\u7ee7\u7eed\u3002';
  }

  return content;
}

function buildChatCompletionMessages(
  sharedMessages,
  userText,
  imageInputs,
  allowImages = false,
  instructions = BOT_INSTRUCTIONS
) {
  const normalizedSharedMessages = normalizeSharedMessages(sharedMessages);

  return [
    {
      role: 'system',
      content: instructions
    },
    ...normalizedSharedMessages,
    {
      role: 'user',
      content: buildChatContent(userText, imageInputs, allowImages)
    }
  ];
}

function extractChatCompletionText(completion) {
  const content = completion?.choices?.[0]?.message?.content;

  if (typeof content === 'string') {
    return content.trim();
  }

  if (Array.isArray(content)) {
    return content
      .map((item) => {
        if (typeof item === 'string') {
          return item;
        }

        if (item && typeof item === 'object' && typeof item.text === 'string') {
          return item.text;
        }

        return '';
      })
      .join('')
      .trim();
  }

  return '';
}

export function createOpenAIProvider({
  routeName,
  apiKey,
  model,
  baseURL,
  apiStyle,
  reasoningEffort,
  textVerbosity,
  enableWebSearch,
  enableCodeInterpreter,
  fallback,
  botPersona = ''
}) {
  const selectedClient = normalizeRouteConfig({
    routeName,
    apiKey,
    model,
    baseURL,
    apiStyle,
    reasoningEffort,
    textVerbosity,
    enableWebSearch,
    enableCodeInterpreter,
    fallback
  });
  const instructions = buildBotInstructions(botPersona);

  function buildReplyEnvelope(reply, effectiveRequest) {
    const effectiveApiStyle = effectiveRequest.apiStyle;

    return {
      ...reply,
      route: selectedClient.routeName,
      model: selectedClient.model,
      apiStyle:
        selectedClient.apiStyle === effectiveApiStyle
          ? selectedClient.apiStyle
          : `${selectedClient.apiStyle}->${effectiveApiStyle}`,
      configuredApiStyle: selectedClient.apiStyle,
      effectiveApiStyle,
      configuredReasoningEffort: selectedClient.reasoningEffort,
      effectiveReasoningEffort: effectiveRequest.reasoningEffort,
      configuredTextVerbosity: selectedClient.textVerbosity,
      effectiveTextVerbosity: effectiveRequest.textVerbosity,
      configuredEnableWebSearch: selectedClient.enableWebSearch,
      effectiveEnableWebSearch: effectiveRequest.enableWebSearch,
      configuredEnableCodeInterpreter: selectedClient.enableCodeInterpreter,
      effectiveEnableCodeInterpreter: effectiveRequest.enableCodeInterpreter,
      configuredTools: buildEnabledToolKinds({
        enableWebSearch: selectedClient.enableWebSearch,
        enableCodeInterpreter: selectedClient.enableCodeInterpreter
      }),
      effectiveTools: buildEnabledToolKinds({
        enableWebSearch: effectiveRequest.enableWebSearch,
        enableCodeInterpreter: effectiveRequest.enableCodeInterpreter
      }),
      baseURL: selectedClient.baseURL
    };
  }

  async function generateResponsesReply({
    userText,
    previousResponseId,
    imageInputs,
    sharedMessages,
    effectiveSettings,
    store = true
  }) {
    const request = {
      model: selectedClient.model,
      store,
      instructions,
      input: [
        {
          role: 'user',
          content: buildResponsesContent(userText, imageInputs, sharedMessages, previousResponseId)
        }
      ]
    };

    if (typeof previousResponseId === 'string' && previousResponseId) {
      request.previous_response_id = previousResponseId;
    }

    if (effectiveSettings.reasoningEffort) {
      request.reasoning = {
        effort: effectiveSettings.reasoningEffort
      };
    }

    if (effectiveSettings.textVerbosity) {
      request.text = {
        verbosity: effectiveSettings.textVerbosity
      };
    }

    const tools = buildResponsesTools({
      enableWebSearch: effectiveSettings.enableWebSearch,
      enableCodeInterpreter: effectiveSettings.enableCodeInterpreter
    });

    if (tools.length > 0) {
      request.tools = tools;
    }

    const response = await selectedClient.client.responses.create(request);
    const text = typeof response.output_text === 'string' ? response.output_text.trim() : '';
    const responseId = typeof response.id === 'string' && response.id ? response.id : null;

    return {
      text,
      responseId,
      sessionUpdate: {
        previousResponseId: responseId,
        sharedMessages: appendSharedMessages(sharedMessages, userText, text)
      }
    };
  }

  async function generateChatReply({ userText, sharedMessages, imageInputs, allowImages = false }) {
    if (!allowImages && Array.isArray(imageInputs) && imageInputs.length > 0) {
      throw new Error('Current chat-completions route does not support image inputs.');
    }

    const completion = await selectedClient.client.chat.completions.create({
      model: selectedClient.model,
      messages: buildChatCompletionMessages(
        sharedMessages,
        userText,
        imageInputs,
        allowImages,
        instructions
      ),
      stream: false
    });

    const text = extractChatCompletionText(completion);

    return {
      text,
      responseId: null,
      sessionUpdate: {
        previousResponseId: null,
        sharedMessages: appendSharedMessages(sharedMessages, userText, text)
      }
    };
  }

  async function generateReply({
    userText,
    previousResponseId = null,
    sharedMessages = [],
    imageInputs = [],
    reasoningEffortOverride,
    textVerbosityOverride,
    enableWebSearchOverride,
    enableCodeInterpreterOverride,
    storeOverride
  }) {
    const effectiveSettings = {
      reasoningEffort: resolveEffectiveReasoningEffort(reasoningEffortOverride, selectedClient),
      textVerbosity: resolveEffectiveTextVerbosity(textVerbosityOverride, selectedClient),
      enableWebSearch: resolveEffectiveBooleanFlag(
        enableWebSearchOverride,
        selectedClient.enableWebSearch
      ),
      enableCodeInterpreter: resolveEffectiveBooleanFlag(
        enableCodeInterpreterOverride,
        selectedClient.enableCodeInterpreter
      )
    };
    const effectiveStore = typeof storeOverride === 'boolean' ? storeOverride : true;
    let reply;

    if (selectedClient.apiStyle === API_STYLE_CHAT_COMPLETIONS) {
      reply = await generateChatReply({
        userText,
        sharedMessages,
        imageInputs,
        allowImages: imageInputs.length > 0
      });
      return buildReplyEnvelope(reply, {
        apiStyle: API_STYLE_CHAT_COMPLETIONS,
        reasoningEffort: '',
        textVerbosity: '',
        enableWebSearch: false,
        enableCodeInterpreter: false
      });
    } else if (imageInputs.length > 0) {
      try {
      reply = await withTimeout(
          () =>
            generateResponsesReply({
              userText,
              previousResponseId,
              imageInputs,
              sharedMessages,
              effectiveSettings,
              store: effectiveStore
            }),
          ADVANCED_IMAGE_REQUEST_TIMEOUT_MS,
          `Image request timed out: ${selectedClient.routeName}/${selectedClient.model}`
        );
      } catch {
        reply = await generateChatReply({
          userText,
          sharedMessages,
          imageInputs,
          allowImages: true
        });
        return buildReplyEnvelope(reply, {
          apiStyle: API_STYLE_CHAT_COMPLETIONS,
          reasoningEffort: '',
          textVerbosity: '',
          enableWebSearch: false,
          enableCodeInterpreter: false
        });
      }
    } else {
      reply = await generateResponsesReply({
        userText,
        previousResponseId,
        imageInputs,
        sharedMessages,
        effectiveSettings,
        store: effectiveStore
      });
    }

    return buildReplyEnvelope(reply, {
      apiStyle: API_STYLE_RESPONSES,
      reasoningEffort: effectiveSettings.reasoningEffort,
      textVerbosity: effectiveSettings.textVerbosity,
      enableWebSearch: effectiveSettings.enableWebSearch,
      enableCodeInterpreter: effectiveSettings.enableCodeInterpreter
    });
  }

  return {
    routeName: selectedClient.routeName,
    model: selectedClient.model,
    apiStyle: selectedClient.apiStyle,
    reasoningEffort: selectedClient.reasoningEffort,
    textVerbosity: selectedClient.textVerbosity,
    enableWebSearch: selectedClient.enableWebSearch,
    enableCodeInterpreter: selectedClient.enableCodeInterpreter,
    baseURL: selectedClient.baseURL,
    generateReply
  };
}

export const __test__ = Object.freeze({
  buildResponsesTools,
  buildEnabledToolKinds
});
