import WebSocket from 'ws';

import { formatError, safeJsonParse, toUtf8String } from './utils.mjs';

function normalizeId(value) {
  if (value === undefined || value === null) {
    return '';
  }

  return String(value).trim();
}

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function hasSelfMentionInSegments(message, selfId) {
  if (!Array.isArray(message) || !selfId) {
    return false;
  }

  return message.some((segment) => {
    if (!segment || typeof segment !== 'object') {
      return false;
    }

    if (segment.type !== 'at') {
      return false;
    }

    const qq = normalizeId(segment.data?.qq ?? segment.qq);
    return qq === selfId;
  });
}

function hasSelfMentionInRawText(rawText, selfId) {
  if (typeof rawText !== 'string' || !selfId) {
    return false;
  }

  const selfMentionPattern = new RegExp(`\\[CQ:at,qq=${escapeRegExp(selfId)}(?:,[^\\]]*)?\\]`);
  return selfMentionPattern.test(rawText);
}

function stripSelfMention(rawText, selfId) {
  if (typeof rawText !== 'string' || !selfId) {
    return typeof rawText === 'string' ? rawText : '';
  }

  const selfMentionPattern = new RegExp(`\\[CQ:at,qq=${escapeRegExp(selfId)}(?:,[^\\]]*)?\\]`, 'g');
  return rawText.replace(selfMentionPattern, ' ');
}

function stripImageCodes(rawText) {
  if (typeof rawText !== 'string') {
    return '';
  }

  return rawText.replace(/\[CQ:image,[^\]]+\]/g, ' ');
}

function normalizeImageUrl(value) {
  if (typeof value !== 'string') {
    return '';
  }

  const trimmed = value.trim();

  if (
    trimmed.startsWith('http://') ||
    trimmed.startsWith('https://') ||
    trimmed.startsWith('data:image/')
  ) {
    return trimmed;
  }

  return '';
}

function normalizeFileId(value) {
  if (typeof value !== 'string') {
    return '';
  }

  const trimmed = value.trim();
  return trimmed || '';
}

function safeDecodeURIComponent(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function extractPlainTextFromSegments(message) {
  if (!Array.isArray(message)) {
    return '';
  }

  return message
    .map((segment) => {
      if (typeof segment === 'string') {
        return segment;
      }

      if (!segment || typeof segment !== 'object') {
        return '';
      }

      if (segment.type === 'text') {
        return typeof segment.data?.text === 'string' ? segment.data.text : '';
      }

      if (segment.type === 'at') {
        const name = typeof segment.data?.name === 'string' ? segment.data.name : '';
        return name ? `@${name} ` : '';
      }

      return '';
    })
    .join('');
}

function extractReplyIdsFromSegments(message) {
  if (!Array.isArray(message)) {
    return [];
  }

  return message
    .filter((segment) => segment && typeof segment === 'object' && segment.type === 'reply')
    .map((segment) => normalizeFileId(segment.data?.id ?? ''))
    .filter(Boolean);
}

function extractReplyIdsFromRawText(rawText) {
  if (typeof rawText !== 'string' || !rawText) {
    return [];
  }

  const replyPattern = /\[CQ:reply,([^\]]+)\]/g;
  const replyIds = [];
  let match = replyPattern.exec(rawText);

  while (match) {
    const params = match[1];
    const idMatch = params.match(/(?:^|,)id=([^,\]]+)/);
    const replyId = normalizeFileId(safeDecodeURIComponent(idMatch?.[1] ?? ''));

    if (replyId) {
      replyIds.push(replyId);
    }

    match = replyPattern.exec(rawText);
  }

  return replyIds;
}

function extractImageInputsFromSegments(message) {
  if (!Array.isArray(message)) {
    return [];
  }

  return message
    .filter((segment) => segment && typeof segment === 'object' && segment.type === 'image')
    .map((segment, index) => {
      const imageUrl =
        normalizeImageUrl(segment.data?.url) ||
        normalizeImageUrl(segment.data?.file);
      const fileId =
        normalizeFileId(segment.data?.file) ||
        normalizeFileId(segment.data?.file_id);

      if (!imageUrl && !fileId) {
        return null;
      }

      return {
        imageUrl,
        fileId,
        source: 'segment',
        index
      };
    })
    .filter(Boolean);
}

function extractImageInputsFromRawText(rawText) {
  if (typeof rawText !== 'string' || !rawText) {
    return [];
  }

  const cqImagePattern = /\[CQ:image,([^\]]+)\]/g;
  const images = [];
  let match = cqImagePattern.exec(rawText);

  while (match) {
    const params = match[1];
    const urlMatch = params.match(/(?:^|,)url=([^,\]]+)/);
    const fileMatch = params.match(/(?:^|,)file=([^,\]]+)/);
    const imageUrl =
      normalizeImageUrl(safeDecodeURIComponent(urlMatch?.[1] ?? '')) ||
      normalizeImageUrl(safeDecodeURIComponent(fileMatch?.[1] ?? ''));
    const fileId = normalizeFileId(safeDecodeURIComponent(fileMatch?.[1] ?? ''));

    if (imageUrl || fileId) {
      images.push({
        imageUrl,
        fileId,
        source: 'raw_message',
        index: images.length
      });
    }

    match = cqImagePattern.exec(rawText);
  }

  return images;
}

export function isGroupMessageEvent(event) {
  if (!event || typeof event !== 'object') {
    return false;
  }

  return event.post_type === 'message' && event.message_type === 'group';
}

export function extractRawText(event) {
  if (!event || typeof event !== 'object') {
    return '';
  }

  if (typeof event.raw_message === 'string') {
    return event.raw_message;
  }

  if (typeof event.message === 'string') {
    return event.message;
  }

  if (Array.isArray(event.message)) {
    return event.message
      .map((segment) => {
        if (typeof segment === 'string') {
          return segment;
        }

        if (segment && typeof segment === 'object') {
          if (typeof segment.text === 'string') {
            return segment.text;
          }

          if (segment.data && typeof segment.data.text === 'string') {
            return segment.data.text;
          }
        }

        return '';
      })
      .join('');
  }

  return '';
}

export function extractTriggerInfo(event, prefix) {
  const rawText = extractRawText(event);
  const plainTextFromSegments = extractPlainTextFromSegments(event?.message).trim();
  const selfId = normalizeId(event?.self_id);
  const triggerSourceText = plainTextFromSegments || rawText;
  const hasPrefixTrigger =
    typeof prefix === 'string' && prefix.length > 0 && triggerSourceText.startsWith(prefix);
  const hasMentionTrigger =
    hasSelfMentionInRawText(rawText, selfId) || hasSelfMentionInSegments(event?.message, selfId);
  const triggered = hasPrefixTrigger || hasMentionTrigger;

  if (!triggered) {
    return {
      triggered: false,
      trigger: 'none',
      rawText,
      userText: ''
    };
  }

  let userText = stripSelfMention(triggerSourceText, selfId).trim();
  userText = stripImageCodes(userText).trim();

  if (typeof prefix === 'string' && prefix.length > 0 && userText.startsWith(prefix)) {
    userText = userText.slice(prefix.length).trim();
  }

  return {
    triggered: true,
    trigger: hasPrefixTrigger ? 'prefix' : 'mention',
    rawText,
    userText: userText || '请继续。'
  };
}

export function extractImageInputs(event) {
  const imagesFromSegments = extractImageInputsFromSegments(event?.message);

  if (imagesFromSegments.length > 0) {
    return imagesFromSegments;
  }

  return extractImageInputsFromRawText(extractRawText(event));
}

export function extractReplyMessageIds(event) {
  const replyIdsFromSegments = extractReplyIdsFromSegments(event?.message);

  if (replyIdsFromSegments.length > 0) {
    return replyIdsFromSegments;
  }

  return extractReplyIdsFromRawText(extractRawText(event));
}

export function createNapCatClient({
  url,
  token,
  onEvent,
  onOpen,
  onClose,
  onError,
  actionTimeoutMs = 10000
}) {
  let socket = null;
  let echoCounter = 0;
  const pendingActions = new Map();

  function buildAuthHeaders() {
    return {
      Authorization: `Bearer ${token}`
    };
  }

  function getSocket() {
    return socket;
  }

  function nextEcho() {
    echoCounter += 1;
    return `echo_${Date.now()}_${echoCounter}`;
  }

  function clearPendingActions(reason) {
    for (const [echo, pending] of pendingActions.entries()) {
      clearTimeout(pending.timeoutId);
      pending.reject(new Error(reason));
      pendingActions.delete(echo);
    }
  }

  function handleActionResponse(packet) {
    const echo = String(packet.echo);
    const pending = pendingActions.get(echo);

    if (!pending) {
      return false;
    }

    const isSuccess =
      packet.status === 'ok' ||
      packet.retcode === 0 ||
      packet.retcode === undefined ||
      packet.retcode === null;

    if (pending.mode === 'stream') {
      if (!isSuccess) {
        clearTimeout(pending.timeoutId);
        pendingActions.delete(echo);
        const detail = packet.wording || packet.message || `retcode=${packet.retcode}`;
        pending.reject(new Error(`NapCat stream action failed: ${detail}`));
        return true;
      }

      pending.packets.push(packet);

      const packetType = packet.data?.type;
      const isFinalPacket = packetType === 'response' || packet.data?.data_type === 'file_complete';

      if (isFinalPacket) {
        clearTimeout(pending.timeoutId);
        pendingActions.delete(echo);
        pending.resolve({
          packets: pending.packets.slice(),
          finalPacket: packet
        });
      }

      return true;
    }

    clearTimeout(pending.timeoutId);
    pendingActions.delete(echo);

    if (isSuccess) {
      pending.resolve(packet);
    } else {
      const detail = packet.wording || packet.message || `retcode=${packet.retcode}`;
      pending.reject(new Error(`NapCat action failed: ${detail}`));
    }

    return true;
  }

  function connect() {
    if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
      return socket;
    }

    socket = new WebSocket(url, {
      headers: buildAuthHeaders()
    });

    socket.on('open', () => {
      if (typeof onOpen === 'function') {
        onOpen();
      }
    });

    socket.on('message', (data) => {
      const raw = toUtf8String(data);
      const packet = safeJsonParse(raw, null);

      if (!packet) {
        console.error('[napcat] 收到无法解析的 JSON 数据。');
        return;
      }

      if (packet && typeof packet === 'object' && Object.hasOwn(packet, 'echo') && packet.echo !== undefined) {
        const handled = handleActionResponse(packet);

        if (handled) {
          return;
        }
      }

      if (typeof onEvent === 'function') {
        onEvent(packet);
      }
    });

    socket.on('error', (error) => {
      if (typeof onError === 'function') {
        onError(error);
      } else {
        console.error('[napcat] WebSocket 错误:', error);
      }
    });

    socket.on('close', (code, reasonBuffer) => {
      const reason = toUtf8String(reasonBuffer);
      clearPendingActions(`NapCat WebSocket 已断开: code=${code}, reason=${reason || 'none'}`);
      socket = null;

      if (typeof onClose === 'function') {
        onClose(code, reason);
      }
    });

    return socket;
  }

  function sendAction(action, params) {
    return sendActionInternal(action, params, 'normal');
  }

  function sendStreamAction(action, params) {
    return sendActionInternal(action, params, 'stream');
  }

  function sendActionInternal(action, params, mode) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return Promise.reject(new Error('NapCat WebSocket 未连接。'));
    }

    const echo = nextEcho();
    const payload = {
      action,
      params,
      echo
    };

    return new Promise((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        pendingActions.delete(echo);
        reject(new Error(`NapCat action timeout: ${action}`));
      }, actionTimeoutMs);

      pendingActions.set(echo, {
        action,
        mode,
        packets: [],
        resolve,
        reject,
        timeoutId
      });

      socket.send(JSON.stringify(payload), (error) => {
        if (!error) {
          return;
        }

        clearTimeout(timeoutId);
        pendingActions.delete(echo);
        reject(new Error(`NapCat action send failed: ${formatError(error)}`));
      });
    });
  }

  function sendGroupMsg(groupId, message) {
    return sendAction('send_group_msg', {
      group_id: groupId,
      message
    });
  }

  function disconnect(code = 1000, reason = 'client disconnect') {
    if (!socket) {
      return;
    }

    const currentSocket = socket;

    if (
      currentSocket.readyState === WebSocket.OPEN ||
      currentSocket.readyState === WebSocket.CONNECTING
    ) {
      currentSocket.close(code, reason);
      return;
    }

    socket = null;
  }

  return {
    connect,
    disconnect,
    getSocket,
    sendAction,
    sendStreamAction,
    sendGroupMsg
  };
}
