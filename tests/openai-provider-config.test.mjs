import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs/promises';

import { __test__, createOpenAIProvider, prepareImageInputs } from '../src/adapters/llm/openai-provider.mjs';

function listen(server) {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      server.off('error', reject);
      const address = server.address();

      if (!address || typeof address === 'string') {
        reject(new Error('Failed to resolve server address.'));
        return;
      }

      resolve(address.port);
    });
  });
}

function close(server) {
  return new Promise((resolve, reject) => {
    server.close((error) => {
      if (error) {
        reject(error);
        return;
      }

      resolve();
    });
  });
}

test('OpenAI provider applies GPT-5 default reasoning and verbosity heuristics', () => {
  const defaultProvider = createOpenAIProvider({
    routeName: 'default',
    apiKey: 'test-key',
    model: 'gpt-5.4',
    baseURL: 'http://127.0.0.1:1',
    apiStyle: 'responses'
  });
  const advancedProvider = createOpenAIProvider({
    routeName: 'advanced',
    apiKey: 'test-key',
    model: 'gpt-5.4',
    baseURL: 'http://127.0.0.1:1',
    apiStyle: 'responses'
  });

  assert.equal(defaultProvider.reasoningEffort, 'medium');
  assert.equal(defaultProvider.textVerbosity, 'medium');
  assert.equal(advancedProvider.reasoningEffort, 'high');
  assert.equal(advancedProvider.textVerbosity, 'high');
});

test('OpenAI provider sends configured reasoning effort and text verbosity in responses requests', async () => {
  const requests = [];
  const server = http.createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/responses') {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const chunks = [];

    for await (const chunk of req) {
      chunks.push(chunk);
    }

    requests.push(JSON.parse(Buffer.concat(chunks).toString('utf8')));

    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(
      JSON.stringify({
        id: 'resp_test',
        output_text: 'ok'
      })
    );
  });

  const port = await listen(server);

  try {
    const provider = createOpenAIProvider({
      routeName: 'advanced',
      apiKey: 'test-key',
      model: 'gpt-5.4',
      baseURL: `http://127.0.0.1:${port}`,
      apiStyle: 'responses',
      reasoningEffort: 'high',
      textVerbosity: 'high'
    });

    const reply = await provider.generateReply({
      userText: 'explain this',
      previousResponseId: 'prev_resp_id',
      sharedMessages: [],
      imageInputs: []
    });

    assert.equal(requests.length, 1);
    assert.equal(requests[0].reasoning.effort, 'high');
    assert.equal(requests[0].text.verbosity, 'high');
    assert.equal(requests[0].previous_response_id, 'prev_resp_id');
    assert.equal(reply.configuredApiStyle, 'responses');
    assert.equal(reply.effectiveApiStyle, 'responses');
    assert.equal(reply.configuredReasoningEffort, 'high');
    assert.equal(reply.effectiveReasoningEffort, 'high');
    assert.equal(reply.configuredTextVerbosity, 'high');
    assert.equal(reply.effectiveTextVerbosity, 'high');
  } finally {
    await close(server);
  }
});

test('OpenAI provider builds responses tools for web search and code interpreter', () => {
  assert.deepEqual(__test__.buildResponsesTools(), []);
  assert.deepEqual(__test__.buildResponsesTools({ enableWebSearch: true }), [
    { type: 'web_search' }
  ]);
  assert.deepEqual(__test__.buildResponsesTools({ enableCodeInterpreter: true }), [
    {
      type: 'code_interpreter',
      container: { type: 'auto' }
    }
  ]);
  assert.deepEqual(
    __test__.buildResponsesTools({
      enableWebSearch: true,
      enableCodeInterpreter: true
    }),
    [
      { type: 'web_search' },
      { type: 'code_interpreter', container: { type: 'auto' } }
    ]
  );
});

test('OpenAI provider sends configured tools in responses requests', async () => {
  const requests = [];
  const server = http.createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/responses') {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const chunks = [];

    for await (const chunk of req) {
      chunks.push(chunk);
    }

    requests.push(JSON.parse(Buffer.concat(chunks).toString('utf8')));

    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(
      JSON.stringify({
        id: 'resp_tools',
        output_text: 'ok'
      })
    );
  });

  const port = await listen(server);

  try {
    const provider = createOpenAIProvider({
      routeName: 'advanced',
      apiKey: 'test-key',
      model: 'gpt-5.4',
      baseURL: `http://127.0.0.1:${port}`,
      apiStyle: 'responses',
      enableWebSearch: true,
      enableCodeInterpreter: true
    });

    const reply = await provider.generateReply({
      userText: 'what is the latest official guidance',
      previousResponseId: null,
      sharedMessages: [],
      imageInputs: []
    });

    assert.equal(requests.length, 1);
    assert.deepEqual(requests[0].tools, [
      { type: 'web_search' },
      { type: 'code_interpreter', container: { type: 'auto' } }
    ]);
    assert.deepEqual(reply.configuredTools, ['web_search', 'code_interpreter']);
    assert.deepEqual(reply.effectiveTools, ['web_search', 'code_interpreter']);
    assert.equal(reply.configuredEnableWebSearch, true);
    assert.equal(reply.effectiveEnableWebSearch, true);
    assert.equal(reply.configuredEnableCodeInterpreter, true);
    assert.equal(reply.effectiveEnableCodeInterpreter, true);
  } finally {
    await close(server);
  }
});

test('OpenAI provider applies per-request override settings for extend-thinking style upgrades', async () => {
  const requests = [];
  const server = http.createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/responses') {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const chunks = [];

    for await (const chunk of req) {
      chunks.push(chunk);
    }

    requests.push(JSON.parse(Buffer.concat(chunks).toString('utf8')));

    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(
      JSON.stringify({
        id: 'resp_override',
        output_text: 'ok'
      })
    );
  });

  const port = await listen(server);

  try {
    const provider = createOpenAIProvider({
      routeName: 'default',
      apiKey: 'test-key',
      model: 'gpt-5.4',
      baseURL: `http://127.0.0.1:${port}`,
      apiStyle: 'responses',
      reasoningEffort: 'medium',
      textVerbosity: 'medium',
      enableWebSearch: false,
      enableCodeInterpreter: false
    });

    const reply = await provider.generateReply({
      userText: 'analyze this deeply',
      previousResponseId: null,
      sharedMessages: [],
      imageInputs: [],
      reasoningEffortOverride: 'high',
      textVerbosityOverride: 'high',
      enableWebSearchOverride: true,
      enableCodeInterpreterOverride: true
    });

    assert.equal(requests.length, 1);
    assert.equal(requests[0].reasoning.effort, 'high');
    assert.equal(requests[0].text.verbosity, 'high');
    assert.deepEqual(requests[0].tools, [
      { type: 'web_search' },
      { type: 'code_interpreter', container: { type: 'auto' } }
    ]);
    assert.equal(reply.configuredReasoningEffort, 'medium');
    assert.equal(reply.effectiveReasoningEffort, 'high');
    assert.equal(reply.configuredTextVerbosity, 'medium');
    assert.equal(reply.effectiveTextVerbosity, 'high');
    assert.deepEqual(reply.configuredTools, []);
    assert.deepEqual(reply.effectiveTools, ['web_search', 'code_interpreter']);
  } finally {
    await close(server);
  }
});

test('OpenAI provider applies store override for internal planner-style requests', async () => {
  const requests = [];
  const server = http.createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/responses') {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const chunks = [];
    for await (const chunk of req) {
      chunks.push(chunk);
    }

    requests.push(JSON.parse(Buffer.concat(chunks).toString('utf8')));
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ id: 'resp_store_override', output_text: 'ok' }));
  });

  const port = await listen(server);

  try {
    const provider = createOpenAIProvider({
      routeName: 'default',
      apiKey: 'test-key',
      model: 'gpt-5.4',
      baseURL: `http://127.0.0.1:${port}`,
      apiStyle: 'responses'
    });

    await provider.generateReply({
      userText: '[INTERNAL_PLANNER] plan this',
      sharedMessages: [],
      imageInputs: [],
      storeOverride: false
    });

    assert.equal(requests.length, 1);
    assert.equal(requests[0].store, false);
  } finally {
    await close(server);
  }
});

test('prepareImageInputs rejects remote http image URLs', async () => {
  await assert.rejects(
    () =>
      prepareImageInputs([
        {
          imageUrl: 'https://example.com/image.png'
        }
      ]),
    /Remote image URLs are not allowed/
  );
});

test('prepareImageInputs rejects untrusted local file paths', async () => {
  await assert.rejects(
    () =>
      prepareImageInputs([
        {
          imageUrl: 'file:///C:/Windows/win.ini'
        }
      ]),
    /Unsupported image reference/
  );
});

test('prepareImageInputs accepts trusted local file paths', async () => {
  const tempDir = await fs.mkdtemp('D:\\Temp\\qq-ai-bot-image-');
  const filePath = `${tempDir}\\sample.png`;
  await fs.writeFile(
    filePath,
    Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9s5lMP8AAAAASUVORK5CYII=', 'base64')
  );

  const prepared = await prepareImageInputs([
    {
      imageUrl: filePath,
      trustedLocalPath: true
    }
  ]);

  assert.equal(prepared.length, 1);
  assert.match(prepared[0].imageUrl, /^data:image\/png;base64,/);
});
