from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import time
from typing import Union, List


class Ignite64ClockError(ValueError):
    pass


@dataclass(frozen=True)
class Si5340Write:
    page: int
    reg: int
    value: int


def parse_si5340_config(path: "Union[str, Path]") -> "List[Si5340Write]":
    """
    Parser compatible with the C# `writeSI5340ConfFromFile`.

    Supported lines:
    - "# Created ..." (ignored)
    - "# Delay <ms> msec" (handled by caller; this parser stores it as a pseudo write with reg=0xFF)
    - "<ADDR_HEX> <VAL_HEX>" where ADDR_HEX is 4 hex chars: PPAA (PP=page, AA=reg)
      The C# code also accepts commas and "0x" tokens; here we keep it tolerant.
    - Header "Address Data" lines are ignored.
    """
    sep_tokens = (" ", ",")
    writes: List[Si5340Write] = []

    for raw in Path(path).read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if not line:
            continue
        # mimic the C# split behavior that removes "0x"
        line = line.replace("0x", " ").replace("0X", " ")
        for s in sep_tokens:
            line = line.replace(s, " ")
        parts = [p for p in line.split() if p]
        if not parts:
            continue

        if parts[0] == "#" and len(parts) > 4 and parts[1].lower() == "created":
            continue
        if parts[0] == "#" and len(parts) > 3 and parts[1].lower() == "delay" and parts[3].lower() == "msec":
            try:
                ms = int(parts[2])
            except ValueError as e:
                raise Ignite64ClockError(f"Bad delay line: {raw!r}") from e
            # encode delay as pseudo-write: page=0, reg=0xFF, value=ms (caller interprets)
            writes.append(Si5340Write(page=0, reg=0xFF, value=ms))
            continue

        if len(parts) != 2 or parts[0].lower() == "address":
            continue

        addr_hex = parts[0]
        if len(addr_hex) < 4:
            # accept short forms by left-padding
            addr_hex = addr_hex.rjust(4, "0")
        try:
            page = int(addr_hex[0:2], 16)
            reg = int(addr_hex[2:4], 16)
            value = int(parts[1], 16)
        except ValueError as e:
            raise Ignite64ClockError(f"Bad config line: {raw!r}") from e
        if not (0 <= value <= 0xFF):
            raise Ignite64ClockError(f"Value out of range 0..255 in line: {raw!r}")
        writes.append(Si5340Write(page=page, reg=reg, value=value))

    return writes

