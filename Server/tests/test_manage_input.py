"""Tests for manage_input MCP tool."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_input import manage_input, ALL_ACTIONS


@pytest.fixture
def mock_unity(monkeypatch):
    """Mock Unity communication, capture params sent to Unity."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_input.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_input.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── Action validation ──────────────────────────────────────


def test_all_actions_are_lowercase():
    for action in ALL_ACTIONS:
        assert action == action.lower(), f"Action '{action}' should be lowercase"


# ── Keyboard tests ─────────────────────────────────────────


def test_key_press_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="key_press", key="w", duration_ms=200)
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_input"
    assert mock_unity["params"]["action"] == "key_press"
    assert mock_unity["params"]["key"] == "w"
    assert mock_unity["params"]["duration_ms"] == 200


def test_key_down_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="key_down", key="space")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "key_down"
    assert mock_unity["params"]["key"] == "space"


def test_key_up_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="key_up", key="leftShift")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "key_up"
    assert mock_unity["params"]["key"] == "leftShift"


# ── Mouse tests ────────────────────────────────────────────


def test_mouse_move_forwards_position(mock_unity):
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="mouse_move", position=[100.0, 200.0])
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "mouse_move"
    assert mock_unity["params"]["position"] == [100.0, 200.0]


def test_mouse_click_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(
            SimpleNamespace(),
            action="mouse_click",
            button="right",
            position=[50.0, 60.0],
            duration_ms=100,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["button"] == "right"
    assert mock_unity["params"]["position"] == [50.0, 60.0]
    assert mock_unity["params"]["duration_ms"] == 100


def test_mouse_scroll_forwards_delta(mock_unity):
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="mouse_scroll", delta=[0.0, 120.0])
    )
    assert result["success"] is True
    assert mock_unity["params"]["delta"] == [0.0, 120.0]


# ── Gamepad tests ──────────────────────────────────────────


def test_gamepad_button_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(
            SimpleNamespace(), action="gamepad_button", button="south", duration_ms=50
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["button"] == "south"
    assert mock_unity["params"]["duration_ms"] == 50


def test_gamepad_stick_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(
            SimpleNamespace(), action="gamepad_stick", stick="left", value=[0.5, 1.0]
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["stick"] == "left"
    assert mock_unity["params"]["value"] == [0.5, 1.0]


def test_gamepad_trigger_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(
            SimpleNamespace(), action="gamepad_trigger", trigger="right", value=0.8
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["trigger"] == "right"
    assert mock_unity["params"]["value"] == 0.8


# ── Touch tests ────────────────────────────────────────────


def test_touch_tap_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(
            SimpleNamespace(),
            action="touch_tap",
            position=[300.0, 400.0],
            touch_id=1,
            duration_ms=0,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "touch_tap"
    assert mock_unity["params"]["position"] == [300.0, 400.0]
    assert mock_unity["params"]["touch_id"] == 1


def test_touch_down_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(
            SimpleNamespace(), action="touch_down", position=[100.0, 100.0], touch_id=2
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["touch_id"] == 2


def test_touch_move_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(
            SimpleNamespace(), action="touch_move", position=[150.0, 150.0], touch_id=1
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["position"] == [150.0, 150.0]


def test_touch_up_forwards_params(mock_unity):
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="touch_up", touch_id=1)
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "touch_up"


# ── get_devices test ───────────────────────────────────────


def test_get_devices_forwards(mock_unity):
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="get_devices")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_devices"


# ── None parameter omission ───────────────────────────────


def test_none_params_omitted(mock_unity):
    """Optional params that are None should not appear in the forwarded dict."""
    result = asyncio.run(
        manage_input(SimpleNamespace(), action="key_press", key="w")
    )
    assert result["success"] is True
    params = mock_unity["params"]
    assert "button" not in params
    assert "position" not in params
    assert "delta" not in params
    assert "stick" not in params
    assert "trigger" not in params
    assert "touch_id" not in params
    assert "duration_ms" not in params


# ── Non-dict response handling ─────────────────────────────


def test_non_dict_response_wrapped(monkeypatch):
    """When Unity returns a non-dict, it should be wrapped."""

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.manage_input.get_unity_instance_from_context",
        AsyncMock(return_value="unity-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_input.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_input(SimpleNamespace(), action="key_press", key="w")
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]
