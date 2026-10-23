# UnityMCPBridge Compatibility

This document describes the project-side compatibility layer used to surface selected `MCPForUnity` capabilities through `UnityMCPBridge` in an internal Unity project.

## Scope

The compatibility layer is intentionally narrow:

- `MCPForUnity` remains the source of truth for editor capabilities
- `UnityMCPBridge` remains the internal runtime shell
- project-side compatibility code discovers MCPForUnity tools and selectively re-exposes them as Bridge tools
- only a curated subset is exposed, not every MCPForUnity capability

The compatibility layer currently supports three categories:

1. **Sync-compatible**
   These tools execute synchronously and are proxied directly.
2. **Deferred-compatible**
   These tools are allowed to resolve asynchronously through Bridge deferred results.
3. **Resource-compatible**
   These are read-only wrappers for high-value MCPForUnity resources, exposed as Bridge tools until Bridge resource protocol support is expanded.

## Diagnostics

Use `mcpforunity-compat-status` as the canonical diagnostics entry point.

It reports:

- total proxied and skipped counts
- state breakdowns
- compatibility policy
- deferred allowlists
- per-tool metadata such as group, source class, assembly, namespace, and skip reason

If a tool is missing from `/mcp/tools`, check `mcpforunity-compat-status` first.

## Current Categories

### Sync-compatible

Typical examples:

- `manage_editor`
- `manage_scene`
- `manage_asset`
- `manage_gameobject`
- `execute_menu_item`
- `read_console`
- `find_gameobjects`

These should expose useful input schemas and stable `action` enums where practical.

### Deferred-compatible

Current allowlist:

- `refresh_unity`
- `run_tests`

Related sync poll/read tool:

- `get_test_job`

Rules:

- Bridge must not block the Unity main thread while waiting
- Bridge deferred timeout must stay below the Bridge global deferred timeout
- domain reload sensitive flows should prefer Bridge-owned waiting logic over upstream wait loops when necessary

### Resource-compatible

Current Bridge read-only wrappers:

- `mcpforunity-editor-state`
- `mcpforunity-project-info`
- `mcpforunity-selection`

These are explicit Bridge tools, not true MCP resources.

## Tool Acceptance Rules

Every new MCPForUnity capability should be classified as one of the following:

### 1. Sync-compatible

Use this when:

- handler completes synchronously
- tool does not require polling
- tool does not depend on long-running editor workflows

Expected implementation:

- proxy through compat bridge
- add schema overrides if raw metadata is weak

### 2. Deferred-compatible

Use this when:

- handler returns `Task<object>`
- or upstream contract relies on polling / long-running work
- and the workflow is worth supporting inside Bridge

Expected implementation:

- add to compat deferred allowlist
- bridge through `MCPToolResult.Deferred(...)`
- validate timeout and recovery behavior

### 3. Resource-only

Use this when:

- capability is fundamentally read-only context
- it is currently represented as an MCPForUnity resource
- Bridge does not yet support real resources for that capability

Expected implementation:

- create an explicit read-only Bridge tool wrapper
- use `mcpforunity-*` naming to avoid confusion with native Bridge tools

### 4. Not compatible

Use this when:

- tool is too transport-specific
- workflow is unsafe or too noisy inside the internal editor
- or the compatibility layer would become more complex than a direct native Bridge implementation

Expected implementation:

- keep it skipped
- document the reason in `mcpforunity-compat-status`

## Release Checklist

Before syncing a new `MCPForUnity` version into the internal project:

1. Run `mcpforunity-compat-status`
2. Confirm proxied and skipped counts are sensible
3. Verify smoke tools still work:
   - `execute_menu_item`
   - `read_console`
   - `manage_editor`
   - `find_gameobjects`
4. Verify one deferred flow:
   - `refresh_unity`
   - `run_tests` -> `get_test_job`
5. Verify resource wrappers:
   - `mcpforunity-editor-state`
   - `mcpforunity-project-info`
   - `mcpforunity-selection`
6. Inspect `/mcp/tools` and confirm high-value schema overrides still look healthy

## Change Discipline

- Do not silently widen the compat surface
- Prefer explicit allowlists over implicit discovery for async and deferred behavior
- Prefer Bridge-owned schema overrides over leaking weak upstream metadata into clients
- Extract shared Core only after the compatibility layer proves that a capability is genuinely shared and stable
