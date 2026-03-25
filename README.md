# QQ AI Bot

一个运行在 Windows 本机上的 QQ 群聊 AI 机器人项目。

它现在不是单一 Node 脚本，而是完整的本地控制架构：

- `desktop/QQAIBot.Desktop`
  WPF 桌面控制台，支持单实例、托盘常驻、开机启动、配置编辑
- `src/index.mjs`
  supervisor，负责 control API、worker 生命周期和本地控制面
- `src/app/runtime-worker.mjs`
  runtime worker，负责 NapCat、消息处理、LLM 调用、会话存储
- `data/sessions.json`
  本地 conversation v2 持久化

## 当前能力

- QQ 群消息机器人
- `/ai` 前缀触发和 `@机器人` 触发
- 默认模型 / 高级模型路由
- 图片输入、回复取图、图片上下文延续
- supervisor + worker 运行模型
- 本地 control API
- 配置热重载
- `.env` watcher
- 桌面端托盘常驻
- 单实例桌面端
- 当前用户级开机启动
- worker 自动恢复通知

## 架构概览

```text
Desktop Shell (WPF tray app)
        |
        v
Control API / Supervisor
        |
        v
Runtime Worker
        |
        +-- NapCat adapter
        +-- LLM router / provider
        +-- session store
        +-- config hot reload
```


## 目录结构

```text
.
├─ desktop/
│  └─ QQAIBot.Desktop/
├─ src/
│  ├─ adapters/
│  ├─ app/
│  ├─ domain/
│  ├─ index.mjs
│  ├─ napcat.mjs
│  ├─ openai.mjs
│  └─ session.mjs
├─ data/
├─ .env.example
├─ package.json
└─ README.md
```

## 环境要求

- Windows
- Node.js 18+
- 已安装并登录 NapCat
- NapCat 已启用正向 WebSocket / WebSocket Server
- 如果要编译桌面端，需要 .NET 8 SDK

默认 NapCat 地址：

```text
ws://127.0.0.1:3001
```

## 安装

```powershell
npm install
Copy-Item .env.example .env
```

然后填写 `.env`。

## 关键配置

```env
OPENAI_API_KEY=
OPENAI_MODEL=gpt-5.4
OPENAI_BASE_URL=

OPENAI_DEFAULT_API_KEY=
OPENAI_DEFAULT_MODEL=gpt-5.4
OPENAI_DEFAULT_BASE_URL=
OPENAI_DEFAULT_API_STYLE=responses

OPENAI_ADVANCED_API_KEY=
OPENAI_ADVANCED_MODEL=gpt-5.4
OPENAI_ADVANCED_BASE_URL=
OPENAI_ADVANCED_API_STYLE=responses
OPENAI_ADVANCED_TRIGGER_PREFIXES=/5.4,/gpt,/vision,/高级,/多模态,/看图,/图片分析

NAPCAT_WS_URL=ws://127.0.0.1:3001
NAPCAT_TOKEN=

BOT_PREFIX=/ai
BOT_PERSONA=
MAX_OUTPUT_CHARS=800
ALLOWED_CHAT_IDS=
ALLOWED_USER_IDS=
```

- `OPENAI_DEFAULT_API_KEY` / `OPENAI_DEFAULT_BASE_URL` can be left blank to inherit the shared route.

说明：

- `OPENAI_DEFAULT_*`
  默认文本路由
- `OPENAI_ADVANCED_*`
  高级路由，通常用于 GPT-5.4 / 图片 / 多模态
- `OPENAI_ADVANCED_TRIGGER_PREFIXES`
  命中这些前缀时切到高级路由
- `BOT_PERSONA`
  支持多行，保存时会自动转义为 `\n`
- `ALLOWED_CHAT_IDS` / `ALLOWED_USER_IDS`
  留空表示不过滤

## 启动方式

### 启动 supervisor

```powershell
npm start
```

默认 control API：

```text
http://127.0.0.1:3199
```

### 启动桌面端

```powershell
dotnet run --project .\desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj
```

桌面端特性：

- 单实例
- 最小化到托盘
- 关闭默认隐藏到托盘
- 可以附着已有 supervisor
- 可配置开机启动
- 开机启动时默认 `--minimized --ensure-runtime`

### Desktop 验收脚本

跨进程 desktop 验收与剩余人工检查步骤：

```powershell
npm run desktop:acceptance
```

只跑脚本里的自动化部分：

```powershell
npm run desktop:acceptance:auto
```

只校验路径和文件，不启动任何 GUI 进程：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\desktop-acceptance.ps1 -ValidateOnly
```

### 启动微信桥模拟器

如果你还没有真实个人微信 bridge，可以先跑本地模拟器：

```powershell
npm run wechat:bridge:sim
```

默认地址：

```text
ws://127.0.0.1:3198
```

然后把 `.env` 里的这些值填上：

```env
WECHAT_BRIDGE_URL=ws://127.0.0.1:3198
WECHAT_BRIDGE_TOKEN=
WECHAT_BOT_PREFIX=/ai
```

模拟器还提供两个本地 HTTP 入口：

- `GET /health`
- `POST /emit-test-message`
- `GET /actions`

测试消息注入示例：

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:3198/emit-test-message
```

也可以传 JSON 自定义消息内容：

```powershell
$body = @{
  chatId = "wx_group_demo"
  userId = "wx_user_demo"
  text = "/ai 帮我总结一下今天的任务"
  mentioned = $false
  replyToMessageIds = @()
  images = @()
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri http://127.0.0.1:3198/emit-test-message `
  -ContentType "application/json" `
  -Body $body
```

支持字段：

- `chatId`
- `userId`
- `selfId`
- `text`
- `rawText`
- `mentioned`
- `replyToMessageIds`
- `images`

bridge 收到的 action 会写入当前工作目录下的：

```text
wechat-actions.json
```

你也可以用环境变量改路径：

```env
WECHAT_BRIDGE_SIM_ACTION_LOG=D:\path\to\wechat-actions.json
```

## 发布打包

生成一份干净的源码发布目录和 zip：

```powershell
npm run release:source
```

输出位置：

```text
dist/qq-ai-bot-<version>-<timestamp>/
dist/qq-ai-bot-<version>-<timestamp>.zip
```

打包内容会包含：

- `src`
- `desktop`
- `.env.example`
- `README.md`
- `package.json`
- `package-lock.json`
- `scripts`

不会包含：

- `.env`
- `node_modules`
- `data`
- `dist`
- `NapCat.Shell.Windows.Node`
- 桌面端 `bin/obj`
- 临时日志

## Control API

### `GET /status`

返回当前 supervisor / runtime 状态，包含：

- `runtimeActive`
- `napcatConnected`
- `workerProcessId`
- `activeLockCount`
- `controlApiUrl`

### `GET /config`

读取当前配置视图。

### `PUT /config`

保存配置并立即热生效。

### `POST /start`

启动 runtime worker。

### `POST /stop`

停止 runtime worker，但 supervisor 保持存活。

## 消息路由

当前规则：

- 普通文本：走 `default`
- 命中升级前缀：走 `advanced`
- 有图片输入：强制走 `advanced`

会话按 `channel=<id>|chat=<id>|user=<id>` 隔离。
不同 route 的上下文和 `previousResponseId` 分开保存。

## 会话存储

文件：

```text
data/sessions.json
```

当前 schema：

- `version: 2`
- `conversationId`
- `routes.default`
- `routes.advanced`
- `shared.lastImageRefs`
- `updatedAt`

旧的 `group:user:default` / `group:user:advanced` key 已在 store 层迁移合并。

## 配置热重载

支持两种方式：

- 通过 control API / 桌面端保存配置
- 直接编辑 `.env`

两种方式都会触发运行时配置刷新。

如果 NapCat 连接参数变化，worker 会自动重连。

## 桌面端行为

- 托盘菜单支持打开窗口、启动 runtime、停止 runtime、退出应用
- 轮询 control API，检测 worker 重启和控制面异常
- worker 自动恢复时弹托盘通知
- control API 不可达 / 恢复可达时弹托盘通知

## 日志分类

- `[startup]`
- `[control]`
- `[supervisor]`
- `[runtime]`
- `[napcat]`
- `[message]`
- `[openai]`
- `[session]`
- `[filter]`
- `[lock]`

桌面端会显示后端输出日志。

## 仓库说明

建议不要提交这些内容：

- `.env`
- `node_modules`
- `data`
- `tmp-backend-*.log`
- `desktop/QQAIBot.Desktop/bin`
- `desktop/QQAIBot.Desktop/obj`
- NapCat 运行时数据库和日志

对应忽略规则已经写进 `.gitignore`。

## 当前边界

- control API 默认只监听 `127.0.0.1`
- 开机启动使用的是当前用户 `HKCU\...\Run`
- 桌面端通知依赖桌面壳在运行
- 还没有做安装器 / Windows Service 包装

## 后续优先级

如果继续产品化，建议优先做：

1. 安装包 / 发布脚本
2. Windows Service 或任务计划
3. control API 本地鉴权
4. 发布版托盘图标和资源
