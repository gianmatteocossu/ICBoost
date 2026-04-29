from __future__ import annotations

from pathlib import Path
import os
import platform
import traceback
from typing import Any, Dict


def main() -> None:
    from ignite64py.api import Ignite64
    from ignite64py.device import Ignite64Addresses
    from ignite64py.device import Ignite64TransportError

    # Defaults
    SW, NW, SE, NE, ALL = "SW", "NW", "SE", "NE", "ALL"
    OFFLINE = os.environ.get("OFFLINE", "").strip() not in ("", "0", "false", "False", "FALSE")

    # Fixed addresses + strict mode avoid I2C probing (reduces vendor popups).
    addrs = Ignite64Addresses(mux_addr=0xE0, ioext_addr=0x40, top_addr=0xFC, strict=True)
    hw = Ignite64(dll_dir=str(Path(__file__).resolve().parents[1]), usb_only=False, addresses=addrs)

    # 1) Bring-up + load default configuration for a quadrant (or ALL)
    if OFFLINE:
        # (B) Self-test without touching the hardware (no enable_usb / enumeration).
        print("OFFLINE=1: skipping start_config() (no hardware required)")
        print("Python:", platform.python_version(), platform.architecture())
        print("TCPtoI2C loaded:", hw.get_loaded_dll_path("TCPtoI2C.dll"))
        print("USBtoI2C32 loaded:", hw.get_loaded_dll_path("USBtoI2C32.dll"))
        print("Interactive symbols:", {"SW": SW, "NW": NW, "SE": SE, "NE": NE, "ALL": ALL})
        # Verify key callables exist (this tests interactive console usability).
        required = [
            "start_config",
            "AnalogChannelOFF",
            "AnalogChannelON",
            "AnalogColumnSetDAC",
            "StartTP",
        ]
        missing = [name for name in required if not callable(getattr(hw, name, None))]
        if missing:
            raise RuntimeError(f"Self-test failed: missing callables: {missing}")
        print("Self-test OK: hw methods available (hardware calls will fail until device is connected)")
    else:
        try:
            hw.start_config(SW)
        except Ignite64TransportError as e:
            print("ERROR:", e)
            print(
                "Fix:\n"
                "  - chiudi eventuali console >>> ancora aperte\n"
                "  - chiudi il software C# / altri tool USBtoI2C\n"
                "  - stacca/riattacca l'USBtoI2C (o power-cycle board)\n"
                "  - verifica con: py examples\\list_devices.py\n"
            )
            raise

    def source(path: str) -> Dict[str, Any]:
        """
        Load a python "macro" file into the interactive console.

        Convention:
        - `source("spegnitutto.py")` executes the file.
        - If the file defines a callable with the same name as the filename stem
          (e.g. `def spegnitutto(hw, quad): ...`) it will be called automatically as:
            spegnitutto(hw, SW)
        - All definitions remain available in the interactive console namespace.
        """
        p = Path(path)
        if not p.is_absolute():
            # Search relative to CWD and to examples/macros/
            candidates = [
                Path.cwd() / p,
                Path(__file__).resolve().parent / p,
                Path(__file__).resolve().parent / "macros" / p,
            ]
            for c in candidates:
                if c.exists():
                    p = c
                    break
        if not p.exists():
            raise FileNotFoundError(f"macro file not found: {path}")

        ns: Dict[str, Any] = {"hw": hw, "SW": SW, "NW": NW, "SE": SE, "NE": NE, "ALL": ALL, "source": source}
        code_text = p.read_text(encoding="utf-8")
        exec(compile(code_text, str(p), "exec"), ns, ns)

        stem = p.stem
        fn = ns.get(stem)
        if callable(fn):
            fn(hw, SW)
        return ns

    # 2) Interactive console (type commands and press Enter)
    banner = (
        "IGNITE64 interactive console\n"
        "(Tip: set OFFLINE=1 to open console without hardware)\n"
        "Type commands like:\n"
        "  hw.AnalogChannelOFF(SW, mattonella=1, canale=23)\n"
        "  hw.AnalogChannelON(SW, mattonella=1, canale=23)\n"
        "  hw.StartTP(numberOfRepetition=10)\n"
        "  hw.start_config(ALL)\n"
        "  source('macros/spegnitutto.py')\n"
        "  source macros/spegnitutto.py\n"
    )
    print(banner)

    env: Dict[str, Any] = {"hw": hw, "SW": SW, "NW": NW, "SE": SE, "NE": NE, "ALL": ALL, "source": source}
    while True:
        try:
            line = input(">>> ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            break
        if not line:
            continue

        # Convenience: allow "source path/to/file.py" without parentheses.
        if line.lower().startswith("source "):
            path = line[7:].strip().strip('"').strip("'")
            try:
                source(path)
            except Exception:
                traceback.print_exc()
            continue

        try:
            # First try eval() for expressions (so return values print nicely).
            try:
                out = eval(line, env, env)
            except SyntaxError:
                out = None
                exec(line, env, env)
            if out is not None:
                print(repr(out))
        except Exception:
            traceback.print_exc()


if __name__ == "__main__":
    main()

