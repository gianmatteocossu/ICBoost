from __future__ import annotations

import threading
from dataclasses import dataclass

from ctypes import c_byte, c_int, c_ushort, byref, wintypes
import ctypes
from typing import Optional, Union, List

from .dll import load_tcp_dll, load_usb_dll, load_usb_as_i2c_backend, Ignite64DllError


class Ignite64TransportError(RuntimeError):
    pass


def _quad_to_mask(quad: str) -> int:
    q = quad.strip().upper()
    if q == "SW":
        return 0x01
    if q == "NW":
        return 0x02
    if q == "SE":
        return 0x04
    if q == "NE":
        return 0x08
    raise ValueError(f"Unknown quadrant: {quad!r} (expected SW/NW/SE/NE)")


@dataclass
class Ignite64Addresses:
    mux_addr: int = 0xE0  # common defaults: 0xE0 or 0xAE (selectable in C# GUI)
    top_addr: int = 0xFC  # common defaults: 0xFC or 0xAE (selectable in C# GUI)
    ioext_addr: int = 0x40  # common defaults: 0x40 or 0xAE (selectable in C# GUI)
    strict: bool = True  # avoid probing alternate addresses (reduces vendor popups)


class Ignite64LowLevel:
    """
    Low-level access: I2C read/write and quadrant mux selection.
    """

    def __init__(
        self,
        *,
        dll_dir: Optional[str] = None,
        addresses: Optional[Ignite64Addresses] = None,
        usb_only: bool = True,
    ):
        self._dll_dir = dll_dir
        self.usb = None
        self._usb_only = bool(usb_only)

        # Backend for I2C primitives:
        # - C# uses TCPtoI2C.dll even in USB mode (EnableTransportUSB).
        # - If user requests USB-only, we try to use USBtoI2C32.dll directly as I2C backend.
        if usb_only:
            self.tcp = load_usb_as_i2c_backend(dll_dir)
            # also keep usb handle for USB-only helpers
            self.usb = self.tcp
        else:
            # Avoid calling DLL functions during load; stdcall matches C# DllImport default.
            self.tcp = load_tcp_dll(dll_dir, calling_convention="stdcall")
        self.addr = addresses or Ignite64Addresses()
        self._i2c_lock = threading.Lock()

    # ---- transport selection ----

    def enable_tcp(self) -> None:
        self.tcp.EnableTransportTCP()

    def enable_usb(self) -> None:
        # In C# they pass "USBtoI2C32.dll" to TCPtoI2C.EnableTransportUSB().
        # In USB-only mode there is no such function: USBtoI2C32.dll is used directly.
        if self._usb_only:
            if self.usb is None:
                self.usb = load_usb_dll(self._dll_dir)
            return

        self.tcp.EnableTransportUSB(b"USBtoI2C32.dll")
        if self.usb is None:
            self.usb = load_usb_dll(self._dll_dir)

    def _require_usb(self) -> None:
        if self.usb is None:
            raise Ignite64TransportError("USB transport not initialized. Call enable_usb() first.")

    # ---- USB-only helpers (mirror C# usage) ----

    def i2c_set_frequency(self, frequency_hz: int) -> int:
        """
        USB-only: calls USBtoI2C32.dll I2C_SetFrequency(frequency).
        Returns the DLL return code.
        """
        self._require_usb()
        return int(self.usb.I2C_SetFrequency(int(frequency_hz)))

    def gpio_configure(self, port_configuration: int) -> int:
        """
        USB-only: calls GPIO_Configure(byte PortConfiguation).
        """
        self._require_usb()
        return int(self.usb.GPIO_Configure(c_byte(port_configuration & 0xFF)))

    def gpio_in(self) -> int:
        """
        USB-only: calls GPIO_IN().
        """
        self._require_usb()
        return int(self.usb.GPIO_IN())

    def gpio_out(self, output_state: int) -> int:
        """
        USB-only: calls GPIO_OUT(int OutputState).
        """
        self._require_usb()
        return int(self.usb.GPIO_OUT(int(output_state)))

    def i2c_bus_recovery(self) -> int:
        """
        USB-only: calls I2C_BusRecovery().
        """
        self._require_usb()
        return int(self.usb.I2C_BusRecovery())

    def connect_tcp(self, *, serial_number: int, ip: str, port: int) -> None:
        # C#: ConnectDeviceTCP(byte SerialNumber, string IP, ushort port)
        self.tcp.ConnectDeviceTCP(c_byte(serial_number), ip.encode("ascii"), c_ushort(port))

    def get_dll_version(self) -> int:
        return int(self.tcp.Get_DLL_Version())

    def get_loaded_dll_path(self, dll_name: str) -> Optional[str]:
        """
        Equivalent of C# GetModuleHandle + GetModuleFileName for a DLL.
        Returns the full path of the loaded module, or None if not loaded.
        """
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
        kernel32.GetModuleHandleW.restype = wintypes.HMODULE
        kernel32.GetModuleFileNameW.argtypes = [wintypes.HMODULE, wintypes.LPWSTR, wintypes.DWORD]
        kernel32.GetModuleFileNameW.restype = wintypes.DWORD

        hmod = kernel32.GetModuleHandleW(dll_name)
        if not hmod:
            return None
        buf = ctypes.create_unicode_buffer(260)
        n = kernel32.GetModuleFileNameW(hmod, buf, 260)
        if n == 0:
            return None
        return buf.value

    def get_number_of_devices(self) -> int:
        return int(self.tcp.GetNumberOfDevices())

    def get_serial_numbers(self, max_devices: int = 10) -> list[int]:
        """
        Mirror of C# GetSerialNumbers(int[]).
        Returns a list of serial numbers (length == number returned by DLL).
        """
        # Some vendor DLLs expect an array large enough; allocate generously.
        n_alloc = max(32, int(max_devices))
        arr = (c_int * n_alloc)()
        n = int(self.tcp.GetSerialNumbers(arr))
        if n <= 0:
            return []
        return [int(arr[i]) for i in range(min(n, n_alloc))]

    def select_by_serial_number(self, serial_number: int) -> int:
        try:
            return int(self.tcp.SelectBySerialNumber(c_int(serial_number)))
        except OSError as e:
            raise Ignite64TransportError(
                "SelectBySerialNumber caused an OS error (access violation). "
                "This often indicates a driver/runtime mismatch or restricted Python distribution "
                "(Windows Store Python) interacting with native USB drivers. "
                f"serial={serial_number}, error={e!r}"
            ) from e

    def init_sandrobox_usb(
        self,
        *,
        preferred_serial: Optional[int] = None,
        i2c_frequency_hz: Optional[int] = None,
        do_gpio_init: bool = True,
        do_ioext_init: bool = True,
        do_bus_recovery: bool = False,
        retry_enumeration: bool = False,
    ) -> int:
        """
        Equivalent of the default startup path when the GUI chooses "SandroBOX":
        - EnableTransportUSB("USBtoI2C32.dll")
        - select first available serial (or preferred_serial)
        - (optional) I2C_SetFrequency(...)
        - (optional) GPIO_Configure(15) ; GPIO_OUT(10)
        - (optional) IOext_init() default register init on dev 0x40 regs 0..10

        Returns the selected serial number.
        """
        import os

        debug = os.environ.get("IGNITE64_DEBUG", "").strip() not in ("", "0", "false", "False", "FALSE")

        def _ck(msg: str) -> None:
            if not debug:
                return
            # stderr is less likely to be buffered.
            import sys
            sys.stderr.write(msg.rstrip() + "\n")
            sys.stderr.flush()

        _ck("[ignite64] init_sandrobox_usb: enable_usb()")
        self.enable_usb()
        _ck("[ignite64] init_sandrobox_usb: enable_usb() done")

        # Give the driver a moment after transport init.
        import time
        time.sleep(0.25)

        # IMPORTANT: mimic C# startup ordering.
        # The GUI always calls GetSerialNumbers(...) before SelectBySerialNumber(...).
        # Some driver builds become unstable if SelectBySerialNumber is called "cold".
        # Enumeration can be flaky right after USB attach; optionally retry.
        serials: list[int] = []
        if retry_enumeration:
            # Some driver builds are very flaky right after open/replug.
            # Give it a wider window before failing.
            timeout_s = 30.0
            start = time.time()
            last_reenable = 0.0
            while time.time() - start < timeout_s and not serials:
                # periodically re-enable USB transport (some driver builds need it)
                if time.time() - last_reenable > 1.0:
                    try:
                        self.enable_usb()
                    except Exception:
                        pass
                    last_reenable = time.time()

                try:
                    _ = self.get_number_of_devices()
                except Exception:
                    pass

                try:
                    serials = self.get_serial_numbers(max_devices=32)
                except Exception:
                    serials = []

                if serials:
                    break
                time.sleep(0.1)
        else:
            _ck("[ignite64] init_sandrobox_usb: GetSerialNumbers()")
            serials = self.get_serial_numbers(max_devices=32)
            _ck(f"[ignite64] init_sandrobox_usb: GetSerialNumbers() -> {serials!r}")

        if not serials:
            if self._usb_only:
                raise Ignite64TransportError(
                    "USB-only mode: GetSerialNumbers returned 0 or is not exported by USBtoI2C32.dll. "
                    "This installation likely requires TCPtoI2C.dll for device enumeration/selection."
                )
            dll_ver = None
            try:
                dll_ver = self.get_dll_version()
            except Exception:
                pass
            try:
                n_dev = self.get_number_of_devices()
            except Exception:
                n_dev = None
            raise Ignite64TransportError(
                "No USBtoI2C devices found (GetSerialNumbers returned 0). "
                f"GetNumberOfDevices={n_dev}, TCPtoI2C dll_version={dll_ver}, "
                f"TCPtoI2C loaded_from={self.get_loaded_dll_path('TCPtoI2C.dll')}, "
                f"USBtoI2C32 loaded_from={self.get_loaded_dll_path('USBtoI2C32.dll')}"
            )

        # Prefer a known serial if provided, but only if it is present in the enumerated list
        candidates: list[int] = []
        if preferred_serial is not None and preferred_serial in serials:
            candidates.append(preferred_serial)
        candidates.extend([s for s in serials if s not in candidates])

        selected: Optional[int] = None
        last_rc = None
        # Retry selection a couple of times in case the driver is slow to settle
        for _try in range(3):
            for s in candidates:
                try:
                    _ck(f"[ignite64] init_sandrobox_usb: SelectBySerialNumber({s})")
                    rc = self.select_by_serial_number(s)
                    last_rc = rc
                    _ck(f"[ignite64] init_sandrobox_usb: SelectBySerialNumber({s}) -> rc={rc}")
                except Ignite64TransportError:
                    # Access violation or similar; wait and retry
                    time.sleep(0.1)
                    continue
                if rc != 0:
                    selected = s
                    break
            if selected is not None:
                break
            time.sleep(0.1)

        if selected is None:
            raise Ignite64TransportError(
                "Could not SelectBySerialNumber any device. "
                f"Candidates={candidates}, last_rc={last_rc}"
            )

        if i2c_frequency_hz is not None:
            _ck(f"[ignite64] init_sandrobox_usb: I2C_SetFrequency({int(i2c_frequency_hz)})")
            self.i2c_set_frequency(int(i2c_frequency_hz))
            _ck("[ignite64] init_sandrobox_usb: I2C_SetFrequency() done")
        if do_gpio_init:
            _ck("[ignite64] init_sandrobox_usb: GPIO_Configure(15)")
            self.gpio_configure(15)
            _ck("[ignite64] init_sandrobox_usb: GPIO_OUT(10)")
            self.gpio_out(10)
        # Some driver builds show blocking popups on failed recovery attempts; keep it opt-in.
        if do_bus_recovery:
            _ck("[ignite64] init_sandrobox_usb: I2C_BusRecovery()")
            self.i2c_bus_recovery()
            _ck("[ignite64] init_sandrobox_usb: I2C_BusRecovery() done")
        if do_ioext_init:
            _ck("[ignite64] init_sandrobox_usb: ioext_init_defaults()")
            self.ioext_init_defaults()
        _ck("[ignite64] init_sandrobox_usb: done")
        return int(selected)

    def autodetect_ioext_address(
        self,
        candidates: Optional[list[int]] = None,
        *,
        retries: int = 3,
        delay_s: float = 0.05,
    ) -> int:
        """
        IOext I2C address in the GUI can be 0x40 or 0xAE.
        Probe candidates by trying a harmless read of reg 0 (IODIR).
        """
        if candidates is None:
            candidates = [self.addr.ioext_addr] if self.addr.strict else [self.addr.ioext_addr, 0x40, 0xAE]
        uniq: list[int] = []
        for a in candidates:
            a = int(a) & 0xFF
            if a not in uniq:
                uniq.append(a)

        import os
        import sys
        import time

        debug = os.environ.get("IGNITE64_DEBUG", "").strip() not in ("", "0", "false", "False", "FALSE")

        def _ck(msg: str) -> None:
            if not debug:
                return
            sys.stderr.write(msg.rstrip() + "\n")
            sys.stderr.flush()

        _ck(f"[ignite64] autodetect_ioext_address: candidates={', '.join('0x%02X' % a for a in uniq)} retries={retries} delay_s={delay_s}")

        last_err: Optional[Exception] = None
        for _attempt in range(max(1, int(retries))):
            _ck(f"[ignite64] autodetect_ioext_address: attempt={_attempt + 1}/{max(1, int(retries))}")
            for dev in uniq:
                try:
                    _ck(f"[ignite64] autodetect_ioext_address: probe read reg0 dev=0x{dev:02X}")
                    _ = self.i2c_read_byte(dev, 0)
                    self.addr.ioext_addr = dev
                    _ck(f"[ignite64] autodetect_ioext_address: OK dev=0x{dev:02X}")
                    return dev
                except Ignite64TransportError as e:
                    last_err = e
                    _ck(f"[ignite64] autodetect_ioext_address: FAIL dev=0x{dev:02X} err={e!r}")
            time.sleep(max(0.0, float(delay_s)))
        raise Ignite64TransportError(
            f"Could not detect IOext address. Tried: {', '.join('0x%02X' % a for a in uniq)}"
        ) from last_err

    def ioext_init_defaults(self) -> None:
        """
        Equivalent of C# IOext_init():
        write default values to IO extender dev 0x40 regs 0..10:
        reg0=0x80, reg5=0x20, reg9=0x34, reg10=0x40, others 0.
        """
        # Autodetect IOext address first (0x40 vs 0xAE).
        # Anche se l'istanza è in modalità "strict", qui vogliamo comunque
        # provare entrambi per evitare blocchi all'avvio (se l'hardware usa 0xAE).
        candidates: list[int] = []
        for a in (int(self.addr.ioext_addr), 0x40, 0xAE):
            a = int(a) & 0xFF
            if a not in candidates:
                candidates.append(a)
        import os
        import sys

        debug = os.environ.get("IGNITE64_DEBUG", "").strip() not in ("", "0", "false", "False", "FALSE")

        def _ck(msg: str) -> None:
            if not debug:
                return
            sys.stderr.write(msg.rstrip() + "\n")
            sys.stderr.flush()

        _ck(f"[ignite64] ioext_init_defaults: ioext_addr(initial)=0x{int(self.addr.ioext_addr) & 0xFF:02X} candidates={', '.join('0x%02X' % a for a in candidates)}")
        try:
            dev = self.autodetect_ioext_address(candidates=candidates, retries=5, delay_s=0.05)
        except Ignite64TransportError as e:
            # Fallback C#-like:
            # If probing IOext via a read of reg0 fails (bus/driver temporarily "stacked"),
            # still try writing defaults using the current known ioext_addr.
            dev = int(self.addr.ioext_addr) & 0xFF
            if debug:
                try:
                    _ck(f"[ignite64] ioext_init_defaults: autodetect failed, fallback dev=0x{dev:02X} err={e!r}")
                except Exception:
                    pass
        defaults = [0x00] * 11
        defaults[0] = 0x80
        defaults[5] = 0x20
        defaults[9] = 0x34
        defaults[10] = 0x40
        _ck(f"[ignite64] ioext_init_defaults: selected dev=0x{dev:02X} writing defaults (reg0..10)")
        # Mirror C# IOext_init(): write one register at a time
        for reg, val in enumerate(defaults):
            _ck(f"[ignite64] ioext_init_defaults: write dev=0x{dev:02X} reg=0x{reg:02X} val=0x{val:02X}")
            self.i2c_write_byte(dev, reg, val)

    # ---- mux selection (quadrant) ----

    def select_quadrant(self, quad: str) -> None:
        """
        Write the mux control register with a bitmask (SW=1, NW=2, SE=4, NE=8),
        mirroring `Quad_I2C_Mux_CheckedChange` + `Mux_I2C_write_but_Click`.
        """
        mask = _quad_to_mask(quad)
        # Some setups use a different mux I2C address (E0 vs AE in the C# GUI).
        # Probing the wrong address can trigger vendor driver popups; allow strict mode.
        if self.addr.strict:
            candidates = [self.addr.mux_addr]
        else:
            candidates = [self.addr.mux_addr]
            for alt in (0xE0, 0xAE):
                if alt not in candidates:
                    candidates.append(alt)

        last_rc = None
        for mux_addr in candidates:
            rc = self.i2c_send_byte(mux_addr, mask)
            last_rc = rc
            if rc == 0:
                self.addr.mux_addr = mux_addr
                return
        raise Ignite64TransportError(
            f"I2C_SendByte(mux addrs={','.join('0x%02X' % a for a in candidates)}, mask=0x{mask:02X}) -> {last_rc}"
        )

    def read_mux_ctrl(self) -> int:
        """
        Read mux control register (best effort).
        """
        return self.i2c_receive_byte(self.addr.mux_addr)

    def autodetect_top_address(
        self,
        candidates: Optional[list[int]] = None,
        *,
        retries: int = 3,
        delay_s: float = 0.1,
    ) -> int:
        """
        The C# GUI allows selecting TOP I2C address (usually 0xFC, sometimes 0xAE).
        This probes candidates and picks the first that responds.

        Note: on some setups READs may fail until TOP is unlocked (e.g. by writing 0xDC to reg 0).
        The C# loader doesn't probe, it just writes. For robustness we try:
        - ReadByte(reg=0)
        - if that fails, WriteByte(reg=0, value=0xDC) as a presence/unlock probe
        """
        if candidates is None:
            candidates = [self.addr.top_addr] if self.addr.strict else [self.addr.top_addr, 0xFC, 0xAE]
        # preserve order, remove dups
        uniq: list[int] = []
        for a in candidates:
            a = int(a) & 0xFF
            if a not in uniq:
                uniq.append(a)

        import time

        last_err: Optional[Exception] = None
        last_attempts: list[str] = []
        for _attempt in range(max(1, int(retries))):
            for dev in uniq:
                try:
                    _ = self.i2c_read_byte(dev, 0)
                    self.addr.top_addr = dev
                    return dev
                except Ignite64TransportError as e:
                    last_err = e
                    last_attempts.append(f"read dev=0x{dev:02X} -> {e}")

                try:
                    self.i2c_write_byte(dev, 0, 0xDC)
                    self.addr.top_addr = dev
                    return dev
                except Ignite64TransportError as e:
                    last_err = e
                    last_attempts.append(f"writeDC dev=0x{dev:02X} -> {e}")

            time.sleep(max(0.0, float(delay_s)))

        raise Ignite64TransportError(
            "Could not detect TOP address. "
            f"Tried: {', '.join('0x%02X' % a for a in uniq)}. "
            f"Last attempts: {last_attempts[-4:]}"
        ) from last_err

    # ---- I2C primitives (guarded like in C#) ----

    def i2c_read_byte(self, dev_addr: int, reg: int) -> int:
        with self._i2c_lock:
            out = c_byte(0)
            rc = int(self.tcp.I2C_ReadByte(c_byte(dev_addr), c_byte(reg), byref(out)))
            if rc != 0:
                raise Ignite64TransportError(f"I2C_ReadByte(dev=0x{dev_addr:02X}, reg=0x{reg:02X}) -> {rc}")
            return int(out.value) & 0xFF

    def i2c_write_byte(self, dev_addr: int, reg: int, value: int) -> None:
        with self._i2c_lock:
            rc = int(self.tcp.I2C_WriteByte(c_byte(dev_addr), c_byte(reg), c_byte(value & 0xFF)))
            if rc != 0:
                raise Ignite64TransportError(
                    f"I2C_WriteByte(dev=0x{dev_addr:02X}, reg=0x{reg:02X}, value=0x{value & 0xFF:02X}) -> {rc}"
                )

    def i2c_send_byte(self, dev_addr: int, value: int) -> int:
        with self._i2c_lock:
            return int(self.tcp.I2C_SendByte(c_byte(dev_addr), c_byte(value & 0xFF)))

    def i2c_receive_byte(self, dev_addr: int) -> int:
        """
        Receive a single byte (no register). Used for mux readback (like C# I2C_ReceiveByte).
        """
        with self._i2c_lock:
            out = c_byte(0)
            rc = int(self.tcp.I2C_ReceiveByte(c_byte(dev_addr), byref(out)))
            if rc != 0:
                raise Ignite64TransportError(f"I2C_ReceiveByte(dev=0x{dev_addr:02X}) -> {rc}")
            return int(out.value) & 0xFF

    def i2c_write_raw(self, dev_addr: int, data: "Union[bytes, bytearray, List[int]]", *, send_stop: int = 1) -> None:
        """
        Raw write without a subaddress (mirrors C# I2C_Write).

        Used by some external DAC/ADC devices that expect the first byte to be a command,
        not a register subaddress.
        """
        if isinstance(data, list):
            data = bytes(int(x) & 0xFF for x in data)
        else:
            data = bytes(data)
        n = len(data)
        if n == 0:
            return
        buf = (c_byte * n)(*data)
        with self._i2c_lock:
            rc = int(self.tcp.I2C_Write(c_byte(dev_addr), c_ushort(n), buf, c_ushort(int(send_stop))))
        if rc != 0:
            raise Ignite64TransportError(f"I2C_Write(dev=0x{dev_addr:02X}, n={n}, send_stop={send_stop}) -> {rc}")

    def i2c_read_raw(self, dev_addr: int, n: int, *, send_stop: int = 1) -> bytes:
        """
        Raw read without a subaddress (mirrors C# I2C_Read).
        """
        if n <= 0:
            return b""
        buf = (c_byte * n)()
        with self._i2c_lock:
            rc = int(self.tcp.I2C_Read(c_byte(dev_addr), c_ushort(int(n)), buf, c_ushort(int(send_stop))))
        if rc != 0:
            raise Ignite64TransportError(f"I2C_Read(dev=0x{dev_addr:02X}, n={n}, send_stop={send_stop}) -> {rc}")
        return bytes((int(b) & 0xFF) for b in buf)

    def i2c_write_bytes(self, dev_addr: int, start_reg: int, data: "Union[bytes, bytearray, List[int]]") -> None:
        """
        Write a contiguous register range using I2C_WriteArray(address, subaddress, nBytes, WriteData).
        """
        if isinstance(data, list):
            data = bytes(int(x) & 0xFF for x in data)
        else:
            data = bytes(data)
        n = len(data)
        if n == 0:
            return
        buf = (c_byte * n)(*data)
        with self._i2c_lock:
            rc = int(self.tcp.I2C_WriteArray(c_byte(dev_addr), c_byte(start_reg), c_ushort(n), buf))
        if rc != 0:
            raise Ignite64TransportError(
                f"I2C_WriteArray(dev=0x{dev_addr:02X}, start=0x{start_reg:02X}, n={n}) -> {rc}"
            )

    def i2c_read_bytes(self, dev_addr: int, start_reg: int, n: int) -> bytes:
        """
        Read a contiguous register range using I2C_ReadArray(address, subaddress, nBytes, ReadData).
        """
        if n <= 0:
            return b""
        buf = (c_byte * n)()
        with self._i2c_lock:
            rc = int(self.tcp.I2C_ReadArray(c_byte(dev_addr), c_byte(start_reg), c_ushort(n), buf))
        if rc != 0:
            raise Ignite64TransportError(
                f"I2C_ReadArray(dev=0x{dev_addr:02X}, start=0x{start_reg:02X}, n={n}) -> {rc}"
            )
        return bytes((int(b) & 0xFF) for b in buf)

    def i2c_read_bytes_rc(self, dev_addr: int, start_reg: int, n: int) -> tuple[int, bytes]:
        """
        Like i2c_read_bytes(), but returns (rc, data) instead of raising.
        Useful for vendor APIs that use a non-zero rc to signal "FIFO empty" (e.g. rc==3).
        """
        if n <= 0:
            return 0, b""
        buf = (c_byte * n)()
        with self._i2c_lock:
            rc = int(self.tcp.I2C_ReadArray(c_byte(dev_addr), c_byte(start_reg), c_ushort(n), buf))
        data = bytes((int(b) & 0xFF) for b in buf)
        return rc, data

    # ---- helpers for MAT addressing ----

    @staticmethod
    def matid_to_devaddr(mat_id: int) -> int:
        # Copied from Ignite32_MATID_ToIntDevAddr: return 2 * abs(MatID)
        if mat_id < 0 or mat_id > 15:
            raise ValueError(f"mat_id out of range: {mat_id} (expected 0..15)")
        # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
        # Enforce a hard guard here so callers can't accidentally hit them.
        # (Broadcast writes use special MAT IDs >15 -> dev 254 and are unaffected.)
        if 4 <= int(mat_id) <= 7:
            raise Ignite64TransportError(
                f"Direct MAT access disabled for mat_id={mat_id} (known I2C stack issue on MAT 4..7). "
                "Use broadcast/CalibDCO47 workaround."
            )
        return 2 * abs(mat_id)

