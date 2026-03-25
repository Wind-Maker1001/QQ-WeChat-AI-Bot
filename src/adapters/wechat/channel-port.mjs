import { createChannelPort } from '../../domain/channel-port.mjs';
import { normalizeIncomingWechatBridgeEvent } from './normalize-event.mjs';

function extractBridgeImageUrl(response) {
  if (typeof response?.data?.imageUrl === 'string' && response.data.imageUrl) {
    return response.data.imageUrl;
  }

  if (typeof response?.data?.dataUrl === 'string' && response.data.dataUrl) {
    return response.data.dataUrl;
  }

  return '';
}

export function createWechatChannelPort({ bridgeClient, prefix = '' }) {
  return createChannelPort({
    channelId: 'wechat',
    async sendText(chatId, text) {
      return bridgeClient.sendGroupText(chatId, text);
    },
    async getMessage(messageId) {
      const response = await bridgeClient.sendAction('get_message', {
        messageId
      });

      return normalizeIncomingWechatBridgeEvent(response?.data, { prefix });
    },
    async readImage(imageRef) {
      if (!imageRef || typeof imageRef !== 'object') {
        return null;
      }

      const hasFileRef = typeof imageRef.fileRef === 'string' && imageRef.fileRef;

      if (hasFileRef) {
        const response = await bridgeClient.sendAction('download_image', {
          fileId: imageRef.fileRef
        });
        const imageUrl = extractBridgeImageUrl(response);

        if (!imageUrl) {
          throw new Error(`Failed to download WeChat image: ${imageRef.fileRef}`);
        }

        return {
          ...imageRef,
          imageUrl,
          trustedLocalPath: true
        };
      }

      if (typeof imageRef.imageUrl === 'string' && imageRef.imageUrl) {
        return imageRef;
      }

      return null;
    }
  });
}
