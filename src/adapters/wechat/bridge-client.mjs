import WebSocket from 'ws';

import { formatError, safeJsonParse, toUtf8String } from '../../utils.mjs';

export function createWechatBridgeClient({
  url,
  token = '',
  actionTimeoutMs = 10000,
  onEvent,
  onOpen,
  onClose,
  onError
}) {
  let socket = null;
  let requestCounter = 0;
  const pendingRequests = new Map();

  function buildHeaders() {
    return token
      ? {
          Authorization: `Bearer ${token}`
        }
      : {};
  }

  function nextRequestId() {
    requestCounter += 1;
    return `wechat_req_${Date.now()}_${requestCounter}`;
  }

  function clearPendingRequests(reason) {
    for (const [requestId, pending] of pendingRequests.entries()) {
      clearTimeout(pending.timeoutId);
      pending.reject(new Error(reason));
      pendingRequests.delete(requestId);
    }
  }

  function connect() {
    if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
      return socket;
    }

    socket = new WebSocket(url, {
      headers: buildHeaders()
    });

    socket.on('open', () => {
      onOpen?.();
    });

    socket.on('message', (data) => {
      const packet = safeJsonParse(toUtf8String(data), null);

      if (!packet) {
        onError?.(new Error('Invalid JSON packet from wechat bridge.'));
        return;
      }

      if (
        packet.type === 'action_result' &&
        typeof packet.requestId === 'string' &&
        pendingRequests.has(packet.requestId)
      ) {
        const pending = pendingRequests.get(packet.requestId);
        clearTimeout(pending.timeoutId);
        pendingRequests.delete(packet.requestId);

        if (packet.ok === false) {
          pending.reject(new Error(packet.error || 'Wechat bridge action failed.'));
        } else {
          pending.resolve(packet);
        }

        return;
      }

      onEvent?.(packet);
    });

    socket.on('error', (error) => {
      onError?.(error);
    });

    socket.on('close', (code, reasonBuffer) => {
      const reason = toUtf8String(reasonBuffer);
      clearPendingRequests(`Wechat bridge closed: code=${code}, reason=${reason || 'none'}`);
      socket = null;
      onClose?.(code, reason);
    });

    return socket;
  }

  function disconnect(code = 1000, reason = 'client disconnect') {
    if (!socket) {
      return;
    }

    if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
      socket.close(code, reason);
      return;
    }

    socket = null;
  }

  function sendAction(action, payload = {}) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return Promise.reject(new Error('Wechat bridge is not connected.'));
    }

    const requestId = nextRequestId();

    return new Promise((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        pendingRequests.delete(requestId);
        reject(new Error(`Wechat bridge action timeout: ${action}`));
      }, actionTimeoutMs);

      pendingRequests.set(requestId, {
        resolve,
        reject,
        timeoutId
      });

      socket.send(
        JSON.stringify({
          type: 'action',
          requestId,
          action,
          params: payload
        }),
        (error) => {
          if (error) {
            clearTimeout(timeoutId);
            pendingRequests.delete(requestId);
            reject(new Error(`Wechat bridge send failed: ${formatError(error)}`));
          }
        }
      );
    });
  }

  function sendGroupText(chatId, text) {
    return sendAction('send_group_text', {
      chatId,
      text
    });
  }

  function sendGroupImage(chatId, imagePath) {
    return sendAction('send_group_image', {
      chatId,
      imagePath
    });
  }

  return {
    connect,
    disconnect,
    sendAction,
    sendGroupText,
    sendGroupImage
  };
}
