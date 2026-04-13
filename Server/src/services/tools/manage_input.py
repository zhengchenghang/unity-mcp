from typing import Annotated, Any, Literal, Optional, get_args

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

InputAction = Literal[
    "key_press",
    "key_down",
    "key_up",
    "mouse_move",
    "mouse_click",
    "mouse_down",
    "mouse_up",
    "mouse_scroll",
    "gamepad_button",
    "gamepad_stick",
    "gamepad_trigger",
    "touch_tap",
    "touch_down",
    "touch_move",
    "touch_up",
    "get_devices",
]

ALL_ACTIONS: list[str] = list(get_args(InputAction))


@mcp_for_unity_tool(
    group="core",
    description=(
        "Simulate input in Unity Play Mode via the Input System. "
        "Use with manage_editor(play/stop) and manage_camera(screenshot) "
        "to build an observe-act agent loop.\n\n"
        "REQUIRES: Play Mode active + Input System package installed.\n\n"
        "KEYBOARD: key_press (tap), key_down (hold), key_up (release). "
        "Keys: w, a, s, d, space, leftShift, escape, enter, tab, f1-f12, etc.\n\n"
        "MOUSE: mouse_move, mouse_click, mouse_down, mouse_up, mouse_scroll. "
        "Positions in screen pixels [x,y]. Buttons: left, right, middle.\n\n"
        "GAMEPAD: gamepad_button (south/a, north/y, east/b, west/x, start, select, "
        "l1, r1, l2, r2, l3, r3, dpadup/down/left/right), "
        "gamepad_stick (left/right, value [-1,1]), "
        "gamepad_trigger (left/right, value [0,1]).\n\n"
        "TOUCH: touch_tap, touch_down, touch_move, touch_up. "
        "Positions in screen pixels [x,y]. touch_id for multi-touch.\n\n"
        "INFO: get_devices lists available input devices.\n\n"
        "AGENT LOOP PATTERN:\n"
        "1. manage_editor(action='play')\n"
        "2. manage_camera(action='screenshot', capture_source='game_view', include_image=true)\n"
        "3. manage_input(action='key_press', key='w', duration_ms=200)\n"
        "4. manage_camera(action='screenshot', ...) -> observe result\n"
        "5. Repeat 2-4 until goal reached\n"
        "6. manage_editor(action='stop')"
    ),
    annotations=ToolAnnotations(title="Manage Input", destructiveHint=True),
)
async def manage_input(
    ctx: Context,
    action: Annotated[InputAction, "The input action to perform."],
    key: Annotated[
        Optional[str],
        "Key name for keyboard actions (e.g., 'w', 'space', 'leftShift', 'escape', 'enter').",
    ] = None,
    button: Annotated[
        Optional[str],
        "Button name. Mouse: left/right/middle. "
        "Gamepad: south/a, north/y, east/b, west/x, start, select, "
        "l1, r1, l2, r2, l3, r3, dpadup/down/left/right.",
    ] = None,
    position: Annotated[
        Optional[list[float]],
        "Screen position [x, y] in pixels for mouse/touch actions.",
    ] = None,
    value: Annotated[
        Optional[Any],
        "Stick value [x,y] in [-1,1] for gamepad_stick, or trigger value 0-1 for gamepad_trigger.",
    ] = None,
    delta: Annotated[
        Optional[list[float]],
        "Scroll delta [x, y] for mouse_scroll. Typical: [0, 120] up, [0, -120] down.",
    ] = None,
    stick: Annotated[
        Optional[str],
        "Stick name: 'left' or 'right' for gamepad_stick.",
    ] = None,
    trigger: Annotated[
        Optional[str],
        "Trigger name: 'left' or 'right' for gamepad_trigger.",
    ] = None,
    touch_id: Annotated[
        Optional[int],
        "Touch finger ID (1-10) for multi-touch. Default 1.",
    ] = None,
    duration_ms: Annotated[
        Optional[int],
        "Hold duration in ms for key_press, mouse_click, gamepad_button, touch_tap. "
        "0 = single frame. Max 10000.",
    ] = None,
) -> dict[str, Any]:
    """Simulate input in Unity Play Mode via the Input System."""

    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid: {', '.join(ALL_ACTIONS)}",
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_lower}

    param_map = {
        "key": key,
        "button": button,
        "position": position,
        "value": value,
        "delta": delta,
        "stick": stick,
        "trigger": trigger,
        "touch_id": touch_id,
        "duration_ms": duration_ms,
    }
    for k, v in param_map.items():
        if v is not None:
            params_dict[k] = v

    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_input", params_dict
    )
    if not isinstance(result, dict):
        return {"success": False, "message": str(result)}
    return result
