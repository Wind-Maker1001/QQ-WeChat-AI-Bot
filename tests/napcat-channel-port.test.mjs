import test from 'node:test';
import assert from 'node:assert/strict';

import { __test__, createNapCatChannelPort } from '../src/adapters/napcat/channel-port.mjs';

const PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9s5lMP8AAAAASUVORK5CYII=';
const JPEG_BASE64 =
  '/9j/4AAQSkZJRgABAQAAAQABAAD/2wCEAAkGBxAQEBAQEA8PDw8PDw8PDw8PDw8PDw8QFREWFhURFRUYHSggGBolGxUVITEhJSkrLi4uFx8zODMsNygtLisBCgoKDg0OGxAQGi0fHyUtLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLf/AABEIAAEAAgMBIgACEQEDEQH/xAAXAAEBAQEAAAAAAAAAAAAAAAAAAQID/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEAMQAAAB6A//xAAVEAEBAAAAAAAAAAAAAAAAAAAAEf/aAAgBAQABBQKf/8QAFBEBAAAAAAAAAAAAAAAAAAAAEP/aAAgBAwEBPwF//8QAFBEBAAAAAAAAAAAAAAAAAAAAEP/aAAgBAgEBPwF//8QAFBABAAAAAAAAAAAAAAAAAAAAEP/aAAgBAQAGPwJ//8QAFBABAAAAAAAAAAAAAAAAAAAAEP/aAAgBAQABPyF//9k=';

test('NapCat image stream prefers file extension from final packet', () => {
  const dataUrl = __test__.buildImageDataUrlFromChunks(
    [
      {
        data: {
          type: 'stream',
          data_type: 'file_chunk',
          data: PNG_BASE64,
          index: 0
        }
      }
    ],
    {
      data: {
        file_path: 'C:\\temp\\napcat-image.png'
      }
    },
    {
      fileRef: 'opaque-file-ref'
    }
  );

  assert.match(dataUrl, /^data:image\/png;base64,/);
});

test('NapCat image stream falls back to base64 magic header when file path is opaque', () => {
  const dataUrl = __test__.buildImageDataUrlFromChunks(
    [
      {
        data: {
          type: 'stream',
          data_type: 'file_chunk',
          data: PNG_BASE64,
          index: 0
        }
      }
    ],
    {
      data: {
        file_path: ''
      }
    },
    {
      fileRef: 'opaque-file-ref'
    }
  );

  assert.match(dataUrl, /^data:image\/png;base64,/);
});

test('NapCat image stream still defaults to jpeg for jpeg content', () => {
  const dataUrl = __test__.buildImageDataUrlFromChunks(
    [
      {
        data: {
          type: 'stream',
          data_type: 'file_chunk',
          data: JPEG_BASE64,
          index: 0
        }
      }
    ],
    null,
    {
      fileRef: 'opaque-file-ref'
    }
  );

  assert.match(dataUrl, /^data:image\/jpeg;base64,/);
});

test('NapCat channel prefers fileRef download over direct imageUrl', async () => {
  const channelPort = createNapCatChannelPort({
    napcatClient: {
      async sendGroupMsg() {},
      async sendAction() {
        throw new Error('sendAction should not be used in this test');
      },
      async sendStreamAction(action, params) {
        assert.equal(action, 'download_file_image_stream');
        assert.equal(params.file, 'napcat-file-ref');

        return {
          packets: [
            {
              data: {
                type: 'stream',
                data_type: 'file_chunk',
                data: PNG_BASE64,
                index: 0
              }
            }
          ],
          finalPacket: {
            data: {
              file_path: 'C:\\temp\\downloaded.png'
            }
          }
        };
      }
    },
    prefix: '/ai'
  });

  const image = await channelPort.readImage({
    imageUrl: 'http://bad-napcat-url.example/bad',
    fileRef: 'napcat-file-ref',
    source: 'segment',
    index: 0
  });

  assert.match(image.imageUrl, /^data:image\/png;base64,/);
});
