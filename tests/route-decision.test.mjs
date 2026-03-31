import test from 'node:test';
import assert from 'node:assert/strict';

import {
  buildRouteDecisionSummary,
  createRouteDecision,
  formatRouteDecisionReason,
  getRouteDecisionReasonGroups,
  getRouteDecisionRequestedCapabilities,
  getRouteDecisionTrigger
} from '../src/domain/route-decision.mjs';

test('createRouteDecision normalizes structured reason groups and requested capabilities', () => {
  const decision = createRouteDecision({
    route: 'advanced',
    userText: 'analyze this',
    imageCount: 1,
    selectedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    trigger: {
      kind: 'image'
    },
    reasonGroups: {
      triggerReasons: ['image'],
      capabilityReasons: ['web_search'],
      upgradeReasons: ['capability_upgrade']
    },
    requestedCapabilities: {
      reasoningEffort: 'high',
      textVerbosity: 'high',
      enableWebSearch: true,
      enableCodeInterpreter: false
    }
  });

  assert.equal(decision.route, 'advanced');
  assert.equal(decision.model, 'gpt-5.4');
  assert.equal(decision.apiStyle, 'responses');
  assert.deepEqual(getRouteDecisionTrigger(decision), {
    kind: 'image',
    matchedPrefix: ''
  });
  assert.deepEqual(getRouteDecisionReasonGroups(decision), {
    triggerReasons: ['image'],
    capabilityReasons: ['web_search'],
    upgradeReasons: ['capability_upgrade']
  });
  assert.deepEqual(getRouteDecisionRequestedCapabilities(decision), {
    reasoningEffort: 'high',
    textVerbosity: 'high',
    enableWebSearch: true,
    enableCodeInterpreter: false,
    needsResponsesCapabilities: true
  });
  assert.equal(formatRouteDecisionReason(decision), 'image+web_search+capability_upgrade');
});

test('createRouteDecision infers trigger reasons from directive metadata when not provided', () => {
  const decision = createRouteDecision({
    route: 'advanced',
    userText: 'look at this',
    selectedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    trigger: {
      kind: 'directive',
      matchedPrefix: '/vision'
    }
  });

  assert.deepEqual(getRouteDecisionTrigger(decision), {
    kind: 'directive',
    matchedPrefix: '/vision'
  });
  assert.deepEqual(getRouteDecisionReasonGroups(decision), {
    triggerReasons: ['directive:/vision'],
    capabilityReasons: [],
    upgradeReasons: []
  });
});

test('buildRouteDecisionSummary exposes normalized route metadata', () => {
  const decision = createRouteDecision({
    route: 'default',
    userText: 'hello',
    selectedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    reasonGroups: {
      capabilityReasons: ['complex']
    },
    requestedCapabilities: {
      reasoningEffort: 'high',
      textVerbosity: 'high'
    }
  });

  assert.deepEqual(buildRouteDecisionSummary(decision), {
    trigger: {
      kind: 'default',
      matchedPrefix: ''
    },
    reasonTags: ['complex'],
    reasonGroups: {
      triggerReasons: [],
      capabilityReasons: ['complex'],
      upgradeReasons: []
    },
    requestedCapabilities: {
      reasoningEffort: 'high',
      textVerbosity: 'high',
      enableWebSearch: undefined,
      enableCodeInterpreter: undefined,
      needsResponsesCapabilities: true
    },
    routeReason: 'complex',
    matchedPrefix: ''
  });
});

test('createRouteDecision falls back to default reason tag when no explicit groups exist', () => {
  const decision = createRouteDecision({
    route: 'default',
    userText: 'hello'
  });

  assert.equal(formatRouteDecisionReason(decision), 'default');
  assert.deepEqual(getRouteDecisionReasonGroups(decision), {
    triggerReasons: [],
    capabilityReasons: [],
    upgradeReasons: []
  });
});
