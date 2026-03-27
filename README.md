# Local AI Runtime Console

This repository packages a local multi-process AI runtime for personal use: Node supervisor, QQ and WeChat workers, a local control API, and a WPF desktop control plane.

本项目当前应被理解为：

`单机多进程 AI runtime + 本地 desktop control plane`

不要把它当成“只有一个 QQ bot 脚本”的仓库，也不要只依赖历史 README 心智模型。

## Start Here

当前推荐阅读顺序：

1. [docs/current-architecture.md](docs/current-architecture.md)
2. `tests/`
3. `desktop/QQAIBot.Desktop.Tests/Program.cs`
4. `src/` 和 `desktop/QQAIBot.Desktop/`

当 README、代码、测试不一致时：

- 以代码和测试为准
- 以 [docs/current-architecture.md](docs/current-architecture.md) 为当前架构说明入口

## Quick Start

安装依赖并准备配置：

```powershell
npm install
Copy-Item .env.example .env
```

运行完整回归：

```powershell
npm test
```

启动 supervisor：

```powershell
npm start
```

启动 Local AI Runtime 控制台：

```powershell
dotnet run --project .\desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj
```

## Current Entry Points

核心入口：

- `src/index.mjs`
- `src/app/runtime-worker.mjs`
- `src/app/wechat-runtime-worker.mjs`
- `src/app/control-api.mjs`
- `desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs`

## Key Scripts

最常用命令：

```powershell
npm test
npm start
npm run test:node
npm run test:desktop
npm run desktop:acceptance
npm run wechat:bridge:sim
npm run setup:install
npm run setup:validate
npm run setup:uninstall
npm run setup:uninstall:keep-state
npm run release:source
npm run release:installable
npm run release:installer
```

## Key Config

最关键的环境变量见 `.env.example`。

通常至少需要关注：

- `OPENAI_API_KEY`
- `OPENAI_DEFAULT_MODEL`
- `OPENAI_ADVANCED_MODEL`
- `NAPCAT_WS_URL`
- `NAPCAT_TOKEN`
- `WECHAT_BRIDGE_URL`
- `WECHAT_BRIDGE_TOKEN`
- `BOT_PREFIX`
- `WECHAT_BOT_PREFIX`
- `QQ_AI_BOT_CONTROL_API_TOKEN`

## Install Layout

默认安装根目录：

- `%LOCALAPPDATA%\QQAIBot`

默认关键路径：

- 应用文件：`%LOCALAPPDATA%\QQAIBot\app`
- 配置文件：`%LOCALAPPDATA%\QQAIBot\app\.env`
- 会话与图片缓存：`%LOCALAPPDATA%\QQAIBot\app\data\`
- 状态快照：`%LOCALAPPDATA%\QQAIBot\app\artifacts\state-snapshots\`
- desktop activity state：`%LOCALAPPDATA%\QQAIBot.Desktop\activity-state\<hash>.json`

安装脚本完成后会直接打印这些路径。

## Upgrade And Uninstall

安装 / 升级：

- `npm run setup:install`
- 重新运行同一个安装脚本会原地升级。
- 原地升级会替换 `app\` 下的程序文件，但保留 `.env`、`data\`、`artifacts\state-snapshots\`，以及 desktop activity state。

卸载：

- `npm run setup:uninstall`
- 这会删除当前安装对应的应用文件、配置、会话数据、状态快照，以及 desktop activity state。

保留状态卸载：

- `npm run setup:uninstall:keep-state`
- 这会删除应用二进制和快捷方式，但保留 `.env`、`data\`、`artifacts\state-snapshots\`，以及 desktop activity state，便于之后重新安装接回原状态。

## Docs

当前文档入口：

- [docs/current-architecture.md](docs/current-architecture.md)
- [docs/operations.md](docs/operations.md)
- [docs/testing.md](docs/testing.md)
- [docs/review-checklist.md](docs/review-checklist.md)

## Legacy Note

旧 README 曾经同时承担产品介绍、安装手册、运行手册和架构说明。
现在这些内容应逐步下沉到 `docs/`，README 仅保留当前入口作用。
