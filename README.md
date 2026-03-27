# Local AI Runtime Console

This repository packages a local multi-process AI runtime for personal use:

- a Node supervisor
- QQ and WeChat workers
- a local control API
- a WPF desktop control plane

Treat it as:

`single-machine AI runtime + local desktop console`

Do not treat it as "just a QQ bot script". Some environment keys still use legacy `BOT_*` names for compatibility, but the product direction is a local runtime and console, not a one-off bot script.

## Start Here

Recommended reading order:

1. [docs/current-architecture.md](docs/current-architecture.md)
2. `tests/`
3. `desktop/QQAIBot.Desktop.Tests/Program.cs`
4. `src/` and `desktop/QQAIBot.Desktop/`

When README, code, and tests disagree:

- trust code and tests first
- treat [docs/current-architecture.md](docs/current-architecture.md) as the current architecture entry point

## Quick Start

Install dependencies and prepare local config:

```powershell
npm install
Copy-Item .env.example .env
```

Minimum first-run config:

- `OPENAI_API_KEY` or `OPENAI_DEFAULT_API_KEY`
- `NAPCAT_TOKEN`

Run the full regression suite:

```powershell
npm test
```

Start the local runtime supervisor:

```powershell
npm start
```

Start the desktop console:

```powershell
dotnet run --project .\desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj
```

## Current Entry Points

Core runtime and control-plane entry points:

- `src/index.mjs`
- `src/app/runtime-worker.mjs`
- `src/app/wechat-runtime-worker.mjs`
- `src/app/control-api.mjs`
- `desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs`

## Key Scripts

Most-used commands:

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

See [.env.example](.env.example) for the full template.

The most important keys are:

- `OPENAI_API_KEY`
- `OPENAI_DEFAULT_MODEL`
- `OPENAI_ADVANCED_MODEL`
- `OPENAI_ADVANCED_TRIGGER_PREFIXES`
- `NAPCAT_WS_URL`
- `NAPCAT_TOKEN`
- `WECHAT_BRIDGE_URL`
- `WECHAT_BRIDGE_TOKEN`
- `BOT_PREFIX`
- `WECHAT_BOT_PREFIX`
- `QQ_AI_BOT_CONTROL_API_TOKEN`

Compatibility note:

- `BOT_PREFIX`, `WECHAT_BOT_PREFIX`, `BOT_SYSTEM_PROMPT`, and `BOT_PERSONA` keep older names for compatibility.
- In product terms, they configure assistant behavior inside the local AI runtime.

## Install Layout

Default install root:

- `%LOCALAPPDATA%\QQAIBot`

Important paths:

- app files: `%LOCALAPPDATA%\QQAIBot\app`
- config file: `%LOCALAPPDATA%\QQAIBot\app\.env`
- sessions and image cache: `%LOCALAPPDATA%\QQAIBot\app\data\`
- state snapshots: `%LOCALAPPDATA%\QQAIBot\app\artifacts\state-snapshots\`
- desktop activity state: `%LOCALAPPDATA%\QQAIBot.Desktop\activity-state\<hash>.json`

The install scripts print these paths when setup finishes.

## Upgrade And Uninstall

Install or upgrade:

- `npm run setup:install`
- Running the same install script again upgrades in place.
- In-place upgrade replaces program files under `app\`, but keeps `.env`, `data\`, `artifacts\state-snapshots\`, and desktop activity state.

Uninstall:

- `npm run setup:uninstall`
- This removes the current install's app files, config, session data, snapshots, and desktop activity state.

Uninstall but keep state:

- `npm run setup:uninstall:keep-state`
- This removes binaries and shortcuts, but keeps `.env`, `data\`, `artifacts\state-snapshots\`, and desktop activity state so a later reinstall can reconnect to the same local state.

## Docs

Current documentation entry points:

- [docs/current-architecture.md](docs/current-architecture.md)
- [docs/operations.md](docs/operations.md)
- [docs/testing.md](docs/testing.md)
- [docs/review-checklist.md](docs/review-checklist.md)

## Legacy Note

Older versions of this repository mixed product intro, install instructions, runtime operations, and architecture notes into one README. The current direction is to keep README as the current product entry point and move deeper detail into `docs/`.
