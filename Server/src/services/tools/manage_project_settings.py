from typing import Annotated, Any, Literal, Optional, get_args

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ProjectSettingsAction = Literal[
    "list_categories",
    "list_properties",
    "get_settings",
    "set_settings",
    "discover",
]

ALL_ACTIONS: list[str] = list(get_args(ProjectSettingsAction))


@mcp_for_unity_tool(
    group="core",
    description=(
        "Read and write Unity ProjectSettings by category.\n\n"
        "CATEGORIES: version_control\n\n"
        "ACTIONS:\n"
        "  list_categories - List supported settings categories\n"
        "  list_properties - List properties and current values for a category\n"
        "  get_settings    - Read all settings for a category\n"
        "  set_settings    - Write settings (pass settings dict)\n"
        "  discover        - Enumerate all underlying properties for a category (debug)\n\n"
        "VERSION CONTROL PROPERTIES:\n"
        "  mode - 'Visible Meta Files' | 'Hidden Meta Files' | 'Perforce' | 'PlasticSCM'\n"
        "  serialization_mode - 'ForceText' | 'ForceBinary' | 'Mixed'\n"
    ),
    annotations=ToolAnnotations(title="Manage Project Settings", destructiveHint=True),
)
async def manage_project_settings(
    ctx: Context,
    action: Annotated[ProjectSettingsAction, "The settings action to perform."],
    category: Annotated[
        Optional[str], "Settings category, e.g. 'version_control'."
    ] = None,
    settings: Annotated[
        Optional[dict[str, Any]], "Key-value pairs for set_settings."
    ] = None,
) -> dict[str, Any]:
    """Read and write Unity ProjectSettings by category."""

    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid: {', '.join(ALL_ACTIONS)}",
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_lower}

    param_map = {
        "category": category,
        "settings": settings,
    }
    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_project_settings",
        params_dict,
    )
    if not isinstance(result, dict):
        return {"success": False, "message": str(result)}
    return result
