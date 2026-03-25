import { createLlmReplyOutcome } from '../domain/llm-reply-outcome.mjs';
import { buildDeliberationConversationDelta } from './deliberation-policy.mjs';
import { formatError } from '../utils.mjs';

function buildPlannerPrompt(userText) {
  return [
    '[INTERNAL_PLANNER]',
    '\u4f60\u6b63\u5728\u505a\u5185\u90e8\u89c4\u5212\uff0c\u4e0d\u76f4\u63a5\u56de\u7b54\u7528\u6237\u3002',
    '\u8bf7\u8f93\u51fa\u7b80\u77ed\u7684\u5185\u90e8\u601d\u8003\u63d0\u7eb2\uff0c\u5305\u542b\uff1a\u95ee\u9898\u62c6\u89e3\u3001\u9700\u8981\u6838\u5b9e\u7684\u70b9\u3001\u56de\u7b54\u7ed3\u6784\u3002',
    '\u4e0d\u8981\u5199\u5f00\u573a\u767d\uff0c\u4e0d\u8981\u76f4\u63a5\u7ed9\u7528\u6237\u6700\u7ec8\u7b54\u6848\u3002',
    '',
    `\u7528\u6237\u95ee\u9898\uff1a${userText}`
  ].join('\n');
}

function buildDraftPrompt(userText, planText) {
  return [
    '[INTERNAL_DRAFT]',
    '\u4f60\u73b0\u5728\u57fa\u4e8e\u5185\u90e8\u89c4\u5212\u751f\u6210\u7ed9\u7528\u6237\u7684\u6b63\u5f0f\u7b54\u590d\u3002',
    '\u8981\u6c42\uff1a\u76f4\u63a5\u56de\u7b54\u3001\u7ed3\u6784\u6e05\u6670\u3001\u5c3d\u91cf\u51c6\u786e\uff0c\u4e0d\u8981\u66b4\u9732\u201c\u5185\u90e8\u89c4\u5212\u201d\u8fd9\u4e2a\u8fc7\u7a0b\u3002',
    '',
    `\u7528\u6237\u95ee\u9898\uff1a${userText}`,
    '',
    `\u5185\u90e8\u89c4\u5212\uff1a${planText || '\u65e0'}`
  ].join('\n');
}

function buildRewritePrompt(userText, planText, draftText) {
  return [
    '[INTERNAL_REWRITE]',
    '\u4f60\u73b0\u5728\u505a\u6700\u7ec8\u8d28\u68c0\u548c\u6539\u5199\u3002',
    '\u8bf7\u68c0\u67e5\u8349\u7a3f\u7b54\u6848\u662f\u5426\u5b58\u5728\u9057\u6f0f\u3001\u6a21\u7cca\u3001\u5e9f\u8bdd\u3001\u903b\u8f91\u8df3\u6b65\u6216\u4e8b\u5b9e\u98ce\u9669\uff0c\u7136\u540e\u76f4\u63a5\u8f93\u51fa\u6539\u5199\u540e\u7684\u6700\u7ec8\u7b54\u6848\u3002',
    '\u4e0d\u8981\u89e3\u91ca\u4fee\u6539\u8fc7\u7a0b\uff0c\u4e0d\u8981\u8f93\u51fa\u8d28\u68c0\u9879\u3002',
    '',
    `\u7528\u6237\u95ee\u9898\uff1a${userText}`,
    '',
    `\u5185\u90e8\u89c4\u5212\uff1a${planText || '\u65e0'}`,
    '',
    `\u8349\u7a3f\u7b54\u6848\uff1a${draftText || '\u65e0'}`
  ].join('\n');
}

export async function runDeliberationPipeline({
  llmRouter,
  executionPlan,
  logger
}) {
  const { deliberation, route, sessionContext, userText } = executionPlan;
  let planText = '';

  try {
    const plannerReply = await llmRouter.generateReply({
      ...deliberation.plannerRequest,
      route,
      userText: buildPlannerPrompt(userText)
    });

    planText = plannerReply.text || '';
    logger.info?.(
      `[openai] Planner generated: route=${plannerReply.route}, model=${plannerReply.model}, length=${planText.length}`
    );
  } catch (error) {
    logger.error?.(`[openai] Planner failed, fallback to direct drafting: ${formatError(error)}`);
  }

  const draftReply = await llmRouter.generateReply({
    ...deliberation.draftRequest,
    route,
    userText: buildDraftPrompt(userText, planText)
  });
  const draftText = draftReply.text || '';

  try {
    const rewriteReply = await llmRouter.generateReply({
      ...deliberation.rewriteRequest,
      route,
      userText: buildRewritePrompt(userText, planText, draftText)
    });
    const rewrittenText = rewriteReply.text || draftText;

    return {
      ...rewriteReply,
      ...createLlmReplyOutcome({
        text: rewrittenText,
        responseId: rewriteReply.responseId,
        conversationDelta: buildDeliberationConversationDelta({
          sharedMessages: sessionContext.sharedMessages,
          userText,
          assistantText: rewrittenText
        })
      })
    };
  } catch (error) {
    logger.error?.(`[openai] Final rewrite failed, fallback to draft answer: ${formatError(error)}`);
    return {
      ...draftReply,
      ...createLlmReplyOutcome({
        text: draftText,
        responseId: draftReply.responseId,
        conversationDelta: buildDeliberationConversationDelta({
          sharedMessages: sessionContext.sharedMessages,
          userText,
          assistantText: draftText
        })
      })
    };
  }
}
