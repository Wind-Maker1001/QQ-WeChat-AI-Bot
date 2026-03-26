export {
  BOT_INSTRUCTIONS,
  DEFAULT_BOT_SYSTEM_PROMPT,
  DEFAULT_ADVANCED_MODEL,
  DEFAULT_DEFAULT_MODEL,
  DEFAULT_OPENAI_BASE_URL,
  prepareImageInputs
} from './adapters/llm/openai-provider.mjs';
export { DEFAULT_ADVANCED_TRIGGER_PREFIXES } from './domain/route-decision.mjs';
export { createLlmRouter as createOpenAIService } from './adapters/llm/llm-router.mjs';
