---
name: update-unity-mcp
description: 更新本仓库（unity-mcp fork）到上游 CoplayDev beta 最新版本并合入 main。当用户说"更新 beta 分支"、"同步上游"、"更新 MCP 版本"、"合并上游 beta"或提到要拉取 CoplayDev/unity-mcp 的最新代码时使用。覆盖：分支关系判断、stash、fast-forward、合并冲突解决、合并后检查项修复、测试验证、推送。
---

# 更新 unity-mcp 到上游 beta 最新版

本仓库是 CoplayDev/unity-mcp 的 fork，main 上有本地定制（遥测剥离、manage_input、query_result、CompatCore 等）。
上游 beta 每周发版，更新 = 「上游 beta → 本地 beta → main」三步走。

## 分支与远端关系（先盘点再动手）

```
origin  = git@github.com:zhengchenghang/unity-mcp.git   # 个人 fork（beta 是 PR 目标分支）
comp    = git@platgit.mihoyo.com:...                     # 内部镜像
上游    = https://github.com/CoplayDev/unity-mcp.git     # 没有配置为 remote，按需临时 fetch
```

- 本地 `beta` / `origin/beta`：指向上游某个历史版本
- `main`：包含 beta + 本地定制提交，**PR 永远从 beta 开**（CLAUDE.md 规定不要直接提交 main）

## 步骤

### 1. 拉上游 beta 并判断更新范围

```bash
# 临时 fetch 上游（不建 remote）
git fetch https://github.com/CoplayDev/unity-mcp.git beta:refs/remotes/upstream-beta-tmp
git log --oneline origin/beta..refs/remotes/upstream-beta-tmp   # 有输出 = 需要更新
```

关键判断：`git rev-list --count refs/remotes/upstream-beta-tmp..beta` 为 0 → 本地 beta 是上游祖先，可 **fast-forward**；非 0 → 有本地独有提交，需真合并（询问用户再动）。

### 2. stash 工作区未提交修改

```bash
git stash push -u -m "local-changes-before-beta-merge" -- <明确列出的文件>
```

- 用 `git status -s` 先看清要 stash 什么，**逐个列文件**，不要 `git stash -u` 全收
- 仓库根的 `unity-mcp.7z` 之类打包产物不进 stash，留着即可

### 3. fast-forward beta 并推送

```bash
git checkout beta && git merge --ff-only refs/remotes/upstream-beta-tmp
git push origin +refs/remotes/upstream-beta-tmp:beta   # fork 的 beta 落后时需强推
git branch -D -r upstream-beta-tmp                     # 清理临时 ref
```

⚠️ 强推 origin/beta 会丢弃 fork 上独有的提交——本 fork 的版本号 bump 提交（`chore: update Unity package to beta version ...`）是 CI 产物，可丢弃；其他情况先停下来问用户。

### 4. 合入 main 并解冲突

```bash
git checkout main && git merge beta
```

冲突解决原则（按文件类型）：

| 冲突文件 | 原则 |
|---|---|
| 上游重构过的实现（如 ManageScene 截图） | **取 beta 侧**，确认本地修复是否已包含在 merged 结果里再定 |
| `package.json` 版本号 | 取 beta（上游版本号） |
| **`test_core_infrastructure_characterization.py`** | **必须取 main 侧（剥离版）**——main 的 7f968ec1 已删遥测，beta 侧测试会 import 已删除的 `core.telemetry_decorator`。只吸收 beta 的独立改进（monkeypatch 清 env、新超时默认值断言） |
| SKILL.md / tools-reference.md | 双侧章节都保留（本地 Project Settings 章 + 上游新章），只删标记行 |

### 5. 修复合并带入的检查项（上游测试会咬本地工具）

合并后跑 `cd Server && uv run pytest tests/ --ignore=tests/e2e --ignore=tests/integration -q`，典型失败与修法：

1. **`test_manifest_tools`**（manifest 未列注册工具）→ 向仓库根 `manifest.json` 的 `tools` 数组补 `{"name": ..., "description": ...}` 条目（描述从各工具文件的 `@mcp_for_unity_tool(description=...)` 提取首句）
2. **`test_tool_annotations`**：
   - `destructiveHint not stated` → 给该工具的 `ToolAnnotations(...)` 补 `destructiveHint=False`
   - `read-only tool set changed` → 把工具加进测试文件 `READ_ONLY` 集合，附一行注释说明为何只读
3. **`test_skill_copies_in_sync`** → `cp .claude/skills/unity-mcp-skill/<差异文件> unity-mcp-skill/<同路径>`（installed 副本是权威版）
4. **假失败排查**：测试报奇怪的环境错误（如 click `stderr not separately captured`）→ 先 `uv sync --extra dev`，确认 pytest 从 `.venv` 而非系统 Anaconda 兜底再下结论

### 6. 恢复 stash

```bash
git stash pop
```

- `Server/uv.lock` 冲突：`mcpforunityserver` 的 `version` 取**上游侧**（stash 里是旧版本号），改完 `uv lock --check` + `uv sync --extra dev` 验证
- 解完 `git add`，stash drop 之前确认工作区改动都回来了

### 7. 验证与收尾

```bash
cd Server && uv run pytest tests/ --ignore=tests/e2e --ignore=tests/integration -q   # 应全绿
```

- 临时 worktree / ref 清理干净（`git worktree list`、`git branch -D -r upstream-beta-tmp`）
- 提交序列：merge commit → 修复检查项 → 恢复的本地改动，每个独立提交
- **推送前问用户**：origin/main 与 comp/main 是否都要推、comp 用快进还是覆盖

## 数据泄露复查（每次更新后）

上游可能带回遥测/外发代码，合并后快速复查（上一次 2026-10 复查结论：干净）：

1. `TelemetryHelper.cs`（C#）和 `Server/src/core/telemetry.py` 的状态——main 的 7f968ec1 剥离过遥测，确认没被 beta 恢复（`telemetry.py` 只剩 `get_package_version()` 是正常的）
2. grep `posthog|sentry|analytics|segment` 双侧
3. 出站面默认应全本地：`127.0.0.1:8080`（MCP 桥）、Unity socket、Blender `127.0.0.1:9876`
4. 已知外发点（均为显式触发，不算泄露）：编辑器窗口每日一次版本号 GET（GitHub raw/supabase）、`unity_docs` 查词到 docs.unity3d.com、AssetGen 组上传 prompt/图片到 AI 服务（默认双重关闭）、可选 API key 验证 URL（默认关）
5. 注意 `McpLogRecord` 默认关闭，开启会把完整工具参数明文写进 `Assets/UnityMCP/Log/`

发现上游带回遥测 → 按主干剥离方式重做：删文件、清 import、测试取剥离版。
