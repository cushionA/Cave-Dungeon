# claude-mcp CLI 調査 (2026-04-25)

**調査対象**: Wave 3 Phase 22 P22-T1（unity-console-check.sh の MCP backend）
**調査結論**: `claude mcp` サブコマンドは**サーバー管理用**で、特定 MCP ツールを CLI から呼ぶ用途には**直接使えない**。Wave 3 で導入した UniCli 最優先方針を継続する。

## CLI 構造

`claude mcp <subcommand>` は **MCP サーバーの登録・管理**を行う:
- `claude mcp add <name> <url>` — サーバー追加
- `claude mcp add --transport http <name> <url>` — HTTP transport 指定
- `claude mcp add --transport stdio <name> <command>` — ローカル process
- `claude mcp serve` — Claude Code 自身を MCP サーバー化
- `claude mcp add-from-claude-desktop` — Claude Desktop の設定をインポート
- `--scope local|project|user` で設定保存先指定

## 当初の期待との差

**期待**: `claude mcp call unity-mcp read_console --filter Error` のように、登録済 MCP サーバーの特定ツールを CLI から呼べる
**現実**: そのような直接呼び出し subcommand は存在しない (2026-04 時点)
- MCP サーバーは Claude Code セッション内から呼ばれることを前提に設計
- CLI 単独で MCP ツールを叩く用途は未対応

## 代替手段

### 1. UniCli 経由（既存、Phase 22 で採用）✅
- Named Pipe IPC で Editor と直結
- 軽量・最速・ロック競合なし
- **本プロジェクトでは UniCli を最優先 backend として運用継続**

### 2. mcpm / 他の third-party CLI
- [MCP-Club/mcpm](https://github.com/MCP-Club/mcpm) は MCP サーバー管理 CLI（claude-mcp と類似機能）
- 直接 MCP ツールを叩く CLI は依然として該当なし

### 3. Claude Code SDK 経由（Python/TypeScript）
- `@anthropic-ai/sdk` または Python `anthropic` で MCP サーバーに直接接続するスクリプトを書ける
- ただし重量級（Node/Python セッション起動コスト）
- hook で毎回起動するには不向き

## アクション

- [x] 調査結果を本ドキュメントに保存
- [x] **`unity-console-check.sh` は現状維持**（UniCli 最優先 + warning なしフォールバック）
- [ ] 公式 MCP CLI 拡張がリリースされたら本ドキュメントを更新

## FUTURE_TASKS への影響

`docs/wave2-4-blockers.md` の「claude-mcp CLI 整備」項目は、**現時点で技術的解決策がない**ことが確定したため、項目を以下に置換:

> 公式の MCP ツール直接呼び出し CLI は 2026-04 時点で未提供。
> SisterGame では UniCli backend で代替済み。本項目はクローズ。
> 将来 Anthropic 公式が CLI 拡張を提供した場合に再評価する。

## 出典

- [Connect Claude Code to tools via MCP - Claude Code Docs](https://code.claude.com/docs/en/mcp)
- [Claude Code MCP Servers: How to Connect, Configure, and Use Them](https://www.builder.io/blog/claude-code-mcp-servers)
- [Claude CLI: Interact with MCP Servers via Command Line (mcpmarket)](https://mcpmarket.com/server/claude-cli)
- [MCP-Club/mcpm](https://github.com/MCP-Club/mcpm)
