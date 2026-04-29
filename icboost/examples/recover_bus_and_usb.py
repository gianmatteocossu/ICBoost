"""
Recovery / diagnostics for SandroBOX USB-to-I2C bus.

Goal:
  - Detect USB device (preferred SERIAL, default 5284)
  - Re-run init_sandrobox_usb with bus recovery + GPIO init
  - Retry select_quadrant + minimal TOP readout check
  - Best-effort probe IOext reg10 (0x40 and 0xAE candidates)

Usage:
  - Run from repo root (or any cwd):
      python examples/recover_bus_and_usb.py
  - Optional env:
      SERIAL=5284
      QUAD=SW
      REPEATS=5
"""

from __future__ import annotations

import os
import sys
import time
import platform
from pathlib import Path


def _fail(step: str, err: BaseException) -> None:
    print(f"[FAIL] {step}: {err!r}")
    sys.exit(1)


def main() -> None:
    repo = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(repo))

    from icboost.api import Ignite64, Ignite64TransportError

    serial_env = os.environ.get("SERIAL", "").strip()
    serial = int(serial_env) if serial_env.isdigit() else 5284
    quad = os.environ.get("QUAD", "SW").strip().upper()
    repeats = int(os.environ.get("REPEATS", "5").strip() or "5")

    print("=== IGNITE64 USB/I2C recovery test ===")
    print("Python:", platform.python_version(), platform.architecture()[0])
    print("Serial:", serial, "Quad:", quad, "Repeats:", repeats)

    hw = Ignite64(dll_dir=str(repo), usb_only=False)

    # Transport init
    hw.enable_usb()

    # Wait/enumerate
    found = False
    for _ in range(10):
        try:
            n = hw.get_number_of_devices()
            serials = hw.get_serial_numbers(max_devices=32)
        except Exception:
            serials = []
            n = 0
        print(f"enum: n={n} serials={serials}")
        if serial in serials:
            found = True
            break
        time.sleep(1.0)

    if not found:
        _fail("USB enumeration", RuntimeError(f"Device serial {serial} not detected after retries"))

    # Recovery loop
    last_err: BaseException | None = None
    for attempt in range(1, repeats + 1):
        print(f"\n-- recovery attempt {attempt}/{repeats} --")
        try:
            # Re-init SandroBOX USB backend
            selected = hw.init_sandrobox_usb(
                preferred_serial=serial,
                i2c_frequency_hz=None,
                do_gpio_init=True,
                do_ioext_init=False,
                do_bus_recovery=True,
                retry_enumeration=True,
            )
            print("init_sandrobox_usb selected:", selected)

            # Retry select_quadrant (mux control)
            for sel_try in range(1, 4):
                try:
                    hw.select_quadrant(quad)
                    print("select_quadrant OK:", quad)
                    break
                except Exception as e:
                    print("select_quadrant failed:", sel_try, repr(e))
                    try:
                        hw.i2c_bus_recovery()
                        print("i2c_bus_recovery called")
                    except Exception as e2:
                        print("i2c_bus_recovery failed:", repr(e2))
                    time.sleep(0.2)
            else:
                raise RuntimeError("select_quadrant never recovered")

            # Minimal TOP access
            try:
                hw.TopReadout("i2c")
                print("TopReadout('i2c') OK")
            except Exception as e:
                print("TopReadout('i2c') failed:", repr(e))

            top = int(hw.addr.top_addr) & 0xFF
            b0 = hw.i2c_read_byte(top, 0)
            print(f"TOP[0] read dev=0x{top:02X} -> 0x{b0:02X}")

            # Best-effort IOext probe (reg 10)
            for dev in (0x40, 0xAE):
                try:
                    v = hw.i2c_read_byte(dev, 10)
                    print(f"IOext reg10 read dev=0x{dev:02X} -> 0x{int(v) & 0xFF:02X}")
                except Exception as e:
                    print(f"IOext reg10 read dev=0x{dev:02X} failed:", repr(e))

            print("\n[OK] Recovery attempt completed (no fatal errors).")
            return

        except BaseException as e:
            last_err = e
            print("[WARN] recovery attempt failed:", repr(e))
            time.sleep(1.0)

    _fail("recovery loop", last_err if last_err is not None else RuntimeError("unknown error"))  # type: ignore[arg-type]


if __name__ == "__main__":
    main()

