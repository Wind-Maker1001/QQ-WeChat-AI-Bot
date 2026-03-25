import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';

import { assertChannelPort } from '../domain/channel-port.mjs';
import { buildConversationId } from '../domain/conversation-state.mjs';
import { formatError, splitText, summarizeText } from '../utils.mjs';

const IMAGE_REFERENCE_KEYWORDS = [
  '\u56fe\u7247',
  '\u8fd9\u5f20',
  '\u8fd9\u56fe',
  '\u753b\u9762',
  '\u7167\u7247',
  '\u4eba\u7269',
  '\u5de6\u624b',
  '\u53f3\u624b',
  '\u5de6\u8fb9',
  '\u53f3\u8fb9',
  '\u4e0a\u9762',
  '\u4e0b\u9762',
  '\u624b\u91cc',
  '\u62ff\u7740',
  '\u8868\u60c5',
  '\u80cc\u666f',
  '\u989c\u8272',
  '\u54ea\u4e2a',
  '\u662f\u8c01',
  '\u4ec0\u4e48'
];

const EMPTY_REPLY_TEXT = '\u8fd9\u6b21\u6ca1\u6709\u751f\u6210\u53ef\u53d1\u9001\u7684\u6587\u672c\u3002';
const FAILED_REPLY_TEXT = '\u5904\u7406\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u518d\u8bd5\u3002';
const FAILED_IMAGE_TEXT = '\u672a\u80fd\u8bfb\u53d6\u53ef\u7528\u56fe\u7247\u5185\u5bb9\u3002';
const MAX_SESSION_MESSAGES = 24;

function inferImageExtensionFromDataUrl(dataUrl) {
  if (typeof dataUrl !== 'string') {
    return '.jpg';
  }

  if (dataUrl.startsWith('data:image/png')) {
    return '.png';
  }

  if (dataUrl.startsWith('data:image/webp')) {
    return '.webp';
  }

  if (dataUrl.startsWith('data:image/gif')) {
    return '.gif';
  }

  return '.jpg';
}

function shouldReuseLastImages(userText, message, conversationState) {
  const lastImageRefs = conversationState?.shared?.lastImageRefs;

  if (!Array.isArray(lastImageRefs) || lastImageRefs.length === 0) {
    return false;
  }

  const normalizedText = typeof userText === 'string' ? userText.trim() : '';
  if (!normalizedText) {
    return false;
  }

  return IMAGE_REFERENCE_KEYWORDS.some((keyword) => normalizedText.includes(keyword));
}

async function filterExistingCachedImageRefs(imageRefs, logger, channelId, chatId, userId) {
  if (!Array.isArray(imageRefs) || imageRefs.length === 0) {
    return [];
  }

  const existingImageRefs = [];

  for (const imageRef of imageRefs) {
    if (typeof imageRef !== 'string' || !imageRef.trim()) {
      continue;
    }

    try {
      await fs.access(imageRef);
      existingImageRefs.push(imageRef);
    } catch {
      logger.info(
        `[message] Dropped stale cached image ref: channel=${channelId}, chat_id=${chatId}, user_id=${userId}, image_ref="${summarizeText(imageRef, 120)}"`
      );
    }
  }

  return existingImageRefs;
}

async function cachePreparedImageInputs(preparedImageInputs, imageCacheDir) {
  if (!Array.isArray(preparedImageInputs) || preparedImageInputs.length === 0) {
    return [];
  }

  await fs.mkdir(imageCacheDir, { recursive: true });
  const imageRefs = [];

  for (const imageInput of preparedImageInputs) {
    if (!imageInput || typeof imageInput !== 'object') {
      continue;
    }

    if (typeof imageInput.imageUrl !== 'string' || !imageInput.imageUrl.startsWith('data:image/')) {
      continue;
    }

    const separatorIndex = imageInput.imageUrl.indexOf(',');

    if (separatorIndex < 0) {
      continue;
    }

    const base64 = imageInput.imageUrl.slice(separatorIndex + 1);
    const extension = inferImageExtensionFromDataUrl(imageInput.imageUrl);
    const fileName = `${Date.now()}-${crypto.randomUUID()}${extension}`;
    const filePath = path.join(imageCacheDir, fileName);

    await fs.writeFile(filePath, Buffer.from(base64, 'base64'));
    imageRefs.push(filePath);
  }

  return imageRefs;
}

async function sendReplySegments({ chatId, text, maxOutputChars, channelPort, logger }) {
  const segments = splitText(text, maxOutputChars);

  if (segments.length === 0) {
    return;
  }

  for (const segment of segments) {
    try {
      await channelPort.sendText(chatId, segment);
      logger.info(
        `[channel] Text sent: channel=${channelPort.channelId}, chat_id=${chatId}, length=${segment.length}`
      );
    } catch (error) {
      logger.error(
        `[channel] Text send failed: channel=${channelPort.channelId}, chat_id=${chatId}, error=${formatError(error)}`
      );
      break;
    }
  }
}

function formatToolList(toolKinds) {
  return Array.isArray(toolKinds) && toolKinds.length > 0 ? toolKinds.join('+') : 'none';
}

function formatDecisionToolOverrides(routeInfo) {
  const toolKinds = [];

  if (routeInfo?.requestedEnableWebSearch === true) {
    toolKinds.push('web_search');
  }

  if (routeInfo?.requestedEnableCodeInterpreter === true) {
    toolKinds.push('code_interpreter');
  }

  return toolKinds.length > 0 ? toolKinds.join('+') : 'none';
}

function buildReplyTelemetry({
  channelId,
  chatId,
  userId,
  finalRouteInfo,
  preparedImageInputs,
  reply
}) {
  return {
    capturedAt: new Date().toISOString(),
    channelId,
    route: reply.route,
    routeReason: finalRouteInfo.reason,
    matchedPrefix: finalRouteInfo.matchedPrefix || '',
    model: reply.model,
    configuredApiStyle: reply.configuredApiStyle || reply.apiStyle || '',
    effectiveApiStyle: reply.effectiveApiStyle || reply.apiStyle || '',
    configuredReasoningEffort: reply.configuredReasoningEffort || '',
    effectiveReasoningEffort: reply.effectiveReasoningEffort || '',
    configuredTextVerbosity: reply.configuredTextVerbosity || '',
    effectiveTextVerbosity: reply.effectiveTextVerbosity || '',
    configuredTools: Array.isArray(reply.configuredTools) ? reply.configuredTools : [],
    effectiveTools: Array.isArray(reply.effectiveTools) ? reply.effectiveTools : [],
    imageCount: Array.isArray(preparedImageInputs) ? preparedImageInputs.length : 0,
    chatId: String(chatId),
    userId: String(userId),
    responseId: reply.responseId || ''
  };
}

function buildFailureTelemetry({
  channelId,
  chatId,
  userId,
  effectiveRouteInfo,
  finalRouteInfo,
  error
}) {
  return {
    capturedAt: new Date().toISOString(),
    channelId,
    route: finalRouteInfo?.route || effectiveRouteInfo?.route || '',
    routeReason: finalRouteInfo?.reason || effectiveRouteInfo?.reason || '',
    matchedPrefix: finalRouteInfo?.matchedPrefix || effectiveRouteInfo?.matchedPrefix || '',
    chatId: chatId === null || chatId === undefined ? '' : String(chatId),
    userId: userId === null || userId === undefined ? '' : String(userId),
    error: formatError(error)
  };
}

function resolveNextPreviousResponseId(routeState, sessionUpdate) {
  if (sessionUpdate?.clearPreviousResponseId === true) {
    return null;
  }

  if (
    sessionUpdate &&
    Object.prototype.hasOwnProperty.call(sessionUpdate, 'previousResponseId')
  ) {
    return sessionUpdate.previousResponseId ?? null;
  }

  return routeState?.previousResponseId ?? null;
}

function shouldUseDeliberationPipeline(routeInfo, preparedImageInputs) {
  if (!routeInfo || (Array.isArray(preparedImageInputs) && preparedImageInputs.length > 0)) {
    return false;
  }

  return (
    routeInfo.requestedReasoningEffort === 'high' ||
    routeInfo.requestedEnableWebSearch === true ||
    routeInfo.requestedEnableCodeInterpreter === true
  );
}

function appendSessionMessages(sharedMessages, userText, assistantText) {
  const normalizedMessages = Array.isArray(sharedMessages)
    ? sharedMessages
        .filter((message) => message && typeof message === 'object')
        .map((message) => ({
          role: message.role === 'assistant' ? 'assistant' : 'user',
          content: typeof message.content === 'string' ? message.content.trim() : ''
        }))
        .filter((message) => message.content)
    : [];

  return [
    ...normalizedMessages,
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
  ].slice(-MAX_SESSION_MESSAGES);
}

function buildPlannerPrompt(userText) {
  return [
    '[INTERNAL_PLANNER]',
    '你正在做内部规划，不直接回答用户。',
    '请输出简短的内部思考提纲，包含：问题拆解、需要核实的点、回答结构。',
    '不要写开场白，不要直接给用户最终答案。',
    '',
    `用户问题：${userText}`
  ].join('\n');
}

function buildDraftPrompt(userText, planText) {
  return [
    '[INTERNAL_DRAFT]',
    '你现在基于内部规划生成给用户的正式答复。',
    '要求：直接回答、结构清晰、尽量准确，不要暴露“内部规划”这个过程。',
    '',
    `用户问题：${userText}`,
    '',
    `内部规划：${planText || '无'}`
  ].join('\n');
}

function buildRewritePrompt(userText, planText, draftText) {
  return [
    '[INTERNAL_REWRITE]',
    '你现在做最终质检和改写。',
    '请检查草稿答案是否存在遗漏、模糊、废话、逻辑跳步或事实风险，然后直接输出改写后的最终答案。',
    '不要解释修改过程，不要输出质检项。',
    '',
    `用户问题：${userText}`,
    '',
    `内部规划：${planText || '无'}`,
    '',
    `草稿答案：${draftText || '无'}`
  ].join('\n');
}

async function runDeliberationPipeline({
  llmRouter,
  finalRouteInfo,
  routeState,
  userText,
  preparedImageInputs,
  logger
}) {
  let planText = '';

  try {
    const plannerReply = await llmRouter.generateReply({
      route: finalRouteInfo.route,
      userText: buildPlannerPrompt(userText),
      previousResponseId: null,
      sharedMessages: routeState.messages,
      imageInputs: [],
      reasoningEffortOverride: 'high',
      textVerbosityOverride: 'low',
      enableWebSearchOverride: false,
      enableCodeInterpreterOverride: false,
      storeOverride: false
    });

    planText = plannerReply.text || '';
    logger.info(
      `[openai] Planner generated: route=${plannerReply.route}, model=${plannerReply.model}, length=${planText.length}`
    );
  } catch (error) {
    logger.error(`[openai] Planner failed, fallback to direct drafting: ${formatError(error)}`);
  }

  const draftReply = await llmRouter.generateReply({
    route: finalRouteInfo.route,
    userText: buildDraftPrompt(userText, planText),
    previousResponseId: null,
    sharedMessages: routeState.messages,
    imageInputs: preparedImageInputs,
    reasoningEffortOverride: finalRouteInfo.requestedReasoningEffort || 'high',
    textVerbosityOverride: finalRouteInfo.requestedTextVerbosity,
    enableWebSearchOverride: finalRouteInfo.requestedEnableWebSearch,
    enableCodeInterpreterOverride: finalRouteInfo.requestedEnableCodeInterpreter,
    storeOverride: false
  });
  const draftText = draftReply.text || '';

  try {
    const rewriteReply = await llmRouter.generateReply({
      route: finalRouteInfo.route,
      userText: buildRewritePrompt(userText, planText, draftText),
      previousResponseId: null,
      sharedMessages: routeState.messages,
      imageInputs: [],
      reasoningEffortOverride: 'high',
      textVerbosityOverride: finalRouteInfo.requestedTextVerbosity || 'medium',
      enableWebSearchOverride: finalRouteInfo.requestedEnableWebSearch,
      enableCodeInterpreterOverride: finalRouteInfo.requestedEnableCodeInterpreter,
      storeOverride: false
    });
    const rewrittenText = rewriteReply.text || draftText;

    return {
      ...rewriteReply,
      text: rewrittenText,
      sessionUpdate: {
        clearPreviousResponseId: true,
        previousResponseId: null,
        sharedMessages: appendSessionMessages(routeState.messages, userText, rewrittenText)
      }
    };
  } catch (error) {
    logger.error(`[openai] Final rewrite failed, fallback to draft answer: ${formatError(error)}`);
    return {
      ...draftReply,
      text: draftText,
      sessionUpdate: {
        clearPreviousResponseId: true,
        previousResponseId: null,
        sharedMessages: appendSessionMessages(routeState.messages, userText, draftText)
      }
    };
  }
}

async function resolveImageInputs(imageRefs, channelPort) {
  if (!Array.isArray(imageRefs) || imageRefs.length === 0) {
    return [];
  }

  const resolvedInputs = [];

  for (const imageRef of imageRefs) {
    const resolvedImage = await channelPort.readImage(imageRef);

    if (!resolvedImage || typeof resolvedImage.imageUrl !== 'string' || !resolvedImage.imageUrl) {
      throw new Error(`Failed to resolve image for channel ${channelPort.channelId}.`);
    }

    resolvedInputs.push(resolvedImage);
  }

  return resolvedInputs;
}

async function resolveRepliedImageRefs({ message, channelPort, logger }) {
  const replyToMessageIds = message.replyToMessageIds;

  for (const replyMessageId of replyToMessageIds) {
    try {
      const repliedMessage = await channelPort.getMessage(replyMessageId);
      const repliedImageRefs = repliedMessage?.imageRefs ?? [];

      if (repliedImageRefs.length > 0) {
        logger.info(
          `[message] Reply image hit: channel=${channelPort.channelId}, reply_message_id=${replyMessageId}, images=${repliedImageRefs.length}`
        );
        return repliedImageRefs;
      }
    } catch (error) {
      logger.error(
        `[message] Failed to load replied message: channel=${channelPort.channelId}, message_id=${replyMessageId}, error=${formatError(error)}`
      );
    }
  }

  return [];
}

export async function orchestrateIncomingMessage({
  message,
  sessionStore,
  llmRouter,
  channelPort,
  logger,
  prepareImageInputs,
  imageCacheDir,
  maxOutputChars,
  onReplyTelemetry,
  onReplyFailureTelemetry
}) {
  assertChannelPort(channelPort);

  const chatId = message?.chatId;
  const userId = message?.userId;
  let effectiveRouteInfo = null;
  let finalRouteInfo = null;

  if (chatId === null || chatId === undefined || userId === null || userId === undefined) {
    return;
  }

  try {
    const fallbackReplyImageRefs =
      message.imageRefs.length > 0
        ? []
        : await resolveRepliedImageRefs({
            message,
            channelPort,
            logger
          });
    const mergedImageRefs =
      message.imageRefs.length > 0 ? message.imageRefs : fallbackReplyImageRefs;
    effectiveRouteInfo = llmRouter.resolveRoute({
      userText: message.text,
      imageInputs: mergedImageRefs
    });
    const conversationKey = buildConversationId({
      channelId: channelPort.channelId,
      chatId,
      userId
    });
    const userText = effectiveRouteInfo.userText;

    logger.info(
      `[message] Triggered: channel=${channelPort.channelId}, trigger=${message.trigger}, route=${effectiveRouteInfo.route}, api=${effectiveRouteInfo.apiStyle}, reason=${effectiveRouteInfo.reason}, requested_reasoning=${effectiveRouteInfo.requestedReasoningEffort || 'default'}, requested_verbosity=${effectiveRouteInfo.requestedTextVerbosity || 'default'}, requested_tools=${formatDecisionToolOverrides(effectiveRouteInfo)}, images=${effectiveRouteInfo.imageCount}, chat_id=${chatId}, user_id=${userId}, text="${summarizeText(userText, 80)}"`
    );

    const conversationState = sessionStore.getConversation(conversationKey);
    const resolvedImageInputs = await resolveImageInputs(mergedImageRefs, channelPort);
    const existingCarryoverImageRefs =
      resolvedImageInputs.length === 0 && shouldReuseLastImages(userText, message, conversationState)
        ? await filterExistingCachedImageRefs(
            conversationState.shared.lastImageRefs,
            logger,
            channelPort.channelId,
            chatId,
            userId
          )
        : [];
    const carryoverImageInputs = existingCarryoverImageRefs.map((imageRef) => ({
      imageUrl: imageRef,
      source: 'session',
      index: 0,
      trustedLocalPath: true
    }));
    const preparedImageInputs = await prepareImageInputs(
      resolvedImageInputs.length > 0 ? resolvedImageInputs : carryoverImageInputs
    );
    finalRouteInfo = llmRouter.resolveRoute({
      userText,
      imageInputs: preparedImageInputs
    });

    if (
      (effectiveRouteInfo.imageCount > 0 || carryoverImageInputs.length > 0) &&
      preparedImageInputs.length === 0
    ) {
      throw new Error(FAILED_IMAGE_TEXT);
    }

    const routeState = conversationState.routes[finalRouteInfo.route];
    const reply = shouldUseDeliberationPipeline(finalRouteInfo, preparedImageInputs)
      ? await runDeliberationPipeline({
          llmRouter,
          finalRouteInfo,
          routeState,
          userText: finalRouteInfo.userText,
          preparedImageInputs,
          logger
        })
      : await llmRouter.generateReply({
          route: finalRouteInfo.route,
          userText: finalRouteInfo.userText,
          previousResponseId: routeState.previousResponseId,
          sharedMessages: routeState.messages,
          imageInputs: preparedImageInputs,
          reasoningEffortOverride: finalRouteInfo.requestedReasoningEffort,
          textVerbosityOverride: finalRouteInfo.requestedTextVerbosity,
          enableWebSearchOverride: finalRouteInfo.requestedEnableWebSearch,
          enableCodeInterpreterOverride: finalRouteInfo.requestedEnableCodeInterpreter
        });

    logger.info(
      `[openai] Reply generated: channel=${channelPort.channelId}, route=${reply.route}, configured_api=${reply.configuredApiStyle || reply.apiStyle}, effective_api=${reply.effectiveApiStyle || reply.apiStyle}, model=${reply.model}, configured_reasoning=${reply.configuredReasoningEffort || 'none'}, effective_reasoning=${reply.effectiveReasoningEffort || 'none'}, configured_verbosity=${reply.configuredTextVerbosity || 'none'}, effective_verbosity=${reply.effectiveTextVerbosity || 'none'}, configured_tools=${formatToolList(reply.configuredTools)}, effective_tools=${formatToolList(reply.effectiveTools)}, images=${preparedImageInputs.length}, chat_id=${chatId}, user_id=${userId}, response_id=${reply.responseId || 'none'}`
    );

    if (typeof onReplyTelemetry === 'function') {
      onReplyTelemetry(
        buildReplyTelemetry({
          channelId: channelPort.channelId,
          chatId,
          userId,
          finalRouteInfo,
          preparedImageInputs,
          reply
        })
      );
    }

    const cachedImageRefs =
      preparedImageInputs.length > 0
        ? await cachePreparedImageInputs(preparedImageInputs, imageCacheDir)
        : conversationState.shared.lastImageRefs;

    await sessionStore.setConversation(conversationKey, {
      ...conversationState,
      routes: {
        ...conversationState.routes,
        [finalRouteInfo.route]: {
          previousResponseId: resolveNextPreviousResponseId(routeState, reply.sessionUpdate),
          messages: reply.sessionUpdate.sharedMessages ?? routeState.messages
        }
      },
      shared: {
        ...conversationState.shared,
        lastImageRefs: cachedImageRefs
      }
    });

    const replyText = reply.text || EMPTY_REPLY_TEXT;
    await sendReplySegments({
      chatId,
      text: replyText,
      maxOutputChars,
      channelPort,
      logger
    });
  } catch (error) {
    if (typeof onReplyFailureTelemetry === 'function') {
      onReplyFailureTelemetry(
        buildFailureTelemetry({
          channelId: channelPort.channelId,
          chatId,
          userId,
          effectiveRouteInfo,
          finalRouteInfo,
          error
        })
      );
    }

    logger.error(
      `[openai] Reply failed: channel=${channelPort.channelId}, chat_id=${chatId}, user_id=${userId}, error=${formatError(error)}`
    );

    try {
      await channelPort.sendText(chatId, FAILED_REPLY_TEXT);
      logger.info(
        `[channel] Text sent: channel=${channelPort.channelId}, chat_id=${chatId}, length=${FAILED_REPLY_TEXT.length}`
      );
    } catch (sendError) {
      logger.error(
        `[channel] Text send failed: channel=${channelPort.channelId}, chat_id=${chatId}, error=${formatError(sendError)}`
      );
    }
  }
}
