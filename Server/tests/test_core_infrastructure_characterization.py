"""
Characterization tests for Core Infrastructure domain (logging, config).

These tests capture the CURRENT behavior of the Core Infrastructure without refactoring.
They document decorator patterns, logging flows, and configuration handling.

To run:
    cd Server && uv run pytest tests/test_core_infrastructure_characterization.py -v
"""

import asyncio
import logging
import os
import sys
from pathlib import Path

import pytest

# Set up sys.path for imports
SERVER_ROOT = Path(__file__).resolve().parents[1]
SERVER_SRC = SERVER_ROOT / "src"
if str(SERVER_ROOT) not in sys.path:
    sys.path.insert(0, str(SERVER_ROOT))
if str(SERVER_SRC) not in sys.path:
    sys.path.insert(0, str(SERVER_SRC))

from core.logging_decorator import log_execution
from core.config import ServerConfig


# =============================================================================
# Fixtures
# =============================================================================

@pytest.fixture
def caplog_fixture(caplog):
    """Fixture to capture and configure logging."""
    caplog.set_level(logging.DEBUG)
    return caplog


# =============================================================================
# SECTION 1: Logging Decorator Tests
# =============================================================================

class TestLoggingDecoratorBasics:
    """Tests for log_execution decorator basic behavior."""

    def test_decorator_logs_function_call_sync(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("test_func", "TestType")
        def sync_function(x, y):
            return x + y

        result = sync_function(1, 2)

        assert result == 3
        assert "TestType 'test_func' called with args=(1, 2) kwargs={}" in caplog_fixture.text
        assert "TestType 'test_func' returned: 3" in caplog_fixture.text

    def test_decorator_logs_function_call_async(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("async_func", "AsyncType")
        async def async_function(x, y):
            return x + y

        result = asyncio.run(async_function(10, 20))

        assert result == 30
        assert "AsyncType 'async_func' called with args=(10, 20) kwargs={}" in caplog_fixture.text
        assert "AsyncType 'async_func' returned: 30" in caplog_fixture.text

    def test_decorator_logs_kwargs(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("kwarg_func", "KwargType")
        def func_with_kwargs(a, b=None, c=None):
            return (a, b, c)

        result = func_with_kwargs(1, b=2, c=3)

        assert result == (1, 2, 3)
        assert "'b': 2" in caplog_fixture.text
        assert "'c': 3" in caplog_fixture.text

    def test_decorator_logs_exception(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("error_func", "ErrorType")
        def func_that_raises():
            raise ValueError("Test error")

        with pytest.raises(ValueError, match="Test error"):
            func_that_raises()

        assert "ErrorType 'error_func' failed: Test error" in caplog_fixture.text

    def test_decorator_preserves_function_metadata(self):
        @log_execution("metadata_func", "MetaType")
        def original_func():
            """Original docstring."""
            pass

        assert original_func.__name__ == "original_func"
        assert "Original docstring" in original_func.__doc__

    def test_decorator_sync_wrapper_selection(self):
        @log_execution("sync_test", "SyncTest")
        def is_sync():
            return "sync"

        result = is_sync()
        assert result == "sync"

    def test_decorator_async_wrapper_selection(self):
        @log_execution("async_test", "AsyncTest")
        async def is_async():
            return "async"

        assert asyncio.iscoroutinefunction(is_async)
        result = asyncio.run(is_async())
        assert result == "async"


class TestLoggingDecoratorExceptionHandling:
    """Tests for exception handling in logging decorator."""

    def test_decorator_exception_reraised_sync(self):
        @log_execution("error_test", "ErrorTest")
        def failing_func():
            raise RuntimeError("Original error")

        with pytest.raises(RuntimeError, match="Original error"):
            failing_func()

    def test_decorator_exception_reraised_async(self):
        @log_execution("async_error", "AsyncError")
        async def async_failing_func():
            raise RuntimeError("Async original error")

        with pytest.raises(RuntimeError, match="Async original error"):
            asyncio.run(async_failing_func())

    def test_decorator_logs_exception_message(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("exc_msg", "ExcMsg")
        def func_with_message():
            raise ValueError("Specific error details")

        with pytest.raises(ValueError):
            func_with_message()

        assert "Specific error details" in caplog_fixture.text

    def test_decorator_logs_any_exception_type(self, caplog_fixture):
        caplog_fixture.clear()

        class CustomError(Exception):
            pass

        @log_execution("any_exc", "AnyExc")
        def func_raises_custom():
            raise CustomError("Custom")

        with pytest.raises(CustomError):
            func_raises_custom()

        assert "Custom" in caplog_fixture.text


class TestLoggingDecoratorComplex:
    """Tests for complex decorator usage patterns."""

    def test_decorator_stacking_with_multiple_decorators(self, caplog_fixture):
        caplog_fixture.clear()

        def other_decorator(f):
            def wrapper(*args, **kwargs):
                return f(*args, **kwargs)
            return wrapper

        @other_decorator
        @log_execution("stacked", "Stacked")
        def decorated_twice(x):
            return x * 2

        result = decorated_twice(5)

        assert result == 10
        assert "Stacked 'stacked'" in caplog_fixture.text

    def test_decorator_with_class_methods(self, caplog_fixture):
        caplog_fixture.clear()

        class TestClass:
            @log_execution("method", "Method")
            def method(self, value):
                return value * 2

        obj = TestClass()
        result = obj.method(5)

        assert result == 10
        assert "method" in caplog_fixture.text
        assert "10" in caplog_fixture.text

    def test_decorator_with_many_arguments(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("many_args", "ManyArgs")
        def func_many_args(a, b, c, d, e=5, f=6, g=7):
            return sum([a, b, c, d, e, f, g])

        result = func_many_args(1, 2, 3, 4, e=5, f=6, g=7)

        assert result == 28
        assert "many_args" in caplog_fixture.text
        assert "28" in caplog_fixture.text


# =============================================================================
# SECTION 2: Configuration Tests
# =============================================================================

class TestServerConfigDefaults:
    """Tests for ServerConfig default values."""

    def test_config_default_values(self):
        config = ServerConfig()

        assert config.unity_host == "127.0.0.1"
        assert config.unity_port == 6400
        assert config.mcp_port == 6500
        assert config.connection_timeout == 30.0
        assert config.buffer_size == 16 * 1024 * 1024
        assert config.require_framing is True
        assert config.handshake_timeout == 1.0
        assert config.framed_receive_timeout == 2.0
        assert config.max_heartbeat_frames == 16
        assert config.heartbeat_timeout == 2.0

    def test_config_logging_defaults(self):
        config = ServerConfig()

        assert config.log_level == "INFO"
        assert "%(asctime)s" in config.log_format
        assert "%(name)s" in config.log_format
        assert "%(levelname)s" in config.log_format
        assert "%(message)s" in config.log_format

    def test_config_server_defaults(self):
        config = ServerConfig()

        assert config.max_retries == 5
        assert config.retry_delay == 0.25
        assert config.reload_retry_ms == 250
        assert config.reload_max_retries == 40
        assert config.port_registry_ttl == 5.0

    def test_config_is_dataclass(self):
        from dataclasses import is_dataclass
        assert is_dataclass(ServerConfig)


class TestHttpDefaultHostFallbacks:
    """Tests for HTTP host/URL defaults in main.py argument parsing."""

    @staticmethod
    def _build_parser():
        import argparse
        parser = argparse.ArgumentParser()
        parser.add_argument("--http-url", type=str, default="http://127.0.0.1:8080")
        parser.add_argument("--http-host", type=str, default=None)
        parser.add_argument("--http-port", type=int, default=None)
        return parser

    def test_default_http_url_uses_127_0_0_1(self):
        from urllib.parse import urlparse
        args = self._build_parser().parse_args([])
        parsed = urlparse(args.http_url)
        assert parsed.hostname == "127.0.0.1"
        assert parsed.port == 8080

    def test_explicit_localhost_url_is_honored(self):
        from urllib.parse import urlparse
        args = self._build_parser().parse_args(["--http-url", "http://localhost:8080"])
        parsed = urlparse(args.http_url)
        assert parsed.hostname == "localhost"

    def test_host_fallback_without_env(self, monkeypatch):
        from urllib.parse import urlparse
        for key in ("UNITY_MCP_HTTP_URL", "UNITY_MCP_HTTP_HOST", "UNITY_MCP_HTTP_PORT"):
            monkeypatch.delenv(key, raising=False)
        args = self._build_parser().parse_args([])
        http_host = (
            args.http_host
            or os.environ.get("UNITY_MCP_HTTP_HOST")
            or urlparse(args.http_url).hostname
            or "127.0.0.1"
        )
        assert http_host == "127.0.0.1"

    def test_env_host_override_is_honored(self, monkeypatch):
        monkeypatch.setenv("UNITY_MCP_HTTP_HOST", "localhost")
        args = self._build_parser().parse_args([])
        http_host = (
            args.http_host
            or os.environ.get("UNITY_MCP_HTTP_HOST")
            or "127.0.0.1"
        )
        assert http_host == "localhost"


class TestServerConfigLogging:
    """Tests documenting that ServerConfig.configure_logging() was removed."""

    def test_configure_logging_method_removed(self):
        config = ServerConfig()
        assert not hasattr(config, "configure_logging")

    def test_configure_logging_info_level_removed(self):
        config = ServerConfig(log_level="INFO")
        assert not hasattr(config, "configure_logging")
        assert config.log_level == "INFO"

    def test_configure_logging_debug_level_removed(self):
        config = ServerConfig(log_level="DEBUG")
        assert not hasattr(config, "configure_logging")
        assert config.log_level == "DEBUG"


# =============================================================================
# SECTION 3: Error Handling and Edge Cases
# =============================================================================

class TestErrorHandlingEdgeCases:
    """Tests for edge cases and error handling."""

    def test_decorator_with_none_return_value(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("none_func", "None")
        def returns_none():
            return None

        result = returns_none()

        assert result is None
        assert "None" in caplog_fixture.text or "returned" in caplog_fixture.text

    def test_decorator_with_empty_string_return(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("empty_func", "Empty")
        def returns_empty():
            return ""

        result = returns_empty()

        assert result == ""
        assert "Empty" in caplog_fixture.text

    def test_decorator_with_complex_nested_exceptions(self, caplog_fixture):
        caplog_fixture.clear()

        @log_execution("nested_error", "Nested")
        def nested_error():
            try:
                raise ValueError("Inner error")
            except ValueError as e:
                raise RuntimeError("Outer error") from e

        with pytest.raises(RuntimeError, match="Outer error"):
            nested_error()

        assert "Outer error" in caplog_fixture.text


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
