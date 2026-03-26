const CURRENT_TURN_CAPABILITY_PATTERNS = [
  /(?:这轮|本轮|当前(?:这轮|对话|回合)?|这次|这一轮|这个回合).{0,24}(?:可调用|可调用的|可用|能用|启用|支持|有哪些|有什么|列表).{0,12}(?:工具|外部工具|能力)/,
  /(?:这轮|本轮|当前(?:这轮|对话|回合)?|这次|这一轮|这个回合).{0,24}(?:工具|外部工具|能力).{0,12}(?:可调用|可调用的|可用|能用|启用|支持|有哪些|有什么|列表|是什么|吗|\?)/,
  /(?:这轮|本轮|当前(?:这轮|对话|回合)?|这次|这一轮|这个回合).{0,24}(?:能|可以|是否能|有没有).{0,8}(?:联网|上网|搜索|查资料|查网|运行代码|执行代码|代码解释器|web_search|code_interpreter)/,
  /\b(?:what tools are available this turn|available tools this turn|callable tools this turn|can you browse this turn|can you search this turn|can you run code this turn)\b/i
];

function normalizeToolLabel(toolKind) {
  if (toolKind === 'web_search') {
    return 'web_search（联网搜索）';
  }

  if (toolKind === 'code_interpreter') {
    return 'code_interpreter（代码解释器）';
  }

  return toolKind;
}

function shouldReplyLocally(userText) {
  if (typeof userText !== 'string') {
    return false;
  }

  const normalizedText = userText.trim();

  if (!normalizedText || normalizedText.length > 160) {
    return false;
  }

  return CURRENT_TURN_CAPABILITY_PATTERNS.some((pattern) => pattern.test(normalizedText));
}

function formatToolSummary(toolKinds) {
  const normalizedTools = Array.isArray(toolKinds)
    ? toolKinds.filter((toolKind) => typeof toolKind === 'string' && toolKind)
    : [];

  return normalizedTools.length > 0
    ? normalizedTools.map(normalizeToolLabel).join('、')
    : '无';
}

export function tryBuildCurrentTurnCapabilityReply({
  userText,
  requestDescriptor
} = {}) {
  if (!shouldReplyLocally(userText) || !requestDescriptor || typeof requestDescriptor !== 'object') {
    return null;
  }

  const route = requestDescriptor.route === 'advanced' ? 'advanced' : 'default';
  const apiStyle =
    typeof requestDescriptor.effectiveApiStyle === 'string' && requestDescriptor.effectiveApiStyle
      ? requestDescriptor.effectiveApiStyle
      : 'unknown';
  const model =
    typeof requestDescriptor.model === 'string' && requestDescriptor.model
      ? requestDescriptor.model
      : 'unknown-model';

  return [
    `当前这轮会走 ${route} 路由（${apiStyle} / ${model}）。`,
    `可调用工具：${formatToolSummary(requestDescriptor.effectiveTools)}。`,
    `联网搜索：${requestDescriptor.effectiveEnableWebSearch ? '开启' : '关闭'}；代码解释器：${requestDescriptor.effectiveEnableCodeInterpreter ? '开启' : '关闭'}。`,
    '这是按本轮消息实时计算的结果，不是全局固定列表。'
  ].join('\n');
}

export const __test__ = Object.freeze({
  shouldReplyLocally,
  formatToolSummary
});
