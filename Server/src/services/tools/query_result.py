"""
Server-side tool for querying cached large tool results.

When tools return large responses, they are automatically cached via
``cache_if_large()`` and a summary with a cache key is returned. This tool
lets the AI search, slice, or retrieve the full cached data.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.result_cache import ResultCache, _get_session_key


@mcp_for_unity_tool(
    unity_target=None,
    group=None,
    description=(
        "Query cached tool results by key. Large tool responses are automatically "
        "cached and summarized; use this tool to search, filter, or paginate the "
        "full result. Actions: get (retrieve slice), grep (regex search), "
        "keys (list active cache keys), delete (remove cached entry)."
    ),
    annotations=ToolAnnotations(
        title="Query Result",
        readOnlyHint=True,
        destructiveHint=False,
    ),
)
async def query_result(
    ctx: Context,
    action: Annotated[
        Literal["get", "grep", "keys", "delete"],
        "Action to perform."
    ],
    key: Annotated[str, "Cache key (e.g., 'cr_a1b2c3d4') from a previous tool response."] | None = None,
    pattern: Annotated[str, "Regex pattern for grep action. Case-insensitive."] | None = None,
    path: Annotated[str, "Dot-path into result data (e.g., 'data.nodes', 'results[0].tests')."] | None = None,
    start: Annotated[int, "Start index for slicing list results."] | None = None,
    end: Annotated[int, "End index for slicing list results."] | None = None,
    limit: Annotated[int, "Max items to return (default 50)."] = 50,
) -> dict[str, Any]:

    session_key = await _get_session_key(ctx)
    cache = ResultCache.get_instance()

    if action == "keys":
        entries = cache.list_keys(session_key)
        return {
            "success": True,
            "message": f"{len(entries)} cached result(s).",
            "data": entries,
        }

    if action == "delete":
        if not key:
            return {"success": False, "error": "missing_key", "message": "Parameter 'key' is required for delete."}
        deleted = cache.delete(key, session_key)
        if deleted:
            return {"success": True, "message": f"Cache entry '{key}' deleted."}
        return {"success": False, "error": "not_found", "message": f"Cache entry '{key}' not found."}

    # get / grep both require a key
    if not key:
        return {"success": False, "error": "missing_key", "message": "Parameter 'key' is required."}

    if action == "grep":
        if not pattern:
            return {"success": False, "error": "missing_pattern", "message": "Parameter 'pattern' is required for grep."}
        return cache.query(key, session_key, pattern=pattern, path=path, limit=limit)

    # action == "get"
    return cache.query(key, session_key, path=path, start=start, end=end, limit=limit)
