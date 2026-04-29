from __future__ import annotations

import argparse
from pathlib import Path


def main() -> None:
    p = argparse.ArgumentParser(description="Isolate which DLL call triggers vendor popups.")
    p.add_argument(
        "step",
        choices=[
            "enable_usb",
            "enable_then_get_serials",
            "enable_then_select_cold",
            "enable_then_get_serials_then_select",
            "all",
        ],
        help="Which step to execute, then exit.",
    )
    p.add_argument("--serial", type=int, default=5284, help="Serial to select for 'select' step.")
    args = p.parse_args()

    print("popup_probe.py: start")

    # Import inside main so we can see popups timing relative to import.
    from ignite64py.api import Ignite64
    from ignite64py.device import Ignite64Addresses

    print("popup_probe.py: ignite64py imported")

    dll_dir = str(Path(__file__).resolve().parents[1])
    addrs = Ignite64Addresses(mux_addr=0xE0, ioext_addr=0x40, top_addr=0xFC, strict=True)
    hw = Ignite64(dll_dir=dll_dir, usb_only=False, addresses=addrs)

    if args.step in ("enable_usb", "all"):
        print("STEP enable_usb()")
        hw.enable_usb()
        print("STEP enable_usb() done")
        if args.step == "enable_usb":
            return

    if args.step in ("enable_then_get_serials", "all"):
        print("STEP enable_usb()")
        hw.enable_usb()
        print("STEP enable_usb() done")
        print("STEP get_serial_numbers()")
        serials = hw.get_serial_numbers(max_devices=32)
        print("STEP get_serial_numbers() done ->", serials)
        if args.step == "enable_then_get_serials":
            return

    if args.step in ("enable_then_select_cold", "all"):
        print("STEP enable_usb()")
        hw.enable_usb()
        print("STEP enable_usb() done")
        print(f"STEP select_by_serial_number({args.serial}) [COLD]")
        rc = hw.select_by_serial_number(args.serial)
        print(f"STEP select_by_serial_number({args.serial}) done -> rc={rc}")
        if args.step == "enable_then_select_cold":
            return

    if args.step in ("enable_then_get_serials_then_select", "all"):
        print("STEP enable_usb()")
        hw.enable_usb()
        print("STEP enable_usb() done")
        print("STEP get_serial_numbers()")
        serials = hw.get_serial_numbers(max_devices=32)
        print("STEP get_serial_numbers() done ->", serials)
        print(f"STEP select_by_serial_number({args.serial})")
        rc = hw.select_by_serial_number(args.serial)
        print(f"STEP select_by_serial_number({args.serial}) done -> rc={rc}")
        if args.step == "enable_then_get_serials_then_select":
            return


if __name__ == "__main__":
    main()

