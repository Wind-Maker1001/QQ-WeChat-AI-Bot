import test from 'node:test';
import assert from 'node:assert/strict';

import { createWechatChannelPort } from '../src/adapters/wechat/channel-port.mjs';

const PNG_DATA_URL =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9s5lMP8AAAAASUVORK5CYII=';

test('WeChat channel prefers fileRef download over direct imageUrl', async () => {
  const channelPort = createWechatChannelPort({
    bridgeClient: {
      async sendGroupText() {},
      async sendAction(action, params) {
        assert.equal(action, 'download_image');
        assert.equal(params.fileId, 'wechat-file-ref');

        return {
          data: {
            dataUrl: PNG_DATA_URL
          }
        };
      }
    },
    prefix: '/ai'
  });

  const image = await channelPort.readImage({
    imageUrl: 'http://bad-wechat-url.example/bad',
    fileRef: 'wechat-file-ref',
    source: 'wechat-bridge',
    index: 0
  });

  assert.equal(image.imageUrl, PNG_DATA_URL);
});
