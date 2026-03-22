import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';

import { hasReplyReferences } from '../domain/inbound-message.mjs';
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

  if (hasReplyReferences(message)) {
    return true;
  }

  const normalizedText = typeof userText === 'string' ? userText.trim() : '';
  if (!normalizedText) {
    return false;
  }

  return IMAGE_REFERENCE_KEYWORDS.some((keyword) => normalizedText.includes(keyword));
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

function buildImageDataUrlFromChunks(packets = []) {
  const chunkPackets = packets
    .map((packet) => packet?.data)
    .filter(
      (data) =>
        data?.type === 'stream' &&
        data?.data_type === 'file_chunk' &&
        typeof data.data === 'string'
    )
    .sort((left, right) => (left.index ?? 0) - (right.index ?? 0));

  if (chunkPackets.length === 0) {
    return '';
  }

  const base64 = chunkPackets.map((chunk) => chunk.data).join('');
  return `data:image/jpeg;base64,${base64}`;
}

async function sendReplySegments({ groupId, text, maxOutputChars, napcat, logger }) {
  const segments = splitText(text, maxOutputChars);

  if (segments.length === 0) {
    return;
  }

  for (const segment of segments) {
    try {
      await napcat.sendGroupMsg(groupId, segment);
      logger.info(`[napcat] Group message sent: group_id=${groupId}, length=${segment.length}`);
    } catch (error) {
      logger.error(`[napcat] Group message failed: group_id=${groupId}, error=${formatError(error)}`);
      break;
    }
  }
}

async function resolveImageInputs(imageInputs, napcat) {
  if (!Array.isArray(imageInputs) || imageInputs.length === 0) {
    return [];
  }

  const resolvedInputs = [];

  for (const imageInput of imageInputs) {
    if (imageInput?.fileId) {
      const streamResult = await napcat.sendStreamAction('download_file_image_stream', {
        file: imageInput.fileId
      });
      const dataUrl = buildImageDataUrlFromChunks(streamResult.packets);

      if (!dataUrl) {
        throw new Error(`Failed to rebuild image from NapCat stream: ${imageInput.fileId}`);
      }

      resolvedInputs.push({
        ...imageInput,
        imageUrl: dataUrl
      });
      continue;
    }

    if (imageInput?.imageUrl) {
      resolvedInputs.push(imageInput);
    }
  }

  return resolvedInputs;
}

async function resolveRepliedImageInputs({ message, napcat, normalizeIncomingEvent, logger }) {
  const replyMessageIds = message.replyMessageIds;

  for (const replyMessageId of replyMessageIds) {
    try {
      const response = await napcat.sendAction('get_msg', {
        message_id: replyMessageId
      });
      const repliedMessage = normalizeIncomingEvent(response?.data);
      const repliedImageInputs = repliedMessage?.imageInputs ?? [];

      if (repliedImageInputs.length > 0) {
        logger.info(
          `[message] Reply image hit: reply_message_id=${replyMessageId}, images=${repliedImageInputs.length}`
        );
        return repliedImageInputs;
      }
    } catch (error) {
      logger.error(
        `[message] Failed to load replied message: message_id=${replyMessageId}, error=${formatError(error)}`
      );
    }
  }

  return [];
}

export async function handleIncomingMessage({
  message,
  sessionStore,
  llmRouter,
  napcat,
  logger,
  normalizeIncomingEvent,
  prepareImageInputs,
  imageCacheDir,
  maxOutputChars
}) {
  const groupId = message?.groupId;
  const userId = message?.userId;

  if (groupId === null || groupId === undefined || userId === null || userId === undefined) {
    return;
  }

  try {
    const fallbackReplyImageInputs =
      message.imageInputs.length > 0
        ? []
        : await resolveRepliedImageInputs({
            message,
            napcat,
            normalizeIncomingEvent,
            logger
          });
    const mergedImageInputs =
      message.imageInputs.length > 0 ? message.imageInputs : fallbackReplyImageInputs;
    const effectiveRouteInfo = llmRouter.resolveRoute({
      userText: message.text,
      imageInputs: mergedImageInputs
    });
    const conversationKey = `${groupId}:${userId}`;
    const userText = effectiveRouteInfo.userText;

    logger.info(
      `[message] Triggered: trigger=${message.trigger}, route=${effectiveRouteInfo.route}, api=${effectiveRouteInfo.apiStyle}, reason=${effectiveRouteInfo.reason}, images=${effectiveRouteInfo.imageCount}, group_id=${groupId}, user_id=${userId}, text="${summarizeText(userText, 80)}"`
    );

    const conversationState = sessionStore.getConversation(conversationKey);
    const resolvedImageInputs = await resolveImageInputs(mergedImageInputs, napcat);
    const carryoverImageInputs =
      resolvedImageInputs.length === 0 && shouldReuseLastImages(userText, message, conversationState)
        ? conversationState.shared.lastImageRefs.map((imageRef) => ({
            imageUrl: imageRef,
            source: 'session',
            index: 0
          }))
        : [];
    const preparedImageInputs = await prepareImageInputs(
      resolvedImageInputs.length > 0 ? resolvedImageInputs : carryoverImageInputs
    );
    const finalRouteInfo = llmRouter.resolveRoute({
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
    const reply = await llmRouter.generateReply({
      route: finalRouteInfo.route,
      userText: finalRouteInfo.userText,
      previousResponseId: routeState.previousResponseId,
      sharedMessages: routeState.messages,
      imageInputs: preparedImageInputs
    });

    logger.info(
      `[openai] Reply generated: route=${reply.route}, api=${reply.apiStyle}, model=${reply.model}, images=${preparedImageInputs.length}, group_id=${groupId}, user_id=${userId}, response_id=${reply.responseId || 'none'}`
    );

    const cachedImageRefs =
      preparedImageInputs.length > 0
        ? await cachePreparedImageInputs(preparedImageInputs, imageCacheDir)
        : conversationState.shared.lastImageRefs;

    await sessionStore.setConversation(conversationKey, {
      ...conversationState,
      routes: {
        ...conversationState.routes,
        [finalRouteInfo.route]: {
          previousResponseId:
            reply.sessionUpdate.previousResponseId ?? routeState.previousResponseId ?? null,
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
      groupId,
      text: replyText,
      maxOutputChars,
      napcat,
      logger
    });
  } catch (error) {
    logger.error(
      `[openai] Reply failed: group_id=${groupId}, user_id=${userId}, error=${formatError(error)}`
    );

    try {
      await napcat.sendGroupMsg(groupId, FAILED_REPLY_TEXT);
      logger.info(
        `[napcat] Group message sent: group_id=${groupId}, length=${FAILED_REPLY_TEXT.length}`
      );
    } catch (sendError) {
      logger.error(
        `[napcat] Group message failed: group_id=${groupId}, error=${formatError(sendError)}`
      );
    }
  }
}
