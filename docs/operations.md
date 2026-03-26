# Operations Guide

This is the lightweight operations guide for the current repository state.

For architecture and ownership boundaries, see [current-architecture.md](current-architecture.md).
For test strategy and behavior-validation entry points, see [testing.md](testing.md).

## Environment

Required:

- Windows
- Node.js 18+
- NapCat installed and logged in
- .NET 8 SDK for local desktop builds

Optional:

- WeChat bridge or the local simulator

Default local endpoints:

- NapCat: `ws://127.0.0.1:3001`
- Control API: `http://127.0.0.1:3199`
- WeChat bridge simulator: `ws://127.0.0.1:3198`

## Setup

Install dependencies and create local config:

```powershell
npm install
Copy-Item .env.example .env
```

Main env keys:

```env
OPENAI_API_KEY=
OPENAI_DEFAULT_MODEL=gpt-5.4
OPENAI_ADVANCED_MODEL=gpt-5.4
NAPCAT_WS_URL=ws://127.0.0.1:3001
NAPCAT_TOKEN=
WECHAT_BRIDGE_URL=
WECHAT_BRIDGE_TOKEN=
WECHAT_BOT_PREFIX=/ai
BOT_PREFIX=/ai
QQ_AI_BOT_CONTROL_API_TOKEN=
```

## Run

Run full regression:

```powershell
npm test
```

Run backend tests only:

```powershell
npm run test:node
```

Run desktop regression only:

```powershell
npm run test:desktop
```

Start supervisor:

```powershell
npm start
```

Start desktop control plane:

```powershell
dotnet run --project .\desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj
```

## Desktop Validation

Desktop acceptance scripts:

```powershell
npm run desktop:acceptance
npm run desktop:acceptance:auto
```

Validate-only mode:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\desktop-acceptance.ps1 -ValidateOnly
```

## WeChat Simulator

Run the local bridge simulator:

```powershell
npm run wechat:bridge:sim
```

Typical env:

```env
WECHAT_BRIDGE_URL=ws://127.0.0.1:3198
WECHAT_BRIDGE_TOKEN=
WECHAT_BOT_PREFIX=/ai
```

The simulator also exposes:

- `GET /health`
- `POST /emit-test-message`
- `GET /actions`

## Install / Uninstall

Install to the current user profile:

```powershell
npm run setup:install
```

Validate install prerequisites only:

```powershell
npm run setup:validate
```

Uninstall:

```powershell
npm run setup:uninstall
```

Uninstall but keep state:

```powershell
npm run setup:uninstall:keep-state
```

## Release

Source package:

```powershell
npm run release:source
```

Installable prebuilt package:

```powershell
npm run release:installable
```

Installable package smoke check:

```powershell
npm run release:installable:smoke
```

Installer package:

```powershell
npm run release:installer
```

Installer validate-only:

```powershell
npm run release:installer:validate
```

## Control API

Main endpoints:

- `GET /status`
- `GET /config`
- `PUT /config`
- `POST /start`
- `POST /stop`

If `QQ_AI_BOT_CONTROL_API_TOKEN` is set, requests must include:

```text
Authorization: Bearer <token>
```

## Source of Truth

When behavior is unclear:

1. read `docs/current-architecture.md`
2. read backend tests in `tests/`
3. read desktop regression in `desktop/QQAIBot.Desktop.Tests/Program.cs`
