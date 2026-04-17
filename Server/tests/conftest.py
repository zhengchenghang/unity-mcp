"""Pytest configuration for unity-mcp tests."""
import logging
import os
import sys
from pathlib import Path
import pytest

logger = logging.getLogger(__name__)

# Add src directory to Python path so tests can import cli, transport, etc.
src_path = Path(__file__).parent.parent / "src"
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))


@pytest.fixture(autouse=True)
def restore_global_config():
    """Restore global config/env mutations between tests."""
    from core.config import config as global_config

    prior_env = os.environ.get("UNITY_MCP_TRANSPORT")
    prior = {
        "transport_mode": global_config.transport_mode,
        "http_remote_hosted": global_config.http_remote_hosted,
        "api_key_validation_url": global_config.api_key_validation_url,
        "api_key_login_url": global_config.api_key_login_url,
        "api_key_cache_ttl": global_config.api_key_cache_ttl,
        "api_key_service_token_header": global_config.api_key_service_token_header,
        "api_key_service_token": global_config.api_key_service_token,
    }
    yield

    if prior_env is None:
        os.environ.pop("UNITY_MCP_TRANSPORT", None)
    else:
        os.environ["UNITY_MCP_TRANSPORT"] = prior_env

    for key, value in prior.items():
        setattr(global_config, key, value)
