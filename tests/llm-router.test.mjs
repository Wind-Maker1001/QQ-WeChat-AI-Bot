import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';

import { createLlmRouter } from '../src/adapters/llm/llm-router.mjs';
import {
  createExecutionProjection,
  EXECUTION_KIND_DIRECT,
  EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK,
  EXECUTION_STAGE_DIRECT
} from '../src/domain/execution-projection.mjs';

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

test('LLM router describes effective tools for responses requests', () => {
  const router = createLlmRouter({
    defaultRoute: {
      apiKey: 'default-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: false,
      enableCodeInterpreter: false
    },
    advancedRoute: {
      apiKey: 'advanced-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: true,
      enableCodeInterpreter: true
    }
  });

  const description = router.describeRequest({
    route: 'default',
    enableWebSearchOverride: true,
    enableCodeInterpreterOverride: false
  });

  assert.equal(description.route, 'default');
  assert.equal(description.effectiveApiStyle, 'responses');
  assert.deepEqual(description.configuredTools, []);
  assert.deepEqual(description.requestedTools, {
    requested: ['web_search'],
    required: []
  });
  assert.deepEqual(description.effectiveTools, ['web_search']);
  assert.equal(description.effectiveEnableWebSearch, true);
  assert.equal(description.effectiveEnableCodeInterpreter, false);
});

test('LLM router suppresses unavailable tools on chat-completions routes', () => {
  const router = createLlmRouter({
    defaultRoute: {
      apiKey: 'default-key',
      model: 'deepseek-chat',
      apiStyle: 'chat_completions',
      enableWebSearch: true,
      enableCodeInterpreter: true
    },
    advancedRoute: {
      apiKey: 'advanced-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: true,
      enableCodeInterpreter: true
    }
  });

  const description = router.describeRequest({
    route: 'default'
  });

  assert.equal(description.configuredApiStyle, 'chat_completions');
  assert.equal(description.effectiveApiStyle, 'chat_completions');
  assert.deepEqual(description.effectiveTools, []);
  assert.equal(description.suppressedTools[0].reason, 'requires_responses_api');
  assert.equal(description.effectiveEnableWebSearch, false);
  assert.equal(description.effectiveEnableCodeInterpreter, false);
});

test('LLM router describes local tool availability and suppression', () => {
  const router = createLlmRouter({
    defaultRoute: {
      apiKey: 'default-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: false,
      enableCodeInterpreter: false
    },
    advancedRoute: {
      apiKey: 'advanced-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: true,
      enableCodeInterpreter: true
    }
  });

  const description = router.describeRequest({
    route: 'default',
    requestedTools: {
      requested: ['local_runtime_state', 'local_snapshot_inspect'],
      required: ['local_runtime_state']
    }
  });

  assert.deepEqual(description.effectiveLocalTools, ['local_runtime_state', 'local_snapshot_inspect']);
  assert.deepEqual(description.suppressedTools, []);
});

test('LLM router falls back to DeepSeek on transient upstream failure for plain text requests', async () => {
  const primaryRequests = [];
  const deepseekRequests = [];
  const primaryServer = http.createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/responses') {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const chunks = [];
    for await (const chunk of req) {
      chunks.push(chunk);
    }

    primaryRequests.push(JSON.parse(Buffer.concat(chunks).toString('utf8')));
    res.writeHead(502, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ error: 'Upstream request failed' }));
  });
  const deepseekServer = http.createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/chat/completions') {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const chunks = [];
    for await (const chunk of req) {
      chunks.push(chunk);
    }

    deepseekRequests.push(JSON.parse(Buffer.concat(chunks).toString('utf8')));
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(
      JSON.stringify({
        id: 'chatcmpl_deepseek',
        choices: [
          {
            message: {
              role: 'assistant',
              content: 'deepseek reply'
            }
          }
        ]
      })
    );
  });

  const primaryPort = await listen(primaryServer);
  const deepseekPort = await listen(deepseekServer);

  try {
    const router = createLlmRouter({
      defaultRoute: {
        apiKey: 'default-key',
        model: 'gpt-5.4',
        apiStyle: 'responses',
        baseURL: `http://127.0.0.1:${primaryPort}`
      },
      advancedRoute: {
        apiKey: 'advanced-key',
        model: 'gpt-5.4',
        apiStyle: 'responses',
        baseURL: `http://127.0.0.1:${primaryPort}`
      },
      deepseekFallback: {
        fallbackEnabled: true,
        apiKey: 'deepseek-key',
        model: 'deepseek-chat',
        baseURL: `http://127.0.0.1:${deepseekPort}`
      }
    });

    const reply = await router.generateReply({
      route: 'default',
      userText: 'hello fallback',
      previousResponseId: 'resp_prev',
      sharedMessages: [{ role: 'assistant', content: 'previous' }],
      imageInputs: [],
      requestedTools: {
        requested: [],
        required: []
      },
      reasoningEffortOverride: '',
      textVerbosityOverride: '',
      enableWebSearchOverride: false,
      enableCodeInterpreterOverride: false
    });

    assert.ok(primaryRequests.length >= 1);
    assert.equal(deepseekRequests.length, 1);
    assert.equal(reply.route, 'default');
    assert.equal(reply.configuredModel, 'gpt-5.4');
    assert.equal(reply.model, 'deepseek-chat');
    assert.equal(reply.configuredApiStyle, 'responses');
    assert.equal(reply.effectiveApiStyle, 'chat_completions');
    assert.equal(reply.text, 'deepseek reply');
    assert.equal(reply.conversationDelta.clearPreviousResponseId, true);
    assert.equal(reply.conversationDelta.previousResponseId, null);
    assert.deepEqual(
      reply.executionProjection,
      createExecutionProjection({
        kind: EXECUTION_KIND_DIRECT,
        completedStages: [EXECUTION_STAGE_DIRECT],
        degraded: true,
        recoveries: [EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK]
      })
    );
  } finally {
    await close(primaryServer);
    await close(deepseekServer);
  }
});

test('LLM router does not fall back to DeepSeek when hosted tools are required', async () => {
  let deepseekRequests = 0;
  const primaryServer = http.createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/responses') {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    res.writeHead(502, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ error: 'Upstream request failed' }));
  });
  const deepseekServer = http.createServer(async (_req, res) => {
    deepseekRequests += 1;
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ ok: true }));
  });

  const primaryPort = await listen(primaryServer);
  const deepseekPort = await listen(deepseekServer);

  try {
    const router = createLlmRouter({
      defaultRoute: {
        apiKey: 'default-key',
        model: 'gpt-5.4',
        apiStyle: 'responses',
        baseURL: `http://127.0.0.1:${primaryPort}`
      },
      advancedRoute: {
        apiKey: 'advanced-key',
        model: 'gpt-5.4',
        apiStyle: 'responses',
        baseURL: `http://127.0.0.1:${primaryPort}`
      },
      deepseekFallback: {
        fallbackEnabled: true,
        apiKey: 'deepseek-key',
        model: 'deepseek-chat',
        baseURL: `http://127.0.0.1:${deepseekPort}`
      }
    });

    await assert.rejects(
      () =>
        router.generateReply({
          route: 'default',
          userText: 'latest guidance',
          sharedMessages: [],
          imageInputs: [],
          requestedTools: {
            requested: ['web_search'],
            required: ['web_search']
          },
          enableWebSearchOverride: true
        }),
      /Upstream request failed/
    );

    assert.equal(deepseekRequests, 0);
  } finally {
    await close(primaryServer);
    await close(deepseekServer);
  }
});
