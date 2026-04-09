"""Tests for result_cache module: ResultCache, cache_if_large, query_result tool."""
import asyncio
import json
import time
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from services.result_cache import ResultCache, cache_if_large, _build_summary, _navigate_path, _count_items, _MISSING


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture(autouse=True)
def fresh_cache():
    """Reset the singleton before each test."""
    ResultCache._instance = None
    yield
    ResultCache._instance = None


@pytest.fixture
def cache():
    return ResultCache.get_instance()


@pytest.fixture
def mock_ctx():
    """Minimal Context mock with client_id for session key derivation."""
    ctx = SimpleNamespace(client_id="test-session-1")
    return ctx


# ---------------------------------------------------------------------------
# ResultCache.store / get / delete
# ---------------------------------------------------------------------------

class TestCacheBasics:
    def test_store_and_get(self, cache):
        entry = cache.store({"items": [1, 2, 3]}, session_key="s1", source_tool="test_tool")
        assert entry.key.startswith("cr_")
        assert entry.item_count == 3
        assert entry.total_size_chars > 0

        retrieved = cache.get(entry.key, "s1")
        assert retrieved is not None
        assert retrieved.data == {"items": [1, 2, 3]}

    def test_get_wrong_session(self, cache):
        entry = cache.store({"x": 1}, session_key="s1", source_tool="t")
        assert cache.get(entry.key, "s2") is None

    def test_delete(self, cache):
        entry = cache.store({"x": 1}, session_key="s1", source_tool="t")
        assert cache.delete(entry.key, "s1") is True
        assert cache.get(entry.key, "s1") is None

    def test_delete_nonexistent(self, cache):
        assert cache.delete("cr_nonexistent", "s1") is False

    def test_ttl_expiration(self, cache):
        entry = cache.store({"x": 1}, session_key="s1", source_tool="t", ttl=0.01)
        time.sleep(0.02)
        assert cache.get(entry.key, "s1") is None

    def test_lru_eviction(self, cache, monkeypatch):
        # Store MAX + 1 entries; first should be evicted
        from core.config import config
        monkeypatch.setattr(config, "result_cache_max_entries", 3)
        keys = []
        for i in range(4):
            e = cache.store({"i": i}, session_key="s1", source_tool="t")
            keys.append(e.key)
        # First entry should be evicted
        assert cache.get(keys[0], "s1") is None
        # Last three should exist
        assert cache.get(keys[1], "s1") is not None
        assert cache.get(keys[3], "s1") is not None


# ---------------------------------------------------------------------------
# ResultCache.list_keys
# ---------------------------------------------------------------------------

class TestListKeys:
    def test_list_keys(self, cache):
        cache.store({"a": 1}, session_key="s1", source_tool="tool_a", source_action="act1")
        cache.store({"b": 2}, session_key="s1", source_tool="tool_b")
        keys = cache.list_keys("s1")
        assert len(keys) == 2
        assert keys[0]["source_tool"] == "tool_a"
        assert keys[0]["source_action"] == "act1"

    def test_list_keys_empty(self, cache):
        assert cache.list_keys("s1") == []

    def test_list_keys_filters_expired(self, cache):
        cache.store({"x": 1}, session_key="s1", source_tool="t", ttl=0.01)
        time.sleep(0.02)
        assert cache.list_keys("s1") == []


# ---------------------------------------------------------------------------
# ResultCache.query - get / grep / slice
# ---------------------------------------------------------------------------

class TestQuery:
    def test_query_list_slice(self, cache):
        items = list(range(100))
        entry = cache.store(items, session_key="s1", source_tool="t")
        result = cache.query(entry.key, "s1", start=10, end=15)
        assert result["success"] is True
        assert result["items"] == [10, 11, 12, 13, 14]
        assert result["total_items"] == 100
        assert result["has_more"] is True

    def test_query_grep_list(self, cache):
        items = [{"name": "Camera"}, {"name": "Player"}, {"name": "MainCamera"}]
        entry = cache.store(items, session_key="s1", source_tool="t")
        result = cache.query(entry.key, "s1", pattern="camera")
        assert result["success"] is True
        assert result["match_count"] == 2
        assert result["matches"][0]["index"] == 0
        assert result["matches"][1]["index"] == 2

    def test_query_grep_dict(self, cache):
        data = {"cam": "Camera", "player": "PlayerObj", "light": "DirectionalLight"}
        entry = cache.store(data, session_key="s1", source_tool="t")
        result = cache.query(entry.key, "s1", pattern="light")
        assert result["success"] is True
        assert result["match_count"] == 1

    def test_query_expired(self, cache):
        entry = cache.store({"x": 1}, session_key="s1", source_tool="t", ttl=0.01)
        time.sleep(0.02)
        result = cache.query(entry.key, "s1")
        assert result["success"] is False
        assert result["error"] == "cache_expired"

    def test_query_with_path(self, cache):
        data = {"data": {"nodes": [{"name": "A"}, {"name": "B"}]}}
        entry = cache.store(data, session_key="s1", source_tool="t")
        result = cache.query(entry.key, "s1", path="data.nodes")
        assert result["success"] is True
        assert result["total_items"] == 2

    def test_query_invalid_path(self, cache):
        entry = cache.store({"a": 1}, session_key="s1", source_tool="t")
        result = cache.query(entry.key, "s1", path="nonexistent.field")
        assert result["success"] is False
        assert result["error"] == "path_not_found"

    def test_query_invalid_regex(self, cache):
        entry = cache.store([1, 2], session_key="s1", source_tool="t")
        result = cache.query(entry.key, "s1", pattern="[invalid")
        assert result["success"] is False
        assert result["error"] == "invalid_pattern"

    def test_query_grep_with_limit(self, cache):
        items = [{"v": i} for i in range(100)]
        entry = cache.store(items, session_key="s1", source_tool="t")
        result = cache.query(entry.key, "s1", pattern="v", limit=5)
        assert len(result["matches"]) == 5
        assert result["has_more"] is True


# ---------------------------------------------------------------------------
# _navigate_path
# ---------------------------------------------------------------------------

class TestNavigatePath:
    def test_simple_dot_path(self):
        data = {"a": {"b": {"c": 42}}}
        assert _navigate_path(data, "a.b.c") == 42

    def test_array_index(self):
        data = {"items": [10, 20, 30]}
        assert _navigate_path(data, "items[1]") == 20

    def test_missing_key(self):
        assert _navigate_path({"a": 1}, "b") is _MISSING

    def test_out_of_range(self):
        assert _navigate_path([1, 2], "[5]") is _MISSING


# ---------------------------------------------------------------------------
# _count_items / _build_summary
# ---------------------------------------------------------------------------

class TestHelpers:
    def test_count_items_list(self):
        assert _count_items([1, 2, 3]) == 3

    def test_count_items_dict_with_items_key(self):
        assert _count_items({"data": {"items": [1, 2]}}) == 2

    def test_count_items_none(self):
        assert _count_items("hello") is None

    def test_build_summary_list(self):
        s = _build_summary([1, 2, 3, 4, 5], 2)
        assert s["total_count"] == 5
        assert s["preview"] == [1, 2]

    def test_build_summary_dict(self):
        s = _build_summary({"nodes": [1, 2, 3], "count": 3}, 2)
        assert s["nodes"]["total_count"] == 3
        assert s["nodes"]["preview"] == [1, 2]
        assert s["count"] == 3

    def test_build_summary_long_string(self):
        s = _build_summary("x" * 1000, 3)
        assert s["total_chars"] == 1000
        assert len(s["preview"]) == 500


# ---------------------------------------------------------------------------
# cache_if_large
# ---------------------------------------------------------------------------

class TestCacheIfLarge:
    def test_small_response_passes_through(self):
        resp = {"success": True, "data": {"x": 1}}
        ctx = SimpleNamespace(client_id="s1")
        result = asyncio.run(cache_if_large(resp, ctx, source_tool="t"))
        assert "_cache" not in result
        assert result == resp

    def test_large_response_gets_cached(self):
        # Create response larger than 4000 chars
        large_data = [{"name": f"item_{i}", "value": "x" * 50} for i in range(100)]
        resp = {"success": True, "message": "ok", "data": large_data}
        ctx = SimpleNamespace(client_id="s1")
        result = asyncio.run(cache_if_large(resp, ctx, source_tool="t", source_action="search"))
        assert "_cache" in result
        assert result["_cache"]["key"].startswith("cr_")
        assert result["_cache"]["total_items"] == 100
        assert "preview" in result["data"]

    def test_custom_threshold(self):
        resp = {"success": True, "data": [1, 2, 3]}
        ctx = SimpleNamespace(client_id="s1")
        # Threshold of 10 means this small response WILL be cached
        result = asyncio.run(cache_if_large(resp, ctx, source_tool="t", threshold_chars=10))
        assert "_cache" in result


# ---------------------------------------------------------------------------
# query_result tool
# ---------------------------------------------------------------------------

class TestQueryResultTool:
    def test_keys_action(self):
        from services.tools.query_result import query_result
        ctx = SimpleNamespace(client_id="s1")
        # Store something first
        cache = ResultCache.get_instance()
        cache.store([1, 2, 3], session_key="s1", source_tool="test")
        result = asyncio.run(query_result(ctx, action="keys"))
        assert result["success"] is True
        assert len(result["data"]) == 1

    def test_get_action(self):
        from services.tools.query_result import query_result
        ctx = SimpleNamespace(client_id="s1")
        cache = ResultCache.get_instance()
        entry = cache.store(list(range(50)), session_key="s1", source_tool="test")
        result = asyncio.run(query_result(ctx, action="get", key=entry.key, start=0, end=5))
        assert result["success"] is True
        assert result["items"] == [0, 1, 2, 3, 4]

    def test_grep_action(self):
        from services.tools.query_result import query_result
        ctx = SimpleNamespace(client_id="s1")
        cache = ResultCache.get_instance()
        items = [{"name": "Camera"}, {"name": "Player"}, {"name": "Light"}]
        entry = cache.store(items, session_key="s1", source_tool="test")
        result = asyncio.run(query_result(ctx, action="grep", key=entry.key, pattern="camera"))
        assert result["success"] is True
        assert result["match_count"] == 1

    def test_delete_action(self):
        from services.tools.query_result import query_result
        ctx = SimpleNamespace(client_id="s1")
        cache = ResultCache.get_instance()
        entry = cache.store({"x": 1}, session_key="s1", source_tool="test")
        result = asyncio.run(query_result(ctx, action="delete", key=entry.key))
        assert result["success"] is True
        assert cache.get(entry.key, "s1") is None

    def test_missing_key(self):
        from services.tools.query_result import query_result
        ctx = SimpleNamespace(client_id="s1")
        result = asyncio.run(query_result(ctx, action="get"))
        assert result["success"] is False
        assert "missing_key" in result["error"]

    def test_grep_missing_pattern(self):
        from services.tools.query_result import query_result
        ctx = SimpleNamespace(client_id="s1")
        result = asyncio.run(query_result(ctx, action="grep", key="cr_fake"))
        assert result["success"] is False
        assert "missing_pattern" in result["error"]
