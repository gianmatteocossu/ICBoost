from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Union


class Ignite64ConfigError(ValueError):
    pass


_QUAD_TO_INDEX = {"SW": 0, "NW": 1, "SE": 2, "NE": 3}
_INDEX_TO_QUAD = {v: k for k, v in _QUAD_TO_INDEX.items()}


@dataclass(frozen=True)
class FullConfiguration:
    quadrant: Optional[str]
    # TOP: 19 bytes written to dev 0xFC, reg 0..18
    top: list[int]
    # MAT: mapping MatID -> 108 bytes written to dev (2*MatID), reg 0..107
    mats: dict[int, list[int]]
    # IOext: 11 bytes written to dev 0x40, reg 0..10
    ioext: list[int]


def _is_int_line(s: str) -> bool:
    s = s.strip()
    if not s:
        return False
    try:
        int(s)
        return True
    except ValueError:
        return False


def _read_int_line(s: str) -> int:
    try:
        v = int(s.strip())
    except ValueError as e:
        raise Ignite64ConfigError(f"Expected integer line, got: {s!r}") from e
    if v < 0 or v > 255:
        raise Ignite64ConfigError(f"Byte out of range 0..255: {v}")
    return v


def parse_full_configuration(path: "Union[str, Path]") -> FullConfiguration:
    """
    Parse the same text format produced by MainForm.saveFullConfigurationToString().
    """
    lines = Path(path).read_text(encoding="utf-8", errors="replace").splitlines()
    i = 0
    while i < len(lines) and not lines[i].strip():
        i += 1

    quadrant: Optional[str] = None
    if i < len(lines) and lines[i].startswith("Quadrant "):
        quadrant = lines[i].split("Quadrant ", 1)[1].strip() or None
        i += 1

    # Skip the next line (Cur_Quad index) if present (C# writes it)
    if i < len(lines) and _is_int_line(lines[i]):
        i += 1

    # Seek TOP
    while i < len(lines) and not lines[i].startswith("TOP"):
        i += 1
    if i >= len(lines):
        raise Ignite64ConfigError("Missing TOP section")
    i += 1

    top: list[int] = []
    while len(top) < 19 and i < len(lines):
        if not lines[i].strip():
            i += 1
            continue
        top.append(_read_int_line(lines[i]))
        i += 1
    if len(top) != 19:
        raise Ignite64ConfigError(f"TOP section incomplete: got {len(top)} bytes, expected 19")

    mats: dict[int, list[int]] = {}
    # Seek MAT sections until IOext header
    while i < len(lines):
        # skip blanks
        while i < len(lines) and not lines[i].strip():
            i += 1
        if i >= len(lines):
            break
        if lines[i].startswith("I/O Ext"):
            break
        if not lines[i].startswith("MAT"):
            # ignore unexpected line
            i += 1
            continue

        header = lines[i].strip()  # e.g. "MAT 0"
        parts = header.replace("MAT", "").strip().split()
        if not parts:
            raise Ignite64ConfigError(f"Bad MAT header: {header!r}")
        try:
            mat_id = int(parts[0])
        except ValueError as e:
            raise Ignite64ConfigError(f"Bad MAT id in header: {header!r}") from e
        i += 1

        data: list[int] = []
        while len(data) < 108 and i < len(lines):
            if not lines[i].strip():
                i += 1
                continue
            # stop if next section begins unexpectedly
            if lines[i].startswith("MAT") or lines[i].startswith("I/O Ext"):
                break
            data.append(_read_int_line(lines[i]))
            i += 1
        if len(data) != 108:
            raise Ignite64ConfigError(f"MAT {mat_id} incomplete: got {len(data)} bytes, expected 108")
        mats[mat_id] = data

    # Seek IOext
    while i < len(lines) and not lines[i].startswith("I/O Ext"):
        i += 1
    if i >= len(lines):
        raise Ignite64ConfigError("Missing I/O Ext & I2C Mux Registers section")
    i += 1

    ioext: list[int] = []
    while len(ioext) < 11 and i < len(lines):
        if not lines[i].strip():
            i += 1
            continue
        ioext.append(_read_int_line(lines[i]))
        i += 1
    if len(ioext) != 11:
        raise Ignite64ConfigError(f"IOext section incomplete: got {len(ioext)} bytes, expected 11")

    return FullConfiguration(quadrant=quadrant, top=top, mats=mats, ioext=ioext)


def rewrite_full_configuration_quadrant(
    src_path: "Union[str, Path]",
    dst_path: "Union[str, Path]",
    quadrant: str,
) -> None:
    """
    Rewrite only the header quadrant fields in a full configuration file:
    - Line "Quadrant XX"
    - The following numeric Cur_Quad line (0..3)

    This is what the C# GUI uses to decide which quadrant to select; the actual mux
    selection is performed when loading (we do that in icboost via select_quadrant()).
    """
    q = quadrant.strip().upper()
    if q not in _QUAD_TO_INDEX:
        raise Ignite64ConfigError(f"Unknown quadrant {quadrant!r} (expected SW/NW/SE/NE)")

    lines = Path(src_path).read_text(encoding="utf-8", errors="replace").splitlines()
    i = 0
    while i < len(lines) and not lines[i].strip():
        i += 1
    if i >= len(lines) or not lines[i].startswith("Quadrant "):
        raise Ignite64ConfigError("Source file does not start with 'Quadrant XX' header")

    lines[i] = f"Quadrant {q}"
    i += 1

    # Next non-empty line should be the Cur_Quad index
    while i < len(lines) and not lines[i].strip():
        i += 1
    if i < len(lines) and _is_int_line(lines[i]):
        lines[i] = str(_QUAD_TO_INDEX[q])
    else:
        # if missing, insert it
        lines.insert(i, str(_QUAD_TO_INDEX[q]))

    Path(dst_path).write_text("\n".join(lines) + "\n", encoding="utf-8")

