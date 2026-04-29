from __future__ import annotations

import os
import ctypes
from ctypes import c_byte, c_ushort, c_int, c_uint, c_char_p, POINTER
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Union


class Ignite64DllError(RuntimeError):
    pass


@dataclass(frozen=True)
class DllPaths:
    tcp_to_i2c: Path
    usb_to_i2c: Path


def _add_dll_dir(dll_dir: "Optional[Union[str, os.PathLike]]") -> None:
    if not dll_dir:
        return
    # Python 3.8+: needed when DLLs are not in PATH
    os.add_dll_directory(str(dll_dir))


def resolve_dlls(dll_dir: "Optional[Union[str, os.PathLike]]" = None) -> DllPaths:
    """
    Resolve DLL locations.

    If dll_dir is provided, we look there first; otherwise rely on PATH.
    """
    tcp_name = "TCPtoI2C.dll"
    usb_name = "USBtoI2C32.dll"

    if dll_dir:
        base = Path(dll_dir)
        tcp = base / tcp_name
        usb = base / usb_name
        if not tcp.exists():
            raise Ignite64DllError(f"Missing {tcp_name} in {base}")
        if not usb.exists():
            raise Ignite64DllError(f"Missing {usb_name} in {base}")
        return DllPaths(tcp_to_i2c=tcp, usb_to_i2c=usb)

    # If no dll_dir, we still return names as Paths (LoadLibrary will use PATH)
    return DllPaths(tcp_to_i2c=Path(tcp_name), usb_to_i2c=Path(usb_name))


class _TcpToI2CBase:
    """
    ctypes wrapper for TCPtoI2C.dll (P/Invoke signatures copied from MainForm.cs).

    Note on calling convention:
    - C# DllImport without CallingConvention uses Winapi (stdcall on Windows).
    - Some vendor builds/export layers still use cdecl.
    We support both by providing two concrete implementations.
    """

    _lib: ctypes.CDLL

    def __init__(self, lib: ctypes.CDLL):
        self._lib = lib

        self.EnableTransportTCP = self._lib.EnableTransportTCP
        self.EnableTransportTCP.argtypes = []
        self.EnableTransportTCP.restype = None

        self.EnableTransportUSB = self._lib.EnableTransportUSB
        self.EnableTransportUSB.argtypes = [c_char_p]
        self.EnableTransportUSB.restype = None

        self.ConnectDeviceTCP = self._lib.ConnectDeviceTCP
        self.ConnectDeviceTCP.argtypes = [c_byte, c_char_p, c_ushort]
        self.ConnectDeviceTCP.restype = None

        self.I2C_GetFrequency = self._lib.I2C_GetFrequency
        self.I2C_GetFrequency.argtypes = []
        self.I2C_GetFrequency.restype = c_int

        self.GetNumberOfDevices = self._lib.GetNumberOfDevices
        self.GetNumberOfDevices.argtypes = []
        self.GetNumberOfDevices.restype = c_int

        self.GetSerialNumbers = self._lib.GetSerialNumbers
        self.GetSerialNumbers.argtypes = [POINTER(c_int)]
        self.GetSerialNumbers.restype = c_int

        self.SelectBySerialNumber = self._lib.SelectBySerialNumber
        self.SelectBySerialNumber.argtypes = [c_int]
        self.SelectBySerialNumber.restype = c_int

        self.Get_DLL_Version = self._lib.Get_DLL_Version
        self.Get_DLL_Version.argtypes = []
        self.Get_DLL_Version.restype = c_int

        self.I2C_Read = self._lib.I2C_Read
        self.I2C_Read.argtypes = [c_byte, c_ushort, POINTER(c_byte), c_ushort]
        self.I2C_Read.restype = c_byte

        self.I2C_ReadArray = self._lib.I2C_ReadArray
        self.I2C_ReadArray.argtypes = [c_byte, c_byte, c_ushort, POINTER(c_byte)]
        self.I2C_ReadArray.restype = c_byte

        self.I2C_ReadByte = self._lib.I2C_ReadByte
        self.I2C_ReadByte.argtypes = [c_byte, c_byte, POINTER(c_byte)]
        self.I2C_ReadByte.restype = c_byte

        self.I2C_ReceiveByte = self._lib.I2C_ReceiveByte
        self.I2C_ReceiveByte.argtypes = [c_byte, POINTER(c_byte)]
        self.I2C_ReceiveByte.restype = c_byte

        self.I2C_SendByte = self._lib.I2C_SendByte
        self.I2C_SendByte.argtypes = [c_byte, c_byte]
        self.I2C_SendByte.restype = c_byte

        self.I2C_Write = self._lib.I2C_Write
        self.I2C_Write.argtypes = [c_byte, c_ushort, POINTER(c_byte), c_ushort]
        self.I2C_Write.restype = c_byte

        self.I2C_WriteArray = self._lib.I2C_WriteArray
        self.I2C_WriteArray.argtypes = [c_byte, c_byte, c_ushort, POINTER(c_byte)]
        self.I2C_WriteArray.restype = c_byte

        self.I2C_WriteByte = self._lib.I2C_WriteByte
        self.I2C_WriteByte.argtypes = [c_byte, c_byte, c_byte]
        self.I2C_WriteByte.restype = c_byte


class TcpToI2CStdcall(_TcpToI2CBase):
    def __init__(self, dll_path: Path):
        super().__init__(ctypes.WinDLL(str(dll_path)))


class TcpToI2CCdecl(_TcpToI2CBase):
    def __init__(self, dll_path: Path):
        super().__init__(ctypes.CDLL(str(dll_path)))


class UsbToI2C32(ctypes.WinDLL):
    """
    ctypes wrapper for USBtoI2C32.dll (P/Invoke signatures copied from MainForm.cs).
    """

    def __init__(self, dll_path: Path):
        super().__init__(str(dll_path))

        # Some installations export I2C primitives directly from USBtoI2C32.dll.
        # They are NOT imported in the C# GUI, but we try to bind them to support "USB-only" mode.
        def _bind(name: str, argtypes, restype):
            fn = getattr(self, name, None)
            if fn is None:
                return False
            fn.argtypes = argtypes
            fn.restype = restype
            return True

        self._has_i2c_primitives = False
        self._has_device_enum = False

        # Optional device enumeration/selection
        if _bind("GetNumberOfDevices", [], c_int) and _bind("SelectBySerialNumber", [c_int], c_int):
            # GetSerialNumbers signature varies across vendors; try common one
            fn = getattr(self, "GetSerialNumbers", None)
            if fn is not None:
                fn.argtypes = [POINTER(c_int)]
                fn.restype = c_int
                self._has_device_enum = True

        # Optional I2C primitives (match TCPtoI2C signatures used by our code)
        has = True
        has &= _bind("I2C_ReadByte", [c_byte, c_byte, POINTER(c_byte)], c_byte)
        has &= _bind("I2C_WriteByte", [c_byte, c_byte, c_byte], c_byte)
        has &= _bind("I2C_ReadArray", [c_byte, c_byte, c_ushort, POINTER(c_byte)], c_byte)
        has &= _bind("I2C_WriteArray", [c_byte, c_byte, c_ushort, POINTER(c_byte)], c_byte)
        has &= _bind("I2C_SendByte", [c_byte, c_byte], c_byte)
        has &= _bind("I2C_ReceiveByte", [c_byte, POINTER(c_byte)], c_byte)
        self._has_i2c_primitives = bool(has)

        self.I2C_SetFrequency.argtypes = [c_int]
        self.I2C_SetFrequency.restype = c_int

        self.GPIO_IN.argtypes = []
        self.GPIO_IN.restype = c_int

        self.GPIO_OUT.argtypes = [c_int]
        self.GPIO_OUT.restype = c_byte

        self.GPIO_Configure.argtypes = [c_byte]
        self.GPIO_Configure.restype = c_int

        self.I2C_BusRecovery.argtypes = []
        self.I2C_BusRecovery.restype = c_int


def load_tcp_dll(
    dll_dir: "Optional[Union[str, os.PathLike]]" = None,
    *,
    calling_convention: str = "stdcall",
) -> _TcpToI2CBase:
    _add_dll_dir(dll_dir)
    paths = resolve_dlls(dll_dir)
    try:
        cc = (calling_convention or "stdcall").strip().lower()
        if cc == "stdcall" or cc == "winapi":
            return TcpToI2CStdcall(paths.tcp_to_i2c)
        if cc == "cdecl":
            return TcpToI2CCdecl(paths.tcp_to_i2c)
        if cc == "auto":
            # Keep backwards compatibility: stdcall first, then cdecl.
            try:
                return TcpToI2CStdcall(paths.tcp_to_i2c)
            except OSError:
                return TcpToI2CCdecl(paths.tcp_to_i2c)
        raise Ignite64DllError(f"Unknown calling_convention={calling_convention!r} (expected stdcall/cdecl/auto)")
    except OSError as e:
        raise Ignite64DllError(f"Failed loading {paths.tcp_to_i2c}: {e}") from e


def load_usb_dll(dll_dir: "Optional[Union[str, os.PathLike]]" = None) -> UsbToI2C32:
    _add_dll_dir(dll_dir)
    paths = resolve_dlls(dll_dir)
    try:
        return UsbToI2C32(paths.usb_to_i2c)
    except OSError as e:
        raise Ignite64DllError(f"Failed loading {paths.usb_to_i2c}: {e}") from e


def load_usb_as_i2c_backend(dll_dir: "Optional[Union[str, os.PathLike]]" = None) -> UsbToI2C32:
    """
    Load USBtoI2C32.dll and require that it exports the I2C primitives we need.
    This enables "USB-only" mode without TCPtoI2C.dll.
    """
    usb = load_usb_dll(dll_dir)
    if not getattr(usb, "_has_i2c_primitives", False):
        raise Ignite64DllError(
            "USBtoI2C32.dll loaded, but it does not export the I2C primitives "
            "(I2C_ReadByte/WriteByte/ReadArray/WriteArray/SendByte/ReceiveByte). "
            "In this installation the common I2C API appears to be provided by TCPtoI2C.dll."
        )
    return usb

