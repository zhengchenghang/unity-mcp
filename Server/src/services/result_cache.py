"""
Result cache for large MCP tool responses.

When a tool returns data exceeding a size threshold, the full result is cached
in memory and a summary + cache key is returned to the AI client. The AI can
then use the ``query_result`` tool to search, slice, or retrieve the full data.
"""
from __future__ import annotations

import json
import logging
import re
import time
import uuid
from dataclasses import dataclass, field
from threading import RLock
from typing import Any, ClassVar

from fastmcp import Context

from core.config import config

logger = logging.getLogger("mcp-for-unity-server")


@dataclass
class CacheEntry:
    """A single cached tool result."""
    key: str
    data: Any
    created_at: float
    ttl: float
    source_tool: str
    source_action: str | None
    session_key: str
    item_count: int | None
    total_size_chars: int


class ResultCache:
    """TTL-based in-memory cache for large tool results, scoped by session."""

    _instance: ClassVar[ResultCache | None] = None
    _instance_lock: ClassVar[RLock] = RLock()

    MAX_ENTRIES_PER_SESSION = 50

    @classmethod
    def get_instance(cls) -> ResultCache:
        if cls._instance is None:
            with cls._instance_lock:
                if cls._instance is None:
                    cls._instance = cls()
        return cls._instance

    def __init__(self) -> None:
        # session_key -> {cache_key -> CacheEntry}
        self._store: dict[str, dict[str, CacheEntry]] = {}
        self._lock = RLock()

    # ------------------------------------------------------------------
    # Core CRUD
    # ------------------------------------------------------------------

    def store(
        self,
        data: Any,
        *,
        session_key: str,
        source_tool: str,
        source_action: str | None = None,
        ttl: float | None = None,
    ) -> CacheEntry:
        """Cache *data* and return the new entry."""
        if ttl is None:
            ttl = getattr(config, "result_cache_ttl", 300.0)

        cache_key = f"cr_{uuid.uuid4().hex[:8]}"

        # Estimate size + item count
        try:
            serialized = json.dumps(data, ensure_ascii=False, default=str)
            total_chars = len(serialized)
        except (TypeError, ValueError):
            total_chars = 0

        item_count = _count_items(data)

        entry = CacheEntry(
            key=cache_key,
            data=data,
            created_at=time.monotonic(),
            ttl=ttl,
            source_tool=source_tool,
            source_action=source_action,
            session_key=session_key,
            item_count=item_count,
            total_size_chars=total_chars,
        )

        max_entries = getattr(config, "result_cache_max_entries", self.MAX_ENTRIES_PER_SESSION)

        with self._lock:
            bucket = self._store.setdefault(session_key, {})
            # Evict expired first
            self._evict_expired_locked(session_key)
            # LRU eviction if at capacity
            while len(bucket) >= max_entries:
                oldest_key = next(iter(bucket))
                del bucket[oldest_key]
            bucket[cache_key] = entry

        logger.debug("Cached result %s (%d chars, %s items) from %s/%s",
                      cache_key, total_chars, item_count, source_tool, source_action)
        return entry

    def get(self, key: str, session_key: str) -> CacheEntry | None:
        with self._lock:
            bucket = self._store.get(session_key)
            if not bucket:
                return None
            entry = bucket.get(key)
            if entry is None:
                return None
            if self._is_expired(entry):
                del bucket[key]
                return None
            return entry

    def delete(self, key: str, session_key: str) -> bool:
        with self._lock:
            bucket = self._store.get(session_key)
            if bucket and key in bucket:
                del bucket[key]
                return True
        return False

    def list_keys(self, session_key: str) -> list[dict[str, Any]]:
        now = time.monotonic()
        result = []
        with self._lock:
            self._evict_expired_locked(session_key)
            bucket = self._store.get(session_key, {})
            for entry in bucket.values():
                age = now - entry.created_at
                remaining = max(0.0, entry.ttl - age)
                result.append({
                    "key": entry.key,
                    "source_tool": entry.source_tool,
                    "source_action": entry.source_action,
                    "item_count": entry.item_count,
                    "total_size_chars": entry.total_size_chars,
                    "age_seconds": round(age, 1),
                    "ttl_remaining_seconds": round(remaining, 1),
                })
        return result

    # ------------------------------------------------------------------
    # Query / grep / slice
    # ------------------------------------------------------------------

    def query(
        self,
        key: str,
        session_key: str,
        *,
        pattern: str | None = None,
        path: str | None = None,
        start: int | None = None,
        end: int | None = None,
        limit: int = 50,
    ) -> dict[str, Any]:
        """Search, navigate, or slice a cached result."""
        entry = self.get(key, session_key)
        if entry is None:
            return {
                "success": False,
                "error": "cache_expired",
                "message": f"Cache entry '{key}' not found or expired. Re-run the original tool.",
            }

        target = entry.data

        # Navigate into nested path (e.g. "data.nodes")
        if path:
            target = _navigate_path(target, path)
            if target is _MISSING:
                return {
                    "success": False,
                    "error": "path_not_found",
                    "message": f"Path '{path}' not found in cached data.",
                }

        # Grep: regex search across items
        if pattern:
            return self._grep(target, pattern, limit)

        # Slice list data
        if isinstance(target, list):
            total = len(target)
            s = start or 0
            e = end if end is not None else s + limit
            sliced = target[s:e]
            return {
                "success": True,
                "total_items": total,
                "start": s,
                "end": min(e, total),
                "has_more": e < total,
                "items": sliced,
            }

        # Return dict/scalar directly (with size info)
        return {
            "success": True,
            "data": target,
        }

    def _grep(self, data: Any, pattern: str, limit: int) -> dict[str, Any]:
        """Regex search across items in data."""
        try:
            regex = re.compile(pattern, re.IGNORECASE)
        except re.error as e:
            return {"success": False, "error": "invalid_pattern", "message": str(e)}

        matches: list[dict[str, Any]] = []

        if isinstance(data, list):
            for i, item in enumerate(data):
                if len(matches) >= limit:
                    break
                text = json.dumps(item, ensure_ascii=False, default=str) if not isinstance(item, str) else item
                if regex.search(text):
                    matches.append({"index": i, "item": item})
            return {
                "success": True,
                "pattern": pattern,
                "total_searched": len(data),
                "match_count": len(matches),
                "has_more": len(matches) >= limit,
                "matches": matches,
            }

        if isinstance(data, dict):
            for k, v in data.items():
                if len(matches) >= limit:
                    break
                text = json.dumps({k: v}, ensure_ascii=False, default=str)
                if regex.search(text):
                    matches.append({"key": k, "value": v})
            return {
                "success": True,
                "pattern": pattern,
                "total_searched": len(data),
                "match_count": len(matches),
                "has_more": len(matches) >= limit,
                "matches": matches,
            }

        # Scalar: just check if it matches
        text = str(data)
        found = bool(regex.search(text))
        return {"success": True, "pattern": pattern, "matched": found, "data": data if found else None}

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _is_expired(self, entry: CacheEntry) -> bool:
        return (time.monotonic() - entry.created_at) > entry.ttl

    def _evict_expired_locked(self, session_key: str) -> int:
        """Remove expired entries. Must hold self._lock."""
        bucket = self._store.get(session_key)
        if not bucket:
            return 0
        expired = [k for k, e in bucket.items() if self._is_expired(e)]
        for k in expired:
            del bucket[k]
        return len(expired)


# ======================================================================
# cache_if_large: the opt-in helper tools call at their return site
# ======================================================================

async def cache_if_large(
    response: dict[str, Any],
    ctx: Context,
    *,
    source_tool: str,
    source_action: str | None = None,
    threshold_chars: int = 4000,
    summary_items: int = 3,
    ttl: float | None = None,
) -> dict[str, Any]:
    """If *response* exceeds *threshold_chars*, cache it and return a summary.

    Otherwise return *response* unchanged. Call this at a tool's return site:

        return await cache_if_large(friendly, ctx, source_tool="manage_scene", source_action="get_hierarchy")
    """
    # Measure serialized size
    try:
        serialized = json.dumps(response, ensure_ascii=False, default=str)
    except (TypeError, ValueError):
        return response
    if len(serialized) < threshold_chars:
        return response

    # Get session key for cache scoping
    session_key = await _get_session_key(ctx)

    cache = ResultCache.get_instance()
    entry = cache.store(
        response,
        session_key=session_key,
        source_tool=source_tool,
        source_action=source_action,
        ttl=ttl,
    )

    # Build summary
    summary_data = _build_summary(response.get("data"), summary_items)
    cache_meta = {
        "key": entry.key,
        "total_items": entry.item_count,
        "total_chars": entry.total_size_chars,
        "preview_items": summary_items,
        "ttl_seconds": entry.ttl,
        "hint": (
            f"Use query_result tool to search or paginate. "
            f"Example: query_result(key='{entry.key}', pattern='keyword') "
            f"or query_result(key='{entry.key}', start=0, end=20)"
        ),
    }

    # Compose the summarized response
    msg = response.get("message", "")
    if entry.item_count is not None:
        msg = f"{msg} ({entry.item_count} items cached)"
    else:
        msg = f"{msg} (result cached, {entry.total_size_chars} chars)"

    return {
        "success": response.get("success", True),
        "message": msg,
        "data": summary_data,
        "_cache": cache_meta,
    }


# ======================================================================
# Private helpers
# ======================================================================

_MISSING = object()


def _navigate_path(data: Any, path: str) -> Any:
    """Navigate into nested data using dot-separated path.

    Supports array indexing: "data.nodes[0].children".
    """
    parts = re.split(r"\.|\[(\d+)\]", path)
    current = data
    for part in parts:
        if part is None or part == "":
            continue
        if part.isdigit():
            idx = int(part)
            if isinstance(current, list) and 0 <= idx < len(current):
                current = current[idx]
            else:
                return _MISSING
        elif isinstance(current, dict) and part in current:
            current = current[part]
        else:
            return _MISSING
    return current


def _count_items(data: Any) -> int | None:
    """Heuristic item count for the data."""
    if isinstance(data, list):
        return len(data)
    if isinstance(data, dict):
        # Look for common collection keys
        d = data.get("data", data)
        if isinstance(d, dict):
            for key in ("items", "nodes", "results", "matches", "entries", "tests", "events"):
                if key in d and isinstance(d[key], list):
                    return len(d[key])
        if isinstance(d, list):
            return len(d)
    return None


def _build_summary(data: Any, max_items: int) -> Any:
    """Create a compact preview of the data."""
    if data is None:
        return None

    if isinstance(data, list):
        return {
            "total_count": len(data),
            "preview": data[:max_items],
        }

    if isinstance(data, dict):
        summary: dict[str, Any] = {}
        for k, v in data.items():
            if isinstance(v, list):
                summary[k] = {
                    "total_count": len(v),
                    "preview": v[:max_items],
                }
            elif isinstance(v, dict):
                # Show keys only for nested dicts
                summary[k] = {"keys": list(v.keys())[:10]}
            else:
                summary[k] = v
        return summary

    # Scalar/string: truncate
    text = str(data)
    if len(text) > 500:
        return {"preview": text[:500], "total_chars": len(text)}
    return data


async def _get_session_key(ctx: Context) -> str:
    """Derive session key from context, matching UnityInstanceMiddleware logic."""
    client_id = getattr(ctx, "client_id", None)
    if isinstance(client_id, str) and client_id:
        return client_id

    try:
        user_id = await ctx.get_state("user_id")
        if isinstance(user_id, str) and user_id:
            return f"user:{user_id}"
    except Exception:
        pass

    return "global"
