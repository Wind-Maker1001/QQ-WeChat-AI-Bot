process.env.QQ_AI_BOT_WORKER_KIND = process.env.QQ_AI_BOT_WORKER_KIND || 'wechat';

await import('./runtime-worker.mjs');
