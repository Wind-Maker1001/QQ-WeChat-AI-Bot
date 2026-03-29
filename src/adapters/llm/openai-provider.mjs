import fs from 'node:fs/promises';

import OpenAI from 'openai';

import {
  appendConversationMessages,
  createLlmReplyOutcome
} from '../../domain/llm-reply-outcome.mjs';
import { normalizeConversationMessages } from '../../domain/conversation-state.mjs';
import {
  API_STYLE_CHAT_COMPLETIONS,
  API_STYLE_RESPONSES,
  buildEnabledToolKinds,
  resolveEffectiveRequestPolicy,
  resolveRouteRequestPolicy
} from '../../domain/llm-request-policy.mjs';
import { withTimeout } from '../../utils.mjs';

export const DEFAULT_OPENAI_BASE_URL = 'https://api.openai.com/v1';
export const DEFAULT_DEFAULT_MODEL = 'gpt-5.4';
export const DEFAULT_ADVANCED_MODEL = 'gpt-5.4';

const MAX_SHARED_HISTORY_MESSAGES = 40;
const MAX_SHARED_CONTEXT_MESSAGES = 20;
const IMAGE_FETCH_TIMEOUT_MS = 15000;
const ADVANCED_IMAGE_REQUEST_TIMEOUT_MS = 20000;

export const DEFAULT_BOT_SYSTEM_PROMPT = [
  '\u4f60\u662f QQ \u7fa4\u52a9\u624b\uff0c\u540c\u65f6\u4e5f\u662f\u4e00\u4e2a\u5bf9\u8bdd\u578b AI \u52a9\u624b\u3002',
  '\u9ed8\u8ba4\u4f7f\u7528\u7b80\u4f53\u4e2d\u6587\u3002',
  '\u5148\u76f4\u63a5\u56de\u7b54\u7528\u6237\u7684\u95ee\u9898\uff0c\u518d\u6309\u9700\u8981\u8865\u5145\u5173\u952e\u7406\u7531\u3001\u6b65\u9aa4\u3001\u4f8b\u5b50\u6216\u98ce\u9669\u3002',
  '\u9047\u5230\u590d\u6742\u95ee\u9898\u65f6\uff0c\u5148\u62c6\u89e3\u95ee\u9898\uff0c\u518d\u7ed9\u51fa\u7ed3\u8bba\uff0c\u4e0d\u8981\u56e0\u4e3a\u201c\u7fa4\u52a9\u624b\u201d\u800c\u628a\u56de\u7b54\u538b\u5f97\u8fc7\u77ed\u3002',
  '\u4fdd\u6301\u51c6\u786e\u3001\u5177\u4f53\u3001\u6709\u5224\u65ad\u529b\uff1b\u4e0d\u786e\u5b9a\u65f6\u660e\u786e\u8bf4\u51fa\u4e0d\u786e\u5b9a\u70b9\u3002',
  '\u9664\u975e\u7528\u6237\u8981\u6c42\uff0c\u5426\u5219\u4e0d\u8981\u8bf4\u6559\uff0c\u4e0d\u8981\u5806\u780c\u5ba2\u5957\u8bdd\uff0c\u4e5f\u4e0d\u8981\u8f93\u51fa\u591a\u4f59\u514d\u8d23\u58f0\u660e\u3002'
].join('\n');

export const BOT_INSTRUCTIONS = DEFAULT_BOT_SYSTEM_PROMPT;

function normalizeBotSystemPrompt(botSystemPrompt = '') {
  const normalizedSystemPrompt =
    typeof botSystemPrompt === 'string' ? botSystemPrompt.replace(/\\n/g, '\n').trim() : '';

  return normalizedSystemPrompt || DEFAULT_BOT_SYSTEM_PROMPT;
}

function buildBotInstructions({ botSystemPrompt = '', botPersona = '' } = {}) {
  const normalizedSystemPrompt = normalizeBotSystemPrompt(botSystemPrompt);
  const normalizedPersona =
    typeof botPersona === 'string' ? botPersona.replace(/\\n/g, '\n').trim() : '';

  if (!normalizedPersona) {
    return normalizedSystemPrompt;
  }

  return `${normalizedSystemPrompt}\n\n\u9644\u52a0\u4eba\u683c\u8bbe\u5b9a:\n${normalizedPersona}`;
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
  fallback,
  routePolicy
}) {
  const resolvedApiKey = apiKey || fallback?.apiKey || '';
  const resolvedBaseURL = normalizeBaseUrl(baseURL ?? fallback?.baseURL ?? null);
  const selectedRoutePolicy =
    routePolicy ??
    resolveRouteRequestPolicy({
      routeName,
      model,
      baseURL: resolvedBaseURL || baseURL || fallback?.baseURL || '',
      apiStyle,
      reasoningEffort,
      textVerbosity,
      enableWebSearch,
      enableCodeInterpreter,
      fallback
    });

  if (typeof resolvedApiKey !== 'string' || !resolvedApiKey) {
    throw new Error(`${routeName} route requires an API key.`);
  }

  if (typeof selectedRoutePolicy.model !== 'string' || !selectedRoutePolicy.model) {
    throw new Error(`${routeName} route requires a model name.`);
  }

  const clientOptions = {
    apiKey: resolvedApiKey
  };

  if (resolvedBaseURL) {
    clientOptions.baseURL = resolvedBaseURL;
  }

  return {
    ...selectedRoutePolicy,
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

function formatSharedContext(sharedMessages) {
  const recentMessages = normalizeConversationMessages(sharedMessages).slice(-MAX_SHARED_CONTEXT_MESSAGES);

  if (recentMessages.length === 0) {
    return '';
  }

  const transcript = recentMessages
    .map((message) => `${message.role === 'assistant' ? '\u52a9\u624b' : '\u7528\u6237'}: ${message.content}`)
    .join('\n');

  return `\u4ee5\u4e0b\u662f\u6700\u8fd1\u5bf9\u8bdd\u4e0a\u4e0b\u6587\uff0c\u8bf7\u4f18\u5148\u4fdd\u6301\u4e0a\u4e0b\u6587\u8fde\u7eed\u6027\uff0c\u5728\u56de\u7b54\u5f53\u524d\u95ee\u9898\u65f6\u53c2\u8003\uff1a\n${transcript}`;
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
  instructions = DEFAULT_BOT_SYSTEM_PROMPT
) {
  const normalizedSharedMessages = normalizeConversationMessages(sharedMessages).slice(
    -MAX_SHARED_HISTORY_MESSAGES
  );

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
  routePolicy,
  botSystemPrompt = '',
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
    fallback,
    routePolicy
  });
  const instructions = buildBotInstructions({
    botSystemPrompt,
    botPersona
  });

  function buildReplyEnvelope(reply, effectiveRequest) {
    const effectiveApiStyle = effectiveRequest.apiStyle;

    return {
      ...reply,
      route: selectedClient.routeName,
      configuredModel: selectedClient.model,
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

    return createLlmReplyOutcome({
      text,
      responseId,
      conversationDelta: {
        previousResponseId: responseId,
        sharedMessages: appendConversationMessages(sharedMessages, userText, text)
      }
    });
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

    return createLlmReplyOutcome({
      text,
      responseId: null,
      conversationDelta: {
        previousResponseId: null,
        sharedMessages: appendConversationMessages(sharedMessages, userText, text)
      }
    });
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
    const effectiveSettings = resolveEffectiveRequestPolicy({
      routePolicy: selectedClient,
      reasoningEffortOverride,
      textVerbosityOverride,
      enableWebSearchOverride,
      enableCodeInterpreterOverride
    });
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
  normalizeBotSystemPrompt,
  buildBotInstructions,
  buildResponsesTools,
  buildEnabledToolKinds
});
