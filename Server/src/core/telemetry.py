"""Version utility for MCP for Unity server."""

from importlib import metadata
from pathlib import Path

import tomli

PACKAGE_NAME = "mcpforunityserver"


def _version_from_local_pyproject() -> str:
    """Locate the nearest pyproject.toml that matches our package name."""
    current = Path(__file__).resolve()
    for parent in current.parents:
        candidate = parent / "pyproject.toml"
        if not candidate.exists():
            continue
        try:
            with candidate.open("rb") as f:
                data = tomli.load(f)
        except (OSError, tomli.TOMLDecodeError):
            continue

        project_table = data.get("project") or {}
        poetry_table = data.get("tool", {}).get("poetry", {})

        project_name = project_table.get("name") or poetry_table.get("name")
        if project_name and project_name.lower() != PACKAGE_NAME.lower():
            continue

        version = project_table.get("version") or poetry_table.get("version")
        if version:
            return version
    raise FileNotFoundError("pyproject.toml not found for mcpforunityserver")


def get_package_version() -> str:
    """Get package version: installed metadata first, then pyproject.toml fallback."""
    try:
        return metadata.version(PACKAGE_NAME)
    except Exception:
        try:
            return _version_from_local_pyproject()
        except Exception:
            return "unknown"


MCP_VERSION = get_package_version()
