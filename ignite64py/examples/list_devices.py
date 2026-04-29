from ignite64py.api import Ignite64
from pathlib import Path
import platform


def main() -> None:
    dll_dir = str(Path(__file__).resolve().parents[1])
    hw = Ignite64(dll_dir=dll_dir, usb_only=False)

    print("Python:", platform.python_version(), platform.architecture())
    print("TCPtoI2C DLL:", hw.get_loaded_dll_path("TCPtoI2C.dll"))
    print("USBtoI2C32 DLL:", hw.get_loaded_dll_path("USBtoI2C32.dll"))
    print("TCPtoI2C dll_version:", hw.get_dll_version())

    # Must be called before enumeration (like C# SandroBOX path)
    hw.enable_usb()
    print("After enable_usb() - USBtoI2C32 DLL:", hw.get_loaded_dll_path("USBtoI2C32.dll"))

    print("GetNumberOfDevices():", hw.get_number_of_devices())
    serials = hw.get_serial_numbers(max_devices=32)
    print("GetSerialNumbers():", serials)

    # Try selecting a known serial (edit as needed)
    serial = 5284
    if serials:
        try:
            rc = hw.select_by_serial_number(serial)
            print(f"SelectBySerialNumber({serial}) rc:", rc)
        except OSError as e:
            print(f"SelectBySerialNumber({serial}) raised OSError:", repr(e))
    else:
        print("Skip SelectBySerialNumber(): no devices enumerated")


if __name__ == "__main__":
    main()

