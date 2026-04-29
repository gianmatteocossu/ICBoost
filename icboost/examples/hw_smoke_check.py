"""
Checklist HW rapida da eseguire dopo molto tempo senza banco (o dopo aggiornamenti software).

Uso (dalla cartella progetto ``icboost`` — o ``ignite64py`` se non rinominata — oppure con PYTHONPATH sul repo):

  python examples/hw_smoke_check.py

Opzionale: ``QUAD=NW`` ``SERIAL=5284`` nell'ambiente.

Passi:
  1. Carica DLL / USB come negli altri esempi
  2. Serial selection (se enumerazione OK)
  3. ``select_quadrant``
  4. TOP read byte 0 + readout I2C
  5. Una lettura FIFO (può essere 0 = vuota)

Non modifica configurazioni MAT significative (solo mux/TOP readout dove serve).
"""

from __future__ import annotations

import os
import platform
import sys
from pathlib import Path


def _fail(step: str, err: BaseException) -> None:
    print(f"[FAIL] {step}: {err}")
    sys.exit(1)


def main() -> None:
    dll_dir = str(Path(__file__).resolve().parents[1])
    sys.path.insert(0, dll_dir)

    from icboost.api import Ignite64

    quad = os.environ.get("QUAD", "SW").strip().upper()
    serial_env = os.environ.get("SERIAL", "").strip()

    print("=== IGNITE64 HW smoke check ===")
    print("Python:", platform.python_version(), platform.architecture()[0])
    print("Quad:", quad)

    hw = Ignite64(dll_dir=dll_dir, usb_only=False)

    try:
        hw.enable_usb()
    except Exception as e:
        _fail("enable_usb()", e)

    try:
        n = hw.get_number_of_devices()
        print(f"[ OK ] GetNumberOfDevices(): {n}")
    except Exception as e:
        _fail("GetNumberOfDevices()", e)

    if n > 0 and serial_env.isdigit():
        try:
            rc = hw.select_by_serial_number(int(serial_env))
            print(f"[ OK ] SelectBySerialNumber({serial_env}) rc={rc}")
        except Exception as e:
            print(f"[WARN] SelectBySerialNumber: {e}")

    try:
        hw.select_quadrant(quad)
        print(f"[ OK ] select_quadrant({quad!r})")
    except Exception as e:
        _fail("select_quadrant()", e)

    try:
        hw.TopReadout("i2c")
        print("[ OK ] TopReadout('i2c')")
    except Exception as e:
        _fail("TopReadout()", e)

    try:
        top = int(hw.addr.top_addr) & 0xFF
        b0 = hw.i2c_read_byte(top, 0)
        print(f"[ OK ] TOP[0] read = 0x{b0:02X} (dev 0x{top:02X})")
    except Exception as e:
        _fail("TOP read reg0", e)

    try:
        ro = hw.readTopReadout()
        print(f"[ OK ] readTopReadout() = {ro!r}")
    except Exception as e:
        _fail("readTopReadout()", e)

    try:
        w = int(hw.FifoReadSingle())
        if w == 0:
            print("[ OK ] FifoReadSingle() = 0 (FIFO vuota — normale se non c'è traffico)")
        else:
            print(f"[ OK ] FifoReadSingle() = 0x{w:016X}")
    except Exception as e:
        _fail("FifoReadSingle()", e)

    try:
        mux = hw.read_mux_ctrl()
        print(f"[ OK ] read_mux_ctrl() = 0x{int(mux) & 0xFF:02X}")
    except Exception as e:
        print(f"[WARN] read_mux_ctrl(): {e}")

    print("=== Smoke check completato (nessun errore bloccante) ===")


if __name__ == "__main__":
    main()
