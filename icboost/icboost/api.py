from __future__ import annotations

from dataclasses import dataclass

from pathlib import Path
import time
from typing import Optional, Union
from .device import Ignite64LowLevel, Ignite64TransportError
from .config import parse_full_configuration
from .clock import parse_si5340_config
from .calib_dco import CalibDCOParams, CalibLoadedData, run_calib_dco_body


class Ignite64NotMappedYet(NotImplementedError):
    pass


def _update_masked_byte(old: int, *, mask: int, value: int) -> int:
    return (old & (~mask & 0xFF)) | (value & mask)


@dataclass(frozen=True)
class MatRegs:
    # From the C# grid mapping: reg = row*16 + col
    CH_MODE_41: int = 65  # row4 col1
    CH_MODE_42: int = 66  # row4 col2
    AFE_BIAS0: int = 68  # row4 col4 (IDISC/ICSA + flags)
    CAL_CONF: int = 67  # EN_CON_PAD etc (see C# Ignite32_Mat_CAL_conf_noUI)
    VINJ_MUX: int = 69  # row4 col5
    DAC_VTH_H: int = 70  # row4 col6
    DAC_VTH_L: int = 71  # row4 col7
    DAC_VINJ_H: int = 72  # row4 col8
    DAC_VINJ_L: int = 73  # row4 col9
    DAC_VLDO: int = 74  # row4 col10
    DAC_VFB: int = 75  # row4 col11
    FT_BASE: int = 76  # 76..107, 2 pixels per byte
    TDC_DCO0: int = 64  # TDC/DCO0 config (TDCON bit6, DE_ON bit7)
    MAT_COMMAND: int = 112  # CalSelDCO + group enables (C# Ignite32_Mat_Command_noUI)


_DAC_NAME_TO_REG = {
    "VTHR_H": MatRegs.DAC_VTH_H,
    "VTH_H": MatRegs.DAC_VTH_H,
    "VTHR_L": MatRegs.DAC_VTH_L,
    "VTH_L": MatRegs.DAC_VTH_L,
    "VINJ_H": MatRegs.DAC_VINJ_H,
    "VINJH": MatRegs.DAC_VINJ_H,
    "VINJ_L": MatRegs.DAC_VINJ_L,
    "VINJL": MatRegs.DAC_VINJ_L,
    "VLDO": MatRegs.DAC_VLDO,
    "VFB": MatRegs.DAC_VFB,
}


class Ignite64(Ignite64LowLevel):
    """
    High-level API with names aligned to the user request.
    """

    # ---------------------------
    # FIFO readout (TOP)
    # ---------------------------

    def FifoReadSingle(self) -> int:
        """
        Read one 64-bit FIFO word via I2C readout interface.

        Mapping from C# `FifoReadSingle()`:
          I2C_ReadArray(dev=TOP(0xFC), subaddr=0x40, n=8) and interpret as little-endian u64.

        Vendor return code rc==3 is treated as "FIFO empty" and returns 0.
        """
        dev = int(self.addr.top_addr) & 0xFF
        rc, data = self.i2c_read_bytes_rc(dev, 0x40, 8)
        if rc == 0 and len(data) >= 8:
            return int.from_bytes(data[:8], byteorder="little", signed=False)
        if rc == 3:
            return 0
        raise Ignite64TransportError(f"FifoReadSingle: I2C_ReadArray(dev=0x{dev:02X}, sub=0x40, n=8) -> {rc}")

    def FifoReadNumWords(self, n_words: int) -> list[int]:
        """
        Read up to 24 FIFO words in one burst.

        Mapping from C# `FifoReadNumWords(fifo_number)`:
          reads (fifo_number+1) words; where fifo_number is 0..23.

        Here `n_words` is 1..24.
        """
        n_words = int(n_words)
        if n_words < 1 or n_words > 24:
            raise ValueError("n_words out of range (expected 1..24)")
        dev = int(self.addr.top_addr) & 0xFF
        nbytes = 8 * n_words
        rc, data = self.i2c_read_bytes_rc(dev, 0x40, nbytes)
        if rc == 3:
            return []
        if rc != 0:
            raise Ignite64TransportError(
                f"FifoReadNumWords: I2C_ReadArray(dev=0x{dev:02X}, sub=0x40, n={nbytes}) -> {rc}"
            )
        out: list[int] = []
        for i in range(n_words):
            chunk = data[i * 8 : (i + 1) * 8]
            if len(chunk) < 8:
                break
            out.append(int.from_bytes(chunk, byteorder="little", signed=False))
        return out

    def FifoDrain(self, *, max_words: int = 4096) -> list[int]:
        """
        Read FIFO until empty (or max_words reached). Returns list of u64 raw words.
        """
        max_words = int(max_words)
        if max_words < 0:
            raise ValueError("max_words must be >= 0")
        out: list[int] = []
        for _ in range(max_words):
            w = int(self.FifoReadSingle())
            if w == 0:
                break
            out.append(w)
        return out

    # ---------------------------
    # Snapshot / monitoring (readback of registers)
    # ---------------------------

    def snapshot_full_configuration(self, quad: str, *, path: Union[str, Path]) -> None:
        """
        Read back the current register state and save it in the same text format as the C# GUI
        "full configuration" file:

          Quadrant XX
          <Cur_Quad index>
          TOP
          <19 lines>
          MAT 0
          <108 lines>
          ...
          I/O Ext & I2C Mux Registers
          <11 lines>

        This is intended for monitoring / GUI snapshots.
        """
        q = str(quad).strip().upper()
        quad_to_idx = {"SW": 0, "NW": 1, "SE": 2, "NE": 3}
        if q not in quad_to_idx:
            raise ValueError("quad must be SW/NW/SE/NE")

        self.select_quadrant(q)
        time.sleep(0.05)

        # TOP (19 bytes, regs 0..18)
        top = list(self.i2c_read_bytes(self.addr.top_addr, 0, 19))

        # MATs (always 0..15, 108 bytes each, regs 0..107)
        mats: dict[int, list[int]] = {}
        for mat_id in range(16):
            dev = self.matid_to_devaddr(mat_id)
            mats[mat_id] = list(self.i2c_read_bytes(dev, 0, 108))

        # IOext (11 bytes, regs 0..10) – use detected IOext address
        try:
            self.autodetect_ioext_address()
        except Exception:
            pass
        ioext = list(self.i2c_read_bytes(self.addr.ioext_addr, 0, 11))

        p = Path(path)
        lines: list[str] = []
        lines.append(f"Quadrant {q}")
        lines.append(str(quad_to_idx[q]))
        lines.append("")
        lines.append("TOP")
        lines.extend(str(int(b)) for b in top)
        lines.append("")
        lines.append("")
        for mat_id in range(16):
            lines.append(f"MAT {mat_id} ")
            lines.extend(str(int(b)) for b in mats[mat_id])
            lines.append("")
            lines.append("")
        lines.append("I/O Ext & I2C Mux Registers")
        lines.extend(str(int(b)) for b in ioext)
        lines.append("")
        p.write_text("\n".join(lines), encoding="utf-8")

    def CalibrateFTDAC(self, quad: str, Mattonella: int, Channel) -> dict[str, object]:
        """
        Calibrate FineTune DAC threshold for a single channel using FIFO activity.

        Algorithm (as described):
        - Set all FineTune DACs to max (15) for the selected MAT
        - Enable only the selected pixel and the MAT TDC
        - Read FIFO; while empty, decrement FT code for that channel and retry
        - When FIFO becomes non-empty, keep the PREVIOUS code as calibrated (lowest threshold)
        """
        def _decode_fifo_word(w: int) -> dict[str, int]:
            """
            Decode FIFO raw word following C# `RawDataToObjArray` mapping.
            Returns key fields needed for logs: fifo_status, quad_fifo, mat, pix.
            """
            w = int(w) & ((1 << 64) - 1)
            fifo_status = int((w >> 47) & 0x1)
            quad_fifo = int((w >> 48) & 0xFF)
            mat = int((w >> 43) & 0xF)
            pix = int((((w >> 40) & 0x7) * 8) + ((w >> 37) & 0x7))
            return {"fifo_status": fifo_status, "quad_fifo": quad_fifo, "mat": mat, "pix": pix}

        def _clear_fifo() -> None:
            _ = self.FifoDrain(max_words=4096)

        def _calibrate_one(ch: int) -> dict[str, object]:
            if ch < 0 or ch > 63:
                raise ValueError("Channel out of range (expected 0..63)")

            print(f"Finding threshold channel {ch} Mattonella {mat} (quad={quad})")

            # Disable all pixels in this MAT, disable TDC, then enable only target.
            for pix in range(64):
                self.EnableDigPix(quad, Mattonella=mat, Channel=pix, enable=False)
            self.EnableTDC(quad, Mattonella=mat, enable=False)

            self.EnableDigPix(quad, Mattonella=mat, Channel=ch, enable=True)
            self.EnableTDC(quad, Mattonella=mat, enable=True)

            # Scan: start from 15, decrement until we see data.
            prev_code: Optional[int] = None
            for code in range(15, -1, -1):
                self.AnalogChannelFineTune(quad, block=0, mattonella=mat, canale=ch, valore=code)
                print(f"ftdac code {code}")

                _clear_fifo()
                time.sleep(0.01)
                w = int(self.FifoReadSingle())

                if w == 0:
                    prev_code = code
                    continue

                calibrated = 15 if prev_code is None else int(prev_code)
                self.AnalogChannelFineTune(quad, block=0, mattonella=mat, canale=ch, valore=calibrated)

                d = _decode_fifo_word(w)
                print("hit found!")
                print("raw data decoded:")
                print(f"  fifo_status: {d['fifo_status']}")
                print(f"  quad_fifo:   {d['quad_fifo']}")
                print(f"  Mattonella:  {d['mat']}")
                print(f"  Channel:     {d['pix']}")
                print(f"set ftdac code {calibrated}")
                return {
                    "quad": quad,
                    "mat": mat,
                    "channel": ch,
                    "calibrated_code": calibrated,
                    "first_word": w,
                    "first_word_decoded": d,
                }

            return {
                "quad": quad,
                "mat": mat,
                "channel": ch,
                "calibrated_code": None,
                "first_word": 0,
                "error": "FIFO stayed empty down to FT code 0",
            }

        quad = str(quad).strip().upper()
        mat = int(Mattonella)
        if mat < 0 or mat > 15:
            raise ValueError("Mattonella out of range (expected 0..15)")

        # Ensure I2C readout is selected on TOP (needed for FIFO reads).
        try:
            self.TopReadout("i2c")
        except (Ignite64TransportError, OSError, ValueError, RuntimeError, AttributeError):
            pass

        self.select_quadrant(quad)

        # Set all FT DACs to max for this MAT once (15)
        for pix in range(64):
            self.AnalogChannelFineTune(quad, block=0, mattonella=mat, canale=pix, valore=15)

        # Channel selector: int 0..63 or "ALL"
        if isinstance(Channel, str) and Channel.strip().upper() == "ALL":
            results: dict[int, dict[str, object]] = {}
            for ch in range(64):
                results[ch] = _calibrate_one(ch)
            return {"quad": quad, "mat": mat, "channels": results}

        ch = int(Channel)
        return _calibrate_one(ch)

    def _set_connect2pad_only(self, quad: str, *, block: int) -> None:
        """
        Force EN_CON_PAD false for all MATs, then enable only for `block`.
        Best-effort: if some MATs are not reachable, skip them.
        """
        self.select_quadrant(quad)
        for mat_id in range(16):
            dev = self.matid_to_devaddr(mat_id)
            try:
                old = self.i2c_read_byte(dev, MatRegs.CAL_CONF)
            except Ignite64TransportError:
                continue
            want = 0x80 if mat_id == int(block) else 0x00
            new = _update_masked_byte(old, mask=0x80, value=want)
            try:
                self.i2c_write_byte(dev, MatRegs.CAL_CONF, new)
            except Ignite64TransportError:
                continue

    # ---------------------------
    # TOP-level helpers (no quadrant argument)
    # ---------------------------

    def TopDriverSTR(self, valore: int) -> None:
        """
        Set SLVS driver strength (TOP reg4 high nibble).
        C# mapping: TOP[4] = 16*SLVS_DRV_STR + SLVS_CMM_MODE
        """
        if valore < 0 or valore > 15:
            raise ValueError("TopDriverSTR out of range (expected 0..15)")
        reg = 4
        old = self.i2c_read_byte(self.addr.top_addr, reg)
        new = _update_masked_byte(old, mask=0xF0, value=(int(valore) & 0x0F) << 4)
        self.i2c_write_byte(self.addr.top_addr, reg, new)

    def readTopDriverSTR(self) -> int:
        """
        Read SLVS driver strength (TOP reg4 high nibble).
        Returns 0..15.
        """
        b = self.i2c_read_byte(self.addr.top_addr, 4)
        return (b >> 4) & 0x0F

    def TopReadout(self, interface: str) -> None:
        """
        Select readout interface (TOP reg13 bits5..4).
        C# mapping: TOP[13] = 16*SEL_RO + SER_CK_SEL, where:
          SEL_RO: 2=I2C Readout, 3=SER Readout
        """
        key = interface.strip().lower()
        if key in ("i2c", "i2c_readout"):
            sel = 2
        elif key in ("ser", "serializer", "ser_readout"):
            sel = 3
        elif key in ("none", "off"):
            sel = 0
        else:
            raise ValueError("TopReadout interface must be 'i2c' or 'ser' (or 'none')")
        reg = 13
        old = self.i2c_read_byte(self.addr.top_addr, reg)
        new = _update_masked_byte(old, mask=0x30, value=(sel & 0x03) << 4)
        self.i2c_write_byte(self.addr.top_addr, reg, new)

    def readTopReadout(self) -> str:
        """
        Read readout interface (TOP reg13 bits5..4).
        Returns: "none" | "i2c" | "ser" | "unknown(<n>)"
        """
        b = self.i2c_read_byte(self.addr.top_addr, 13)
        sel = (b >> 4) & 0x03
        if sel == 0:
            return "none"
        if sel == 2:
            return "i2c"
        if sel == 3:
            return "ser"
        return f"unknown({sel})"

    def TopSLVS(self, mode: str) -> None:
        """
        Select GPO SLVS output (TOP reg12 high nibble).
        C# items: 1=HitOr, 2=CLK 40 MHz.
        """
        key = mode.strip().lower()
        if key in ("clk40", "clk_40", "clk 40", "clk40mhz", "clk_40mhz"):
            idx = 2
        elif key in ("hitor", "hit_or", "hit-or"):
            idx = 1
        else:
            raise ValueError("TopSLVS mode must be 'clk40' or 'hitor'")
        reg = 12
        old = self.i2c_read_byte(self.addr.top_addr, reg)
        new = _update_masked_byte(old, mask=0xF0, value=(idx & 0x0F) << 4)
        self.i2c_write_byte(self.addr.top_addr, reg, new)

    def readTopSLVS(self) -> str:
        """
        Read GPO SLVS output select (TOP reg12 high nibble).
        Returns: "hitor" | "clk40" | "idx(<n>)"
        """
        b = self.i2c_read_byte(self.addr.top_addr, 12)
        idx = (b >> 4) & 0x0F
        if idx == 1:
            return "hitor"
        if idx == 2:
            return "clk40"
        return f"idx({idx})"

    def TopFePolarity(self, polarity: str) -> None:
        """
        FE polarity (TOP reg5 bit7).
        C# items: 0=Active Low, 1=Active High.
        """
        key = polarity.strip().lower()
        if key in ("low", "active_low", "active low", "0"):
            v = 0
        elif key in ("high", "active_high", "active high", "1"):
            v = 1
        else:
            raise ValueError("TopFePolarity must be 'high' or 'low'")
        reg = 5
        old = self.i2c_read_byte(self.addr.top_addr, reg)
        new = _update_masked_byte(old, mask=0x80, value=(v & 1) << 7)
        self.i2c_write_byte(self.addr.top_addr, reg, new)

    def readTopFePolarity(self) -> str:
        """
        Read FE polarity (TOP reg5 bit7).
        Returns: "low" (Active Low) or "high" (Active High).
        """
        b = self.i2c_read_byte(self.addr.top_addr, 5)
        return "high" if ((b >> 7) & 1) else "low"

    def readTopSnapshot(self, quad: str) -> dict[str, object]:
        """
        Read decoded TOP registers for one physical quadrant.

        TOP device address is shared; the quadrant must be selected first (same as full-configuration snapshot).

        Returns keys: ``quad``, ``driver_str``, ``readout``, ``slvs``, ``fe_polarity``, ``start_tp``,
        ``top_reg9``, ``top_reg10``, ``top_reg11`` (raw bytes; regs 9–11 relate to AFE pulse / TP in C# GUI).
        """
        q = str(quad).strip().upper()
        allowed = ("NW", "NE", "SW", "SE")
        if q not in allowed:
            raise ValueError(f"quad must be one of {allowed}")
        self.select_quadrant(q)
        time.sleep(0.03)
        raw = list(self.i2c_read_bytes(self.addr.top_addr, 0, 19))
        b11 = raw[11] if len(raw) > 11 else 0
        tp: dict[str, int | bool] = {
            "start": ((b11 >> 6) & 1) == 1,
            "repetition": b11 & 0x3F,
            "eos": ((b11 >> 7) & 1) == 1,
        }
        return {
            "quad": q,
            "driver_str": int(self.readTopDriverSTR()),
            "readout": str(self.readTopReadout()),
            "slvs": str(self.readTopSLVS()),
            "fe_polarity": str(self.readTopFePolarity()),
            "start_tp": tp,
            "top_reg9": int(raw[9]) if len(raw) > 9 else None,
            "top_reg10": int(raw[10]) if len(raw) > 10 else None,
            "top_reg11": int(raw[11]) if len(raw) > 11 else None,
        }

    # ---------------------------
    # MAT test-mode helpers (CH41/CH42)
    # ---------------------------

    def Hitor(self, quad: str, *, mattonella: int, valore: str) -> None:
        """
        Configure MAT test mode for CH41/CH42 to output DAQ TMR GPO or DAQ HIT-OR.

        C# mapping (MAT regs):
          reg65 = 64*CH_MODE_41 + CH_SEL_41
          reg66 = 64*CH_MODE_42 + CH_SEL_42
        CH_MODE index: 0="DAQ TMR GPO", 1="DAQ HIT-OR"
        """
        key = valore.strip().upper()
        if key in ("DAQTMR", "DAQ_TMR", "DAQ TMR", "TMR"):
            mode = 0
        elif key in ("HITOR", "HIT_OR", "HIT-OR"):
            mode = 1
        else:
            raise ValueError("Hitor valore must be 'DAQTMR' or 'HITOR'")
        self.select_quadrant(quad)
        self._mat_set_ch_mode(mattonella, reg=MatRegs.CH_MODE_41, mode=mode, selector=None)
        self._mat_set_ch_mode(mattonella, reg=MatRegs.CH_MODE_42, mode=mode, selector=None)

    def readHitor(self, quad: str, *, mattonella: int) -> dict[str, object]:
        """
        Read CH41/CH42 test mode configuration (MAT regs 65/66).
        Returns dict with mode/selector for each channel and a convenience "value"
        when both channels match and map to DAQTMR/HITOR.
        """
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(mattonella)
        b41 = self.i2c_read_byte(dev, MatRegs.CH_MODE_41)
        b42 = self.i2c_read_byte(dev, MatRegs.CH_MODE_42)
        m41, s41 = (b41 >> 6) & 0x03, b41 & 0x3F
        m42, s42 = (b42 >> 6) & 0x03, b42 & 0x3F
        value = None
        if m41 == m42:
            if m41 == 0:
                value = "DAQTMR"
            elif m41 == 1:
                value = "HITOR"
        return {
            "CH41": {"mode": m41, "selector": s41},
            "CH42": {"mode": m42, "selector": s42},
            "value": value,
        }

    def ATPulse(self, quad: str, *, mattonella: int, canale: int) -> None:
        """
        Configure MAT test mode for CH41/CH42 as "ATP Pulse" and select the channel (0..63).
        C# CH_MODE index: 3="ATP Pulse"
        """
        if canale < 0 or canale > 63:
            raise ValueError("ATPulse canale out of range (expected 0..63)")
        self.select_quadrant(quad)
        self._mat_set_ch_mode(mattonella, reg=MatRegs.CH_MODE_41, mode=3, selector=int(canale))
        self._mat_set_ch_mode(mattonella, reg=MatRegs.CH_MODE_42, mode=3, selector=int(canale))

    def readATPulse(self, quad: str, *, mattonella: int) -> dict[str, object]:
        """
        Read CH41/CH42 test mode configuration (MAT regs 65/66), with special handling
        for ATPulse (mode=3) and returning the selected channel if both match.
        """
        d = self.readHitor(quad, mattonella=mattonella)
        ch = None
        try:
            m41 = int(d["CH41"]["mode"])  # type: ignore[index]
            s41 = int(d["CH41"]["selector"])  # type: ignore[index]
            m42 = int(d["CH42"]["mode"])  # type: ignore[index]
            s42 = int(d["CH42"]["selector"])  # type: ignore[index]
            if m41 == m42 == 3 and s41 == s42:
                ch = s41
        except Exception:
            pass
        d["channel"] = ch
        return d

    def _mat_set_ch_mode(self, mat_id: int, *, reg: int, mode: int, selector: Optional[int]) -> None:
        if mode < 0 or mode > 3:
            raise ValueError("mode out of range (expected 0..3)")
        dev = self.matid_to_devaddr(mat_id)
        old = self.i2c_read_byte(dev, int(reg))
        sel = (old & 0x3F) if selector is None else (int(selector) & 0x3F)
        new = ((int(mode) & 0x03) << 6) | sel
        self.i2c_write_byte(dev, int(reg), new)

    # ---------------------------
    # Analog bias / IREF
    # ---------------------------

    def AnalogColumnBiasCell(self, quad: str, *, block: int, csa: int, disc: int, krum: int) -> None:
        """
        Set MAT AFE bias DACs:
          - IDISC (0..7) and ICSA (0..7) in MAT reg68
          - IKRUM (0..15) in MAT reg69 low nibble
        Preserves other bits (AUTO/LB, VINJ mux, EXT_DC, EN_P_VINJ, etc).
        """
        if csa < 0 or csa > 7:
            raise ValueError("csa out of range (expected 0..7)")
        if disc < 0 or disc > 7:
            raise ValueError("disc out of range (expected 0..7)")
        if krum < 0 or krum > 15:
            raise ValueError("krum out of range (expected 0..15)")
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(block)
        # reg68: bits5..3 IDISC, bits2..0 ICSA (preserve bits7..6)
        old68 = self.i2c_read_byte(dev, MatRegs.AFE_BIAS0)
        v68 = ((int(disc) & 0x07) << 3) | (int(csa) & 0x07)
        new68 = _update_masked_byte(old68, mask=0x3F, value=v68)
        self.i2c_write_byte(dev, MatRegs.AFE_BIAS0, new68)
        # reg69: low nibble IKRUM, preserve upper bits (VINJ mux, etc)
        old69 = self.i2c_read_byte(dev, MatRegs.VINJ_MUX)
        new69 = _update_masked_byte(old69, mask=0x0F, value=int(krum) & 0x0F)
        self.i2c_write_byte(dev, MatRegs.VINJ_MUX, new69)

    def AnalogColumnConnect2PAD(self, quad: str, *, block: int, valore: bool = True) -> None:
        """
        Enable/disable "connect to pad" for a MAT (MAT reg67 bit7).
        Mapping from C# `Ignite32_Mat_CAL_conf_noUI`: EN_CON_PAD is bit7 of reg 67.
        """
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(block)
        old = self.i2c_read_byte(dev, MatRegs.CAL_CONF)
        new = _update_masked_byte(old, mask=0x80, value=(0x80 if valore else 0x00))
        self.i2c_write_byte(dev, MatRegs.CAL_CONF, new)

    def readAnalogColumnConnect2PAD(self, quad: str, *, block: int) -> bool:
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(block)
        b = self.i2c_read_byte(dev, MatRegs.CAL_CONF)
        return (b & 0x80) != 0

    def _adc_quad_oneshot(self, *, channel: int, gain: int = 0, res_bits: int = 16, delay_s: float = 0.01) -> dict[str, object]:
        """
        Mirror C# WriteAdc_noUI(Quad=true) + ReadAdc_noUI.

        Channels (C# ADCdac_Quad_ch_comboBox):
          0 Vthr_H, 1 Vthr_L, 2 Vinj_H, 3 Vref_L, 4 Vfeed, 5 Vref, 6 V_Icap, 7 V_Iref
        """
        if channel < 0 or channel > 7:
            raise ValueError("ADC channel out of range (expected 0..7)")
        if gain < 0 or gain > 3:
            raise ValueError("ADC gain out of range (expected 0..3)")
        if res_bits not in (12, 14, 16):
            raise ValueError("ADC res_bits must be 12, 14, or 16")

        # address selection like C#:
        # if channel <= 3 -> base address, else base+2 and subtract 4 from channel
        if channel <= 3:
            dev = 208  # 0xD0
            ch = channel
        else:
            dev = 210  # 0xD2
            ch = channel - 4

        # res index: 12->0, 14->1, 16->2 (stored in bits3..2)
        res_idx = {12: 0, 14: 1, 16: 2}[res_bits]

        # config byte: bit7 RDY=1, bits6..5 channel, bit4 OC=0, bits3..2 res, bits1..0 gain
        cfg = 0x80 | ((ch & 0x03) << 5) | ((0 & 0x01) << 4) | ((res_idx & 0x03) << 2) | (gain & 0x03)

        # write config via "send byte" (no subaddress)
        rc = self.i2c_send_byte(dev, cfg)
        if rc != 0:
            raise RuntimeError(f"I2C_SendByte(ADC dev=0x{dev:02X}, cfg=0x{cfg:02X}) -> {rc}")

        time.sleep(max(0.0, float(delay_s)))

        raw = self.i2c_read_raw(dev, 3, send_stop=1)
        if len(raw) != 3:
            raise RuntimeError(f"ADC raw read length mismatch: {len(raw)}")
        code = (raw[0] << 8) | raw[1]

        # C# scaling constant array2 = [1.0, 0.25, 0.0625] and then /2^gain
        lsb_v = [1.0, 0.25, 0.0625][res_idx]
        value = float(code) * lsb_v / (2.0 ** float(gain))
        return {"dev": dev, "cfg": cfg, "code": code, "value": value, "raw": raw}

    def measureVDDA(self, quad: str, *, block: int) -> float:
        """
        Measure VDDA by routing VinjH to VDDA, enabling connect-to-pad, and reading VinjH on the Quad ADC.
        Uses ADC: 16-bit, gain x1 (gain=0).
        Prints the measured value and returns it.
        """
        self.select_quadrant(quad)
        # route VinjH to VDDA
        self.AnalogColumnVinjMux(quad, block=block, vinj="VinjH", valore="VDDA")
        # connect-to-pad: ensure ONLY this MAT has EN_CON_PAD enabled
        self._set_connect2pad_only(quad, block=block)
        # one-shot ADC read: Vinj_H is channel 2
        r = self._adc_quad_oneshot(channel=2, gain=0, res_bits=16, delay_s=0.01)
        v = float(r["value"])
        print(f"VDDA (quad={quad}, block={block}): {v}")
        return v

    def TuneVDDA(
        self,
        quad: str,
        *,
        block: int,
        target_v: float = 0.9,
        step: int = 1,
        max_code: int = 127,
        max_iters: int = 128,
        settle_s: float = 0.02,
        start_code: Optional[int] = None,
    ) -> dict[str, float | int]:
        """
        Simple closed-loop tune of VDDA using the internal VLDO DAC:

        - measureVDDA()
        - if VDDA < target_v, increment VLDO code by `step`
        - repeat until VDDA >= target_v (or safety limits hit)

        Returns {"vdda": <float>, "code": <int>}.
        """
        if target_v <= 0:
            raise ValueError("target_v must be > 0")
        if step <= 0:
            raise ValueError("step must be >= 1")
        if max_code < 0 or max_code > 127:
            raise ValueError("max_code out of range (expected 0..127)")
        if max_iters < 1:
            raise ValueError("max_iters must be >= 1")
        if start_code is not None and (start_code < 0 or start_code > 127):
            raise ValueError("start_code out of range (expected 0..127)")

        # Ensure we operate on the requested quadrant.
        self.select_quadrant(quad)

        # connect-to-pad: ensure ONLY this MAT has EN_CON_PAD enabled
        self._set_connect2pad_only(quad, block=block)

        # Make sure VLDO DAC is enabled.
        self.AnalogColumnDACon(quad, block=block, dac="VLDO", valore=True)

        # Determine starting code.
        if start_code is not None:
            code = int(start_code)
            self.AnalogColumnSetDAC(quad, block=block, dac="VLDO", valore=code)
        else:
            code = int(self.readAnalogColumnDAC(quad, block=block, dac="VLDO")["code"])

        import time

        for _i in range(int(max_iters)):
            v = float(self.measureVDDA(quad, block=block))
            if v >= float(target_v):
                print(f"TuneVDDA OK: VDDA={v} VLDO_code={code}")
                return {"vdda": v, "code": int(code)}

            next_code = int(code) + int(step)
            if next_code > int(max_code):
                raise RuntimeError(
                    f"TuneVDDA failed: VDDA={v} < {target_v}, but VLDO code would exceed max_code={max_code} "
                    f"(current={code}, step={step})"
                )
            code = next_code
            self.AnalogColumnSetDAC(quad, block=block, dac="VLDO", valore=int(code))
            time.sleep(max(0.0, float(settle_s)))

        raise RuntimeError(f"TuneVDDA failed: exceeded max_iters={max_iters} (last_code={code})")

    def readAnalogColumnBiasCell(self, quad: str, *, block: int) -> dict[str, int]:
        """
        Read MAT AFE bias DACs (IDISC/ICSA/IKRUM) from MAT regs 68/69.
        """
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(block)
        b68 = self.i2c_read_byte(dev, MatRegs.AFE_BIAS0)
        b69 = self.i2c_read_byte(dev, MatRegs.VINJ_MUX)
        return {
            "disc": (b68 >> 3) & 0x07,
            "csa": b68 & 0x07,
            "krum": b69 & 0x0F,
        }

    def AnalogSetIREF(self, valore_mv: float) -> None:
        """
        Write external IREF DAC (C# "Write Iref").

        Raw I2C write to dev 0x20:
          [0x31, code_hi, code_lo]

        Code computation matches C# (scaled to VDDA). Here we assume VDDA=1200 mV.
        """
        vdda_mv = 1200.0
        v = float(valore_mv)
        if v < 0:
            raise ValueError("IREF must be >= 0 mV")
        if v > vdda_mv:
            v = vdda_mv
        code = int(v * ((2**16) - 1) / vdda_mv)
        hi = (code >> 8) & 0xFF
        lo = code & 0xFF
        self.i2c_write_raw(0x20, [0x31, hi, lo], send_stop=1)

    def readAnalogIREF(self):
        raise Ignite64NotMappedYet("AnalogSetIREF: readback is not implemented (external DAC read protocol unknown).")

    def start_config(
        self,
        quadrant: str,
        *,
        preferred_serial: Optional[int] = 5284,
        retry_enumeration: bool = True,
        cfg_dir: Optional[Union[str, Path]] = None,
        full_cfg: str = "IGNITE64_configSW_26.04.28.17.04.47.txt",
        si5340_cfg: str = "Si5340-RevD_Crystal-Registers_bis.txt",
        apply_ioext_regs: list[int] = [9, 10],
        mux_settle_s: float = 0.05,
    ) -> int:
        """
        Minimal bring-up sequence (no popups): USB select -> IOext -> mux -> clock -> TOP/MATs.

        - `quadrant`: "SW"/"NW"/"SE"/"NE" or "ALL" to apply same TOP/MAT config to all quadrants.
        - Uses default config files under `ConfigurationFiles/` unless overridden.

        Returns selected USBtoI2C serial number.
        """
        # 0) USB bring-up (skip I2C_SetFrequency + GPIO init: they trigger popups on some setups)
        selected_serial = self.init_sandrobox_usb(
            preferred_serial=preferred_serial,
            i2c_frequency_hz=None,
            do_gpio_init=False,
            do_ioext_init=False,
            do_bus_recovery=False,
            retry_enumeration=bool(retry_enumeration),
        )
        print(f"USB serial: {selected_serial}")

        # Resolve config paths
        if cfg_dir is None:
            cfg_base = Path(__file__).resolve().parents[1] / "ConfigurationFiles"
        else:
            cfg_base = Path(cfg_dir)
        full_cfg_path = cfg_base / full_cfg
        si5340_cfg_path = cfg_base / si5340_cfg

        if not full_cfg_path.exists():
            raise FileNotFoundError(f"Missing full configuration file: {full_cfg_path}")
        if not si5340_cfg_path.exists():
            raise FileNotFoundError(f"Missing SI5340 configuration file: {si5340_cfg_path}")

        # Parse once: we may apply to multiple quadrants.
        cfg = parse_full_configuration(str(full_cfg_path))

        # 1) IOext defaults + only key registers from file
        self.ioext_init_defaults()
        print(f"IOext addr: 0x{self.addr.ioext_addr:02X}")
        if apply_ioext_regs:
            dev = self.addr.ioext_addr
            for r in apply_ioext_regs:
                r = int(r)
                if r < 0 or r >= len(cfg.ioext):
                    raise ValueError(f"IOext reg out of range for config: {r}")
                self.i2c_write_byte(dev, r, int(cfg.ioext[r]) & 0xFF)

        # 2) Clock (global, not quadrant-dependent)
        self.loadClockSetting(str(si5340_cfg_path))
        print("SI5340: configured")

        # 3) Select quadrant(s) and write TOP + MATs
        q = quadrant.strip().upper()
        if q == "ALL":
            quads = ["SW", "NW", "SE", "NE"]
        else:
            quads = [q]

        for qq in quads:
            self.select_quadrant(qq)
            time.sleep(max(0.0, float(mux_settle_s)))
            print(f"Mux addr: 0x{self.addr.mux_addr:02X} (quad={qq})")
            # unlock + write
            self.unlock_top_default_config(retries=5, delay_s=0.1)
            print(f"TOP addr: 0x{self.addr.top_addr:02X}")
            self._write_block_bytewise(self.addr.top_addr, 0, cfg.top)
            for mat_id, data in sorted(cfg.mats.items()):
                dev = self.matid_to_devaddr(mat_id)
                self._write_block_bytewise(dev, 0, data)

        return int(selected_serial)

    # ---------------------------
    # Basic pixel/channel control
    # ---------------------------

    def EnableDigPix(self, quad: str, *, Mattonella: int, Channel: int, enable: bool = True) -> None:
        """
        Enable/disable digital pixel output (PIXON bit6) for a given pixel/channel.

        Mapping from C# `Ignite32_Mat_PIX_conf_noUI`: per-pixel register = PixID, bit6 is PIXON.
        """
        self.select_quadrant(quad)
        self._pix_set_onoff(Mattonella, Channel, on=bool(enable))

    def readEnableDigPix(self, quad: str, *, Mattonella: int, Channel: int) -> bool:
        """
        Read PIXON bit6 for a given pixel/channel.
        """
        return self.readAnalogChannelON(quad, mattonella=Mattonella, canale=Channel)

    def EnableTDC(
        self,
        quad: str,
        *,
        Mattonella: int,
        enable: bool = True,
        double_edge: Optional[bool] = None,
    ) -> None:
        """
        Enable/disable TDC (TDCON bit6) for a MAT (MAT reg64).

        Mapping from C# `Ignite32_Mat_TDC_DCO0conf_noUI`:
          bit7 = DE_ON (double edge)
          bit6 = TDCON
          bits5..4 = adj
          bits3..0 = ctrl
        """
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(Mattonella)
        old = self.i2c_read_byte(dev, MatRegs.TDC_DCO0)
        new = old
        new = _update_masked_byte(new, mask=0x40, value=(0x40 if enable else 0x00))
        if double_edge is not None:
            new = _update_masked_byte(new, mask=0x80, value=(0x80 if double_edge else 0x00))
        self.i2c_write_byte(dev, MatRegs.TDC_DCO0, new)

    def readEnableTDC(self, quad: str, *, Mattonella: int) -> dict[str, bool]:
        """
        Read TDC enable and double-edge flags from MAT reg64.
        Returns {"tdc_on": bool, "double_edge": bool}.
        """
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(Mattonella)
        b = self.i2c_read_byte(dev, MatRegs.TDC_DCO0)
        return {"tdc_on": (b & 0x40) != 0, "double_edge": (b & 0x80) != 0}

    def AnalogChannelON(self, quad: str, *, mattonella: int, canale: int) -> None:
        self.select_quadrant(quad)
        self._pix_set_onoff(mattonella, canale, on=True)

    def AnalogChannelOFF(self, quad: str, *, mattonella: int, canale: int) -> None:
        self.select_quadrant(quad)
        self._pix_set_onoff(mattonella, canale, on=False)

    def _pix_set_onoff(self, mat_id: int, pix_id: int, *, on: bool) -> None:
        if pix_id < 0 or pix_id > 63:
            raise ValueError(f"pix_id out of range: {pix_id} (expected 0..63)")
        dev = self.matid_to_devaddr(mat_id)
        reg = pix_id  # C#: Ignite32_Mat_PIX_conf_noUI reads/writes at reg=PixID
        old = self.i2c_read_byte(dev, reg)
        # PIXON is bit6 in C#
        mask = 0x40
        new = _update_masked_byte(old, mask=mask, value=(0x40 if on else 0x00))
        self.i2c_write_byte(dev, reg, new)

    def readAnalogENPOW(self, quad: str, *, mattonella: int, canale: int) -> bool:
        """
        Lettura dello stato ON/OFF del canale/pixel (bit PIXON).
        """
        self.select_quadrant(quad)
        return self.readAnalogChannelON(quad, mattonella=mattonella, canale=canale)

    def readAnalogChannelON(self, quad: str, *, mattonella: int, canale: int) -> bool:
        self.select_quadrant(quad)
        if canale < 0 or canale > 63:
            raise ValueError(f"pix_id out of range: {canale} (expected 0..63)")
        dev = self.matid_to_devaddr(mattonella)
        reg = canale
        b = self.i2c_read_byte(dev, reg)
        return (b & 0x40) != 0

    def readAnalogPower(self) -> bool:
        """
        Stato Analog Power come nel C# (`IOext_gpio_refresh`):
        IOext reg 10 (dev 0x40), bit6 è invertito: Analog ON quando bit6 == 0.
        """
        # IOext address can be 0x40 or 0xAE; autodetect once if needed
        try:
            self.autodetect_ioext_address()
        except Exception:
            pass
        b = self.i2c_read_byte(self.addr.ioext_addr, 10)
        return ((b >> 6) & 1) != 1

    def setAnalogPower(self, on: bool) -> None:
        """
        Imposta lo stato Analog Power (IOext reg 10, bit6 invertito).

        C#:
          - AnaPwr_chkBox.Checked = ((num >> 6) & 1) != 1
          - quindi Analog Power ON quando bit6 == 0
        """
        try:
            self.autodetect_ioext_address()
        except Exception:
            # Best-effort: if autodetect fails, we still try current addr.ioext_addr.
            pass
        dev = int(self.addr.ioext_addr)
        b = int(self.i2c_read_byte(dev, 10)) & 0xFF
        if on:
            # clear bit6
            new = b & ~(1 << 6)
        else:
            # set bit6
            new = b | (1 << 6)
        self.i2c_write_byte(dev, 10, new & 0xFF)

    # ---------------------------
    # Internal DACs (per MAT)
    # ---------------------------

    def AnalogColumnSetDAC(self, quad: str, *, block: int, dac: str, valore: int) -> None:
        """
        Set internal DAC code for a MAT.

        Mapping from C# (`MAT_IN_DACset_change` / `MAT_DACset_refresh`):
        register holds: enable bit7 + code (0..127)

        Note: in your examples you pass `block=0`. Here we interpret `block` as MatID (0..15).
        """
        self.select_quadrant(quad)
        self._mat_set_dac_code(block, dac, valore)

    def AnalogColumnDACon(self, quad: str, *, block: int, dac: str, valore: bool) -> None:
        self.select_quadrant(quad)
        self._mat_set_dac_enable(block, dac, valore)

    def readAnalogColumnDAC(self, quad: str, *, block: int, dac: str) -> dict[str, int | bool]:
        """
        Legge enable e code del DAC interno (reg 70..75).
        """
        self.select_quadrant(quad)
        reg = self._dac_reg(dac)
        dev = self.matid_to_devaddr(block)
        b = self.i2c_read_byte(dev, reg)
        return {"enable": (b & 0x80) != 0, "code": b & 0x7F}

    def _mat_set_dac_code(self, mat_id: int, dac: str, code: int) -> None:
        if code < 0 or code > 127:
            raise ValueError(f"dac code out of range: {code} (expected 0..127)")
        reg = self._dac_reg(dac)
        dev = self.matid_to_devaddr(mat_id)
        old = self.i2c_read_byte(dev, reg)
        en = old & 0x80
        new = en | (code & 0x7F)
        self.i2c_write_byte(dev, reg, new)

    def _mat_set_dac_enable(self, mat_id: int, dac: str, enable: bool) -> None:
        reg = self._dac_reg(dac)
        dev = self.matid_to_devaddr(mat_id)
        old = self.i2c_read_byte(dev, reg)
        code = old & 0x7F
        new = (0x80 if enable else 0x00) | code
        self.i2c_write_byte(dev, reg, new)

    def _dac_reg(self, dac: str) -> int:
        key = dac.strip().upper()
        if key not in _DAC_NAME_TO_REG:
            raise ValueError(f"Unknown dac {dac!r}. Known: {sorted(_DAC_NAME_TO_REG.keys())}")
        return int(_DAC_NAME_TO_REG[key])

    # ---------------------------
    # Vinj mux (reg 69 bits)
    # ---------------------------

    def AnalogColumnVinjMux(self, quad: str, *, block: int, vinj: str, valore: str) -> None:
        """
        C# mapping (row4 col5 -> reg 69):
        - bit5: SEL_VINJ_MUX_High_comboBox.SelectedIndex (0..1)
        - bit4: SEL_VINJ_MUX_Low_comboBox.SelectedIndex (0..1)

        Accepted valore synonyms:
        - VinjH: "dac"/"750mV" -> 0, "VDDA" -> 1
        - VinjL: "dac"/"150mV" -> 0, "GND"/"GNDA" -> 1
        """
        self.select_quadrant(quad)
        mat_id = block
        dev = self.matid_to_devaddr(mat_id)
        reg = MatRegs.VINJ_MUX
        old = self.i2c_read_byte(dev, reg)

        vsel = self._vinj_value_to_sel(vinj, valore)
        if vinj.strip().upper() in {"VINJH", "VINJ_H"}:
            mask = 0x20
            new = _update_masked_byte(old, mask=mask, value=(0x20 if vsel else 0x00))
        elif vinj.strip().upper() in {"VINJL", "VINJ_L"}:
            mask = 0x10
            new = _update_masked_byte(old, mask=mask, value=(0x10 if vsel else 0x00))
        else:
            raise ValueError("vinj must be 'VinjH' or 'VinjL'")

        self.i2c_write_byte(dev, reg, new)

    def readAnalogColumnVinjMux(self, quad: str, *, block: int) -> dict[str, str]:
        """
        Ritorna la selezione mux per VinjH e VinjL (reg 69 bits 5 e 4).
        """
        self.select_quadrant(quad)
        dev = self.matid_to_devaddr(block)
        b = self.i2c_read_byte(dev, MatRegs.VINJ_MUX)
        vinjh = "VDDA" if (b & 0x20) else "dac"
        vinjl = "GNDA" if (b & 0x10) else "dac"
        return {"VinjH": vinjh, "VinjL": vinjl}

    def _vinj_value_to_sel(self, vinj: str, valore: str) -> int:
        v = valore.strip().upper()
        vh = vinj.strip().upper() in {"VINJH", "VINJ_H"}
        vl = vinj.strip().upper() in {"VINJL", "VINJ_L"}
        if not (vh or vl):
            raise ValueError("vinj must be 'VinjH' or 'VinjL'")

        if vh:
            if v in {"DAC", "750MV", "750", "0"}:
                return 0
            if v in {"VDDA", "VDDA (900 MV)", "900MV", "900", "1"}:
                return 1
        if vl:
            if v in {"DAC", "150MV", "150", "0"}:
                return 0
            if v in {"GND", "GNDA", "GND (0 MV)", "0MV", "1"}:
                return 1
        raise ValueError(f"Unsupported valore={valore!r} for vinj={vinj!r}")

    # ---------------------------
    # FineTune DAC (reg 76..107)
    # ---------------------------

    def AnalogChannelFineTune(self, quad: str, *, block: int, mattonella: int, canale: int, valore: int) -> None:
        """
        FineTune DAC per pixel: 4-bit.
        C# mapping (`MAT_DACset_refresh`): reg = 76 + pix//2, nibble low for even, high for odd.

        Note: `block` is currently unused (kept for signature compatibility).
        """
        self.select_quadrant(quad)
        if valore < 0 or valore > 15:
            raise ValueError(f"fine tune out of range: {valore} (expected 0..15)")
        if canale < 0 or canale > 63:
            raise ValueError(f"pix_id out of range: {canale} (expected 0..63)")

        mat_id = mattonella
        dev = self.matid_to_devaddr(mat_id)
        reg = MatRegs.FT_BASE + (canale // 2)
        old = self.i2c_read_byte(dev, reg)

        if canale % 2 == 0:
            # low nibble
            new = (old & 0xF0) | (valore & 0x0F)
        else:
            # high nibble
            new = (old & 0x0F) | ((valore & 0x0F) << 4)

        self.i2c_write_byte(dev, reg, new)

    def readMatPixelsAndFTDAC(self, quad: str, *, mattonella: int) -> dict[str, list[int] | list[bool]]:
        """
        Efficient readout for GUI monitoring.

        Returns:
          - pix_on: list[bool] length 64 (PIXON bit6 in regs 0..63)
          - ftdac:  list[int]  length 64 (FineTune DAC code 0..15 from regs 76..107)
        """
        self.select_quadrant(quad)
        m = int(mattonella)
        if m < 0 or m > 15:
            raise ValueError("mattonella out of range (expected 0..15)")
        # Some configuration snapshots may leave TOP readout interface not in I2C.
        # The GUI expects I2C-accessible MAT registers to be read back correctly.
        try:
            self.TopReadout("i2c")
        except Exception:
            pass
        dev = self.matid_to_devaddr(m)

        # Pixel regs 0..63: bit6 is PIXON
        pix_bytes = self.i2c_read_bytes(dev, 0x00, 64)
        pix_on = [((b & 0x40) != 0) for b in pix_bytes]

        # FineTune regs 76..107 inclusive: 32 bytes, 2 pixels per byte
        ft_bytes = self.i2c_read_bytes(dev, int(MatRegs.FT_BASE), 32)
        ftdac: list[int] = [0] * 64
        for i, bb in enumerate(ft_bytes):
            ftdac[2 * i] = bb & 0x0F
            ftdac[2 * i + 1] = (bb >> 4) & 0x0F

        return {"pix_on": pix_on, "ftdac": ftdac}

    # ---------------------------
    # TOP: StartTP (reg 11 bit6)
    # ---------------------------

    def StartTP(self, *, numberOfRepetition: int) -> None:
        """
        Mirror of C# `AFE_PULSE_Change` logic on TOP registers.

        - TOP reg 11: bit6 = Start TP, bits5..0 = repetition (0..63)
        """
        if numberOfRepetition < 0 or numberOfRepetition > 63:
            raise ValueError("numberOfRepetition out of range (expected 0..63)")

        reg = 11
        old = self.i2c_read_byte(self.addr.top_addr, reg)
        # keep bit7 as-is, set bit6=1, set low6bits=repetition
        new = (old & 0x80) | 0x40 | (numberOfRepetition & 0x3F)
        self.i2c_write_byte(self.addr.top_addr, reg, new)

    def readStartTP(self) -> dict[str, int | bool]:
        """
        Legge TOP reg 11: bit6 StartTP, bits5..0 repetition.
        """
        b = self.i2c_read_byte(self.addr.top_addr, 11)
        return {"start": ((b >> 6) & 1) == 1, "repetition": b & 0x3F, "eos": ((b >> 7) & 1) == 1}

    # ---------------------------
    # SI5340 clock configuration
    # ---------------------------

    def loadClockSetting(self, path: str) -> None:
        """
        Equivalent of C# `writeSI5340ConfFromFile`.

        Writes to SI5340 at I2C dev 0xEA (234):
        - set page: write page to reg 0x01
        - write value to target register on that page
        Supports "# Delay <ms> msec" lines.
        """
        writes = parse_si5340_config(path)
        dev = 0xEA
        for w in writes:
            if w.reg == 0xFF:
                # pseudo delay
                import time

                time.sleep(max(0, w.value) / 1000.0)
                continue
            self.i2c_write_byte(dev, 0x01, w.page)
            self.i2c_write_byte(dev, w.reg, w.value)

    # ---------------------------
    # Full configuration load (same as C# save/load)
    # ---------------------------

    def unlock_top_default_config(self, *, retries: int = 5, delay_s: float = 0.1) -> int:
        """
        Ensure TOP is in "no default config" state by writing TOP[0] = 0xDC (220).

        On power-up TOP[0] can be 0x00; the C# GUI requires switching it to 0xDC
        before other TOP registers become writable.

        Returns the TOP address (0xFC or 0xAE) that responded.
        """
        self.autodetect_top_address(retries=retries, delay_s=delay_s)
        self.i2c_write_byte(self.addr.top_addr, 0, 0xDC)
        # Best-effort readback
        try:
            rb = self.i2c_read_byte(self.addr.top_addr, 0)
            if rb != 0xDC:
                raise RuntimeError(f"TOP[0] readback mismatch: got 0x{rb:02X}, expected 0xDC")
        except Exception:
            pass
        return int(self.addr.top_addr)

    def _write_block_bytewise(self, dev_addr: int, start_reg: int, data: list[int]) -> None:
        """
        Write a block exactly like the C# GUI loader does: one register at a time with WriteByte.
        This avoids potential limitations of I2C_WriteArray on some devices/firmware.
        """
        for i, v in enumerate(data):
            self.i2c_write_byte(dev_addr, start_reg + i, int(v) & 0xFF)

    def load_full_configuration(self, path: str) -> None:
        """
        Load the exact text format saved by the C# GUI:
        - TOP: write 19 bytes to dev 0xFC, regs 0..18
        - MAT: for each MAT section present: write 108 bytes to dev (2*MatID), regs 0..107
        - IOext: write 11 bytes to dev 0x40, regs 0..10
        """
        cfg = parse_full_configuration(path)

        # Select quadrant if present in file
        if cfg.quadrant:
            self.select_quadrant(cfg.quadrant)

        # TOP
        self._write_block_bytewise(self.addr.top_addr, 0, cfg.top)

        # MATs (only those included in the file)
        for mat_id, data in sorted(cfg.mats.items()):
            dev = self.matid_to_devaddr(mat_id)
            self._write_block_bytewise(dev, 0, data)

        # I/O Ext & I2C mux registers (dev 0x40 like in C#)
        self._write_block_bytewise(0x40, 0, cfg.ioext)

    def load_ioext_and_mux_from_full_configuration(self, path: str) -> None:
        cfg = parse_full_configuration(path)
        # IOext only (dev 0x40 reg 0..10)
        self._write_block_bytewise(0x40, 0, cfg.ioext)
        # and select quadrant if present
        if cfg.quadrant:
            self.select_quadrant(cfg.quadrant)

    def apply_ioext_registers_from_full_configuration(self, path: str, regs: list[int]) -> None:
        """
        Write only selected IOext registers from a full configuration file.

        Useful when the full IOext image may temporarily break the I2C path,
        but a few control bits (e.g. reg9/reg10) are required to reach TOP.
        """
        cfg = parse_full_configuration(path)
        try:
            self.autodetect_ioext_address()
        except Exception:
            pass
        dev = self.addr.ioext_addr
        for r in regs:
            r = int(r)
            if r < 0 or r >= len(cfg.ioext):
                raise ValueError(f"IOext reg out of range for config: {r}")
            self.i2c_write_byte(dev, r, int(cfg.ioext[r]) & 0xFF)

    def load_top_and_mats_from_full_configuration(self, path: str) -> None:
        cfg = parse_full_configuration(path)
        if cfg.quadrant:
            self.select_quadrant(cfg.quadrant)
        # TOP address may differ by hardware (0xFC vs 0xAE). Probe before writing.
        self.unlock_top_default_config(retries=5, delay_s=0.1)
        # Then write the remaining TOP bytes (including re-writing reg0 if cfg.top[0] != 0xDC)
        self._write_block_bytewise(self.addr.top_addr, 0, cfg.top)
        for mat_id, data in sorted(cfg.mats.items()):
            dev = self.matid_to_devaddr(mat_id)
            self._write_block_bytewise(dev, 0, data)

    # ---------------------------
    # Placeholders for not-yet-mapped UI workflows
    # ---------------------------

    def run_calib_dco(
        self,
        params: CalibDCOParams,
        *,
        progress: Optional[object] = None,
    ) -> CalibLoadedData:
        """
        DCO / TDC calibration (C# `DCOcal` / `MultiTestSelForm` CalDCO).
        See `calib_dco.CalibDCOParams` and `calib_dco.run_calib_dco_body`.
        """
        return run_calib_dco_body(self, params, progress=progress)

    def CalibDCO(
        self,
        params: CalibDCOParams,
        *,
        progress: Optional[object] = None,
    ) -> CalibLoadedData:
        """Alias of `run_calib_dco` (same as C# menu name)."""
        return self.run_calib_dco(params, progress=progress)

    def AnalogChannelTestMode(self, *args, **kwargs):
        raise Ignite64NotMappedYet(
            "AnalogChannelTestMode: serve chiarire a quale registro corrisponde nel C# "
            "(nel tool ci sono modalità test su reg 65/66 per CH41/CH42, non per singolo pixel)."
        )

