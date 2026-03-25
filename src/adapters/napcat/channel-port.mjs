import { createChannelPort } from '../../domain/channel-port.mjs';
import { normalizeIncomingNapCatEvent } from './normalize-event.mjs';

function inferMimeTypeFromFilePath(filePath = '') {
  const normalizedFilePath = typeof filePath === 'string' ? filePath.trim().toLowerCase() : '';

  if (!normalizedFilePath) {
    return '';
  }

  if (normalizedFilePath.endsWith('.png')) {
    return 'image/png';
  }

  if (normalizedFilePath.endsWith('.webp')) {
    return 'image/webp';
  }

  if (normalizedFilePath.endsWith('.gif')) {
    return 'image/gif';
  }

  if (normalizedFilePath.endsWith('.bmp')) {
    return 'image/bmp';
  }

  if (normalizedFilePath.endsWith('.jpg') || normalizedFilePath.endsWith('.jpeg')) {
    return 'image/jpeg';
  }

  return '';
}

function inferMimeTypeFromBase64(base64 = '') {
  if (typeof base64 !== 'string' || !base64) {
    return '';
  }

  try {
    const header = Buffer.from(base64.slice(0, 96), 'base64');

    if (header.length >= 4) {
      if (header[0] === 0x89 && header[1] === 0x50 && header[2] === 0x4e && header[3] === 0x47) {
        return 'image/png';
      }

      if (header[0] === 0xff && header[1] === 0xd8 && header[2] === 0xff) {
        return 'image/jpeg';
      }

      if (header[0] === 0x47 && header[1] === 0x49 && header[2] === 0x46 && header[3] === 0x38) {
        return 'image/gif';
      }

      if (
        header.length >= 12 &&
        header.subarray(0, 4).toString('ascii') === 'RIFF' &&
        header.subarray(8, 12).toString('ascii') === 'WEBP'
      ) {
        return 'image/webp';
      }

      if (header[0] === 0x42 && header[1] === 0x4d) {
        return 'image/bmp';
      }
    }
  } catch {
    return '';
  }

  return '';
}

function inferImageMimeType({ base64 = '', finalPacket, imageRef } = {}) {
  const candidates = [
    finalPacket?.data?.file_path,
    finalPacket?.data?.file,
    imageRef?.fileRef
  ];

  for (const candidate of candidates) {
    const mimeType = inferMimeTypeFromFilePath(candidate);

    if (mimeType) {
      return mimeType;
    }
  }

  return inferMimeTypeFromBase64(base64) || 'image/jpeg';
}

function buildImageDataUrlFromChunks(packets = [], finalPacket, imageRef) {
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
  const mimeType = inferImageMimeType({
    base64,
    finalPacket,
    imageRef
  });
  return `data:${mimeType};base64,${base64}`;
}

export function createNapCatChannelPort({ napcatClient, prefix = '' }) {
  return createChannelPort({
    channelId: 'qq',
    async sendText(chatId, text) {
      return napcatClient.sendGroupMsg(chatId, text);
    },
    async getMessage(messageId) {
      const response = await napcatClient.sendAction('get_msg', {
        message_id: messageId
      });

      return normalizeIncomingNapCatEvent(response?.data, { prefix });
    },
    async readImage(imageRef) {
      if (!imageRef || typeof imageRef !== 'object') {
        return null;
      }

      const hasFileRef = typeof imageRef.fileRef === 'string' && imageRef.fileRef;

      if (hasFileRef) {
        const streamResult = await napcatClient.sendStreamAction('download_file_image_stream', {
          file: imageRef.fileRef
        });
        const dataUrl = buildImageDataUrlFromChunks(
          streamResult?.packets ?? [],
          streamResult?.finalPacket,
          imageRef
        );

        if (!dataUrl) {
          throw new Error(`Failed to rebuild image from NapCat stream: ${imageRef.fileRef}`);
        }

        return {
          ...imageRef,
          imageUrl: dataUrl
        };
      }

      if (typeof imageRef.imageUrl === 'string' && imageRef.imageUrl) {
        return imageRef;
      }

      return null;
    }
  });
}

export const __test__ = Object.freeze({
  inferMimeTypeFromFilePath,
  inferMimeTypeFromBase64,
  inferImageMimeType,
  buildImageDataUrlFromChunks
});
