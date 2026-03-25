import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';

import {
  buildMessageTurnSpec,
  buildNextConversationState,
  buildTurnFailureTelemetry,
  buildTurnReplyTelemetry
} from './message-turn-spec.mjs';
import { runDeliberationPipeline } from './deliberation-executor.mjs';
import { assertChannelPort } from '../domain/channel-port.mjs';
import { buildConversationId } from '../domain/conversation-state.mjs';
import {
  formatRouteDecisionReason,
  getRouteDecisionRequestedCapabilities
} from '../domain/route-decision.mjs';
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
  const requestedCapabilities = getRouteDecisionRequestedCapabilities(routeInfo);
  const toolKinds = [];

  if (requestedCapabilities.enableWebSearch === true) {
    toolKinds.push('web_search');
  }

  if (requestedCapabilities.enableCodeInterpreter === true) {
    toolKinds.push('code_interpreter');
  }

  return toolKinds.length > 0 ? toolKinds.join('+') : 'none';
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
  let turnSpec = null;

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
    const requestedCapabilities = getRouteDecisionRequestedCapabilities(effectiveRouteInfo);

    logger.info(
      `[message] Triggered: channel=${channelPort.channelId}, trigger=${message.trigger}, route=${effectiveRouteInfo.route}, api=${effectiveRouteInfo.apiStyle}, reason=${formatRouteDecisionReason(effectiveRouteInfo)}, requested_reasoning=${requestedCapabilities.reasoningEffort || 'default'}, requested_verbosity=${requestedCapabilities.textVerbosity || 'default'}, requested_tools=${formatDecisionToolOverrides(effectiveRouteInfo)}, images=${effectiveRouteInfo.imageCount}, chat_id=${chatId}, user_id=${userId}, text="${summarizeText(userText, 80)}"`
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
    turnSpec = buildMessageTurnSpec({
      channelId: channelPort.channelId,
      chatId,
      userId,
      routeInfo: finalRouteInfo,
      routeState,
      preparedImageInputs
    });
    const reply = turnSpec.executionPlan.mode === 'deliberation'
      ? await runDeliberationPipeline({
          llmRouter,
          executionPlan: turnSpec.executionPlan,
          logger
        })
      : await llmRouter.generateReply(turnSpec.executionPlan.directRequest);

    logger.info(
      `[openai] Reply generated: channel=${channelPort.channelId}, route=${reply.route}, configured_api=${reply.configuredApiStyle || reply.apiStyle}, effective_api=${reply.effectiveApiStyle || reply.apiStyle}, model=${reply.model}, configured_reasoning=${reply.configuredReasoningEffort || 'none'}, effective_reasoning=${reply.effectiveReasoningEffort || 'none'}, configured_verbosity=${reply.configuredTextVerbosity || 'none'}, effective_verbosity=${reply.effectiveTextVerbosity || 'none'}, configured_tools=${formatToolList(reply.configuredTools)}, effective_tools=${formatToolList(reply.effectiveTools)}, images=${preparedImageInputs.length}, chat_id=${chatId}, user_id=${userId}, response_id=${reply.responseId || 'none'}`
    );

    if (typeof onReplyTelemetry === 'function') {
      onReplyTelemetry(buildTurnReplyTelemetry({ turnSpec, reply }));
    }

    const cachedImageRefs =
      preparedImageInputs.length > 0
        ? await cachePreparedImageInputs(preparedImageInputs, imageCacheDir)
        : conversationState.shared.lastImageRefs;

    await sessionStore.setConversation(
      conversationKey,
      buildNextConversationState({
        conversationState,
        turnSpec,
        reply,
        cachedImageRefs
      })
    );

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
        buildTurnFailureTelemetry({
          turnSpec,
          routeInfo: finalRouteInfo ?? effectiveRouteInfo,
          channelId: channelPort.channelId,
          chatId,
          userId,
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
