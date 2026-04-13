import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_project_settings import (
    manage_project_settings,
    ALL_ACTIONS,
)


@pytest.fixture
def mock_unity(monkeypatch):
    captured = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok", "data": {}}

    monkeypatch.setattr(
        "services.tools.manage_project_settings.get_unity_instance_from_context",
        AsyncMock(return_value="test-instance"),
    )
    monkeypatch.setattr(
        "services.tools.manage_project_settings.send_with_unity_instance",
        fake_send,
    )
    return captured


def test_all_actions_count():
    assert len(ALL_ACTIONS) == 5
    assert "list_categories" in ALL_ACTIONS
    assert "get_settings" in ALL_ACTIONS
    assert "set_settings" in ALL_ACTIONS
    assert "list_properties" in ALL_ACTIONS
    assert "discover" in ALL_ACTIONS


def test_unknown_action(mock_unity):
    result = asyncio.run(
        manage_project_settings(SimpleNamespace(), action="nonexistent")
    )
    assert result["success"] is False
    assert "Unknown" in result["message"]


def test_list_categories_forwards(mock_unity):
    result = asyncio.run(
        manage_project_settings(SimpleNamespace(), action="list_categories")
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_project_settings"
    assert mock_unity["params"]["action"] == "list_categories"
    # category should not be in params when not provided
    assert "category" not in mock_unity["params"]


def test_get_settings_forwards_category(mock_unity):
    result = asyncio.run(
        manage_project_settings(
            SimpleNamespace(), action="get_settings", category="version_control"
        )
    )
    assert mock_unity["params"]["category"] == "version_control"
    assert mock_unity["params"]["action"] == "get_settings"


def test_set_settings_forwards(mock_unity):
    result = asyncio.run(
        manage_project_settings(
            SimpleNamespace(),
            action="set_settings",
            category="version_control",
            settings={"mode": "Perforce"},
        )
    )
    assert mock_unity["params"]["category"] == "version_control"
    assert mock_unity["params"]["settings"] == {"mode": "Perforce"}


def test_list_properties_forwards(mock_unity):
    result = asyncio.run(
        manage_project_settings(
            SimpleNamespace(), action="list_properties", category="version_control"
        )
    )
    assert mock_unity["params"]["action"] == "list_properties"
    assert mock_unity["params"]["category"] == "version_control"


def test_discover_forwards(mock_unity):
    result = asyncio.run(
        manage_project_settings(
            SimpleNamespace(), action="discover", category="version_control"
        )
    )
    assert mock_unity["params"]["action"] == "discover"
    assert mock_unity["params"]["category"] == "version_control"


def test_none_params_excluded(mock_unity):
    result = asyncio.run(
        manage_project_settings(SimpleNamespace(), action="list_categories")
    )
    params = mock_unity["params"]
    assert "category" not in params
    assert "settings" not in params
