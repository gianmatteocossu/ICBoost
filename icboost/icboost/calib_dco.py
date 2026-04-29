"""
DCO / TDC calibration (CalibDCO) — logic ported from `MainForm.cs` (`DCOcal`, `DCOcal47`,
`AddDataFromRaw`, `ReadUntilEmpty`, `RawDataToObjArray`).

Requires FIFO readout via I2C (`TopReadout("i2c")`) and the same mux/quadrant selection as the C# tool.
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Callable, Optional

from .device import Ignite64TransportError

# MAT registers (same as `MatRegs` in api.py)
_REG_CAL_CONF = 67
_REG_TDC_DCO0 = 64
_REG_MAT_COMMAND = 112


QUAD_INDEX_TO_STR = ("SW", "NW", "SE", "NE")

# C# uses MAT ID 254 as broadcast for DCOcal47
MAT_BROADCAST_ID = 254


def raw_fifo_to_fields(data_fifo_raw: int) -> dict[str, object]:
    """Mirror `MainForm.RawDataToObjArray` (default 14 fields)."""
    w = int(data_fifo_raw) & ((1 << 64) - 1)
    arr: list[object] = [None] * 14
    arr[0] = f"{w:016X}"
    arr[1] = int((w >> 47) & 1)
    arr[2] = int((w >> 48) & 0xFF)
    arr[3] = int((w >> 43) & 0xF)
    arr[4] = int(((w >> 40) & 7) * 8 + ((w >> 37) & 7))
    arr[5] = int((w >> 36) & 1)
    arr[6] = int((w >> 35) & 1)
    arr[7] = int((w >> 26) & 0x1FF)
    if bool((w >> 35) & 1):
        arr[8] = int((w >> 16) & 1)
        arr[9] = int((w >> 15) & 1)
        arr[10] = int((w >> 13) & 3)
        arr[11] = "-"
        arr[12] = "-"
        arr[13] = int(w & 0x1FFF)
    else:
        arr[8] = "-"
        arr[9] = "-"
        arr[10] = "-"
        arr[11] = int((w >> 17) & 0x1FF)
        arr[12] = int((w >> 8) & 0x1FF)
        arr[13] = int(w & 0xFF)
    return {
        "raw_hex": arr[0],
        "mat": arr[3],
        "pix": arr[4],
        "cal_mode": arr[6],
        "cnt_tot": arr[13],
        "dco_field": arr[8],
        "de_field": arr[9],
        "cal_time_field": arr[10],
        "counts_1": arr[11],
        "counts_0": arr[12],
    }


def _dco_period_ps(*, cal_mode: int, de: Optional[int], cal_time: Optional[int], cnt_tot: int) -> float:
    if cal_mode != 1:
        raise ValueError("DCO period expects CAL_Mode == 1")
    ct = int(cnt_tot)
    if ct <= 0:
        raise ValueError("cnt_tot must be > 0 for DCO period")
    if de == 0:
        return (2.0 ** int(cal_time)) * 400.0 * 1000.0 / float(ct)
    if de == 1:
        return (2.0 ** int(cal_time)) * 400.0 * 1000.0 / (2.0 * float(ct))
    raise ValueError("DE must be 0 or 1 for DCO period")


@dataclass
class CalibFIFOEntry:
    order: int
    raw: str
    mat: int
    pix: int
    cal_mode: int
    cnt_tot: int
    dco: Optional[int]
    de: Optional[int]
    cal_time: Optional[int]
    adjctrl_dco0: Optional[int]
    adjctrl_dco1: Optional[int]
    dco0_t_ps: Optional[float] = None
    dco1_t_ps: Optional[float] = None


StructuredKey = tuple[int, int, int, int]  # mat, pix, cal_mode, dco (999 = wildcard not used)


class CalibLoadedData:
    """
    Minimal port of C# `DataIndex` + `AddDataFromRaw` paths needed for DCO calibration.
    """

    def __init__(self) -> None:
        self.cal_matrix: list[list[list[list[float]]]] = [
            [[[0.0 for _ in range(2)] for _ in range(64)] for _ in range(16)] for _ in range(4)
        ]
        self.resolution_matrix: list[list[list[float]]] = [
            [[0.0 for _ in range(64)] for _ in range(16)] for _ in range(4)
        ]
        self.dco_conf_pairs: list[list[list[list[int]]]] = [
            [[[0 for _ in range(2)] for _ in range(64)] for _ in range(16)] for _ in range(4)
        ]
        self.custom_entries: dict[StructuredKey, list[CalibFIFOEntry]] = {}
        self.n_stored = 0

    def add(self, entry: CalibFIFOEntry) -> None:
        entry.order = self.n_stored
        self.n_stored += 1
        # Mirror C# StructuredKey(MAT, PIX, CAL_Mode, DCO) with DCO = 0/1 for calibration rows.
        dco_k = int(entry.dco) if entry.dco is not None else -1
        key: StructuredKey = (entry.mat, entry.pix, entry.cal_mode, dco_k)
        self.custom_entries.setdefault(key, []).append(entry)

    def get_by_key(self, mat: int, pix: int, cal_mode: int, dco: int) -> list[CalibFIFOEntry]:
        key: StructuredKey = (mat, pix, cal_mode, dco)
        return list(self.custom_entries.get(key, []))

    def add_data_from_raw(
        self,
        data_fifo_raw: int,
        cur_quad: int,
        adjctrl_dco0: int,
        adjctrl_dco1: int,
        *,
        new_cal: bool = False,
    ) -> None:
        fields = raw_fifo_to_fields(data_fifo_raw)
        mat = int(fields["mat"])
        pix = int(fields["pix"])
        cal_mode = int(fields["cal_mode"])
        cnt_tot = int(fields["cnt_tot"])

        def _parse_opt(field: object) -> Optional[int]:
            if field == "-" or field is None:
                return None
            return int(field)  # type: ignore[arg-type]

        dco = _parse_opt(fields["dco_field"])
        de = _parse_opt(fields["de_field"])
        cal_time = _parse_opt(fields["cal_time_field"])

        v0, v1 = int(adjctrl_dco0), int(adjctrl_dco1)
        if self.resolution_matrix[cur_quad][mat][pix] != 0.0 and not new_cal:
            v0 = int(self.dco_conf_pairs[cur_quad][mat][pix][0])
            v1 = int(self.dco_conf_pairs[cur_quad][mat][pix][1])

        entry = CalibFIFOEntry(
            order=0,
            raw=f"{int(data_fifo_raw) & ((1 << 64) - 1):016X}",
            mat=mat,
            pix=pix,
            cal_mode=cal_mode,
            cnt_tot=cnt_tot,
            dco=dco,
            de=de,
            cal_time=cal_time,
            adjctrl_dco0=v0,
            adjctrl_dco1=v1,
        )

        if cal_mode == 1 and dco is not None and de is not None and cal_time is not None:
            period = _dco_period_ps(cal_mode=1, de=int(de), cal_time=int(cal_time), cnt_tot=cnt_tot)
            if dco == 0:
                entry.dco0_t_ps = period
                self.cal_matrix[cur_quad][mat][pix][0] = period
            elif dco == 1:
                entry.dco1_t_ps = period
                self.cal_matrix[cur_quad][mat][pix][1] = period

        if cal_mode == 0:
            c0 = self.cal_matrix[cur_quad][mat][pix][0]
            c1 = self.cal_matrix[cur_quad][mat][pix][1]
            if c0 == 0.0 or c1 == 0.0:
                pass  # useless_counter in C#
            else:
                entry.dco0_t_ps = c0
                entry.dco1_t_ps = c1
                # SetVariables_MEASURE — TA/TOT not needed for DCO cal loop
        self.add(entry)


def read_until_empty(
    hw,
    loaded: CalibLoadedData,
    cur_quad: int,
    adjctrl_dco0: int,
    adjctrl_dco1: int,
    *,
    new_cal: bool = False,
    threshold: int = 1023,
) -> int:
    """Mirror `ReadUntilEmpty(ushort, ushort, bool, ...)` (C#)."""
    n_read = 0
    cons_err = 0
    keep_going = True
    while keep_going:
        num = int(hw.FifoReadSingle())
        if num == 0:
            n_read += 1
            cons_err += 1
            time.sleep(0.001)
            continue
        if n_read % 4 == 0:
            cons_err = 0
        if cons_err == 3:
            keep_going = False
            time.sleep(0.001)
            continue
        num2 = int((num >> 48) & 0xFF)
        if num2 < 1:
            keep_going = False
            time.sleep(0.001)
            continue
        loaded.add_data_from_raw(num, cur_quad, adjctrl_dco0, adjctrl_dco1, new_cal=new_cal)
        n_read += 1
        if n_read > threshold:
            keep_going = False
        time.sleep(0.001)
    return n_read


@dataclass(frozen=True)
class CalibDCOParams:
    """Parameters mirroring `MultiTestSelForm` in CalDCO mode + output path."""

    quadrant_combo_index: int
    """0=SW, 1=NW, 2=SE, 3=NE, 4=ALL quadrants, 5=BROADCAST (not supported for calibration)."""

    mat_combo_index: int
    """0..15 single MAT, 16 = MAT ALL (C# after removing MAT BROADCAST)."""

    pix_min: int
    pix_max: int
    all_pix: bool
    resolution_target_ps: int
    calibration_time: int
    double_edge: bool
    single_adj: int
    single_ctrl: int
    calibrate_mat_4_7: bool
    output_path: Optional[str] = None
    fifo_threshold: int = 1023


def _mat_write_addr(mat_id: int) -> int:
    if mat_id <= 15:
        return 2 * mat_id
    return 254


def _mat_read_addr(mat_id: int) -> int:
    if mat_id <= 15:
        return 2 * mat_id
    return 0


def _mux_send(hw, quadrant_bit_value: int) -> None:
    rc = hw.i2c_send_byte(224, int(quadrant_bit_value) & 0xFF)
    if rc != 0:
        raise Ignite64TransportError(f"I2C_SendByte(mux 224, mask={quadrant_bit_value}) -> {rc}")


def _mat_cal_conf_no_ui(
    hw,
    mat_id: int,
    *,
    en_con_pad: Optional[bool] = None,
    en_p_vth: Optional[bool] = None,
    en_p_vldo: Optional[bool] = None,
    en_p_vfb: Optional[bool] = None,
    en_timeout: Optional[bool] = None,
    cal_mode: Optional[bool] = None,
    cal_time: Optional[int] = None,
) -> None:
    waddr = _mat_write_addr(mat_id)
    raddr = _mat_read_addr(mat_id)
    old = hw.i2c_read_byte(raddr, _REG_CAL_CONF)
    value = bool(old & 0x80) if en_con_pad is None else en_con_pad
    value2 = bool(old & 0x40) if en_p_vth is None else en_p_vth
    value3 = bool(old & 0x20) if en_p_vldo is None else en_p_vldo
    value4 = bool(old & 0x10) if en_p_vfb is None else en_p_vfb
    value5 = bool(old & 0x08) if en_timeout is None else en_timeout
    value6 = bool(old & 0x04) if cal_mode is None else cal_mode
    num2 = old & 3 if cal_time is None else (int(cal_time) & 3)
    num = (
        int(value) * 128
        + int(value2) * 64
        + int(value3) * 32
        + int(value4) * 16
        + int(value5) * 8
        + int(value6) * 4
        + num2
    )
    hw.i2c_write_byte(waddr, _REG_CAL_CONF, int(num) & 0xFF)


def _mat_tdc_dco0_conf_no_ui(
    hw,
    mat_id: int,
    *,
    de_on: Optional[bool] = None,
    tdcon: Optional[bool] = None,
    adj: Optional[int] = None,
    ctrl: Optional[int] = None,
) -> None:
    waddr = _mat_write_addr(mat_id)
    raddr = _mat_read_addr(mat_id)
    old = hw.i2c_read_byte(raddr, _REG_TDC_DCO0)
    value = bool(old & 0x80) if de_on is None else de_on
    value2 = bool(old & 0x40) if tdcon is None else tdcon
    num2 = (old >> 4) & 3 if adj is None else int(adj) & 3
    num3 = old & 0xF if ctrl is None else int(ctrl) & 0xF
    num = int(value) * 128 + int(value2) * 64 + num2 * 16 + num3
    hw.i2c_write_byte(waddr, _REG_TDC_DCO0, int(num) & 0xFF)


def _mat_command_no_ui(
    hw,
    mat_id: int,
    cal_sel_dco: int,
    *,
    daq_res: bool = False,
    g48_63: bool = True,
    g32_47: bool = True,
    g16_31: bool = True,
    g00_15: bool = True,
) -> None:
    waddr = _mat_write_addr(mat_id)
    if cal_sel_dco < 0 or cal_sel_dco > 1:
        raise ValueError("cal_sel_dco must be 0 or 1")
    num = (
        int(daq_res) * 128
        + int(cal_sel_dco) * 16
        + int(g48_63) * 8
        + int(g32_47) * 4
        + int(g16_31) * 2
        + int(g00_15)
    )
    hw.i2c_write_byte(waddr, _REG_MAT_COMMAND, int(num) & 0xFF)


def _mat_pixel_conf_no_ui(
    hw,
    mat_id: int,
    pix_id: int,
    *,
    fe_on: Optional[bool] = None,
    pix_on: Optional[bool] = None,
    adj: Optional[int] = None,
    ctrl: Optional[int] = None,
) -> None:
    waddr = _mat_write_addr(mat_id)
    raddr = _mat_read_addr(mat_id)
    reg = int(pix_id) & 0xFF
    old = hw.i2c_read_byte(raddr, reg)
    value = bool(old & 0x80) if fe_on is None else fe_on
    value2 = bool(old & 0x40) if pix_on is None else pix_on
    num2 = (old >> 4) & 3 if adj is None else int(adj) & 3
    num3 = old & 0xF if ctrl is None else int(ctrl) & 0xF
    num = int(value) * 128 + int(value2) * 64 + num2 * 16 + num3
    hw.i2c_write_byte(waddr, reg, int(num) & 0xFF)


def _read_dco_conf(hw, mat_id: int, dco: int, pix: Optional[int] = None) -> int:
    """`Ignite32_DCO_conf_read` — low 6 bits of reg64 (DCO0) or pixel reg (DCO1)."""
    raddr = _mat_read_addr(mat_id)
    if dco == 0:
        b = hw.i2c_read_byte(raddr, _REG_TDC_DCO0)
    else:
        if pix is None:
            raise ValueError("DCO1 read requires pix")
        b = hw.i2c_read_byte(raddr, int(pix) & 0xFF)
    return int(b) & 0x3F


def _latest(entries: list[CalibFIFOEntry]) -> CalibFIFOEntry:
    return max(entries, key=lambda e: e.order)


def run_dco_cal_47(
    hw,
    loaded: CalibLoadedData,
    cur_quad: int,
    params: CalibDCOParams,
    *,
    progress: Optional[Callable[[str], None]] = None,
) -> None:
    """Port of `DCOcal47` (MAT 4–7, broadcast)."""
    p = progress or (lambda _m: None)
    pix_min = int(params.pix_min)
    pix_max = int(params.pix_max)
    min_lsb = int(params.resolution_target_ps) - 3
    cal_time = int(params.calibration_time)
    de = bool(params.double_edge)
    dco0_adj = int(params.single_adj)
    dco0_ctrl = int(params.single_ctrl)
    mid = MAT_BROADCAST_ID

    _mat_cal_conf_no_ui(
        hw,
        mid,
        en_con_pad=False,
        en_p_vth=False,
        en_p_vldo=False,
        en_p_vfb=False,
        en_timeout=False,
        cal_mode=True,
        cal_time=cal_time,
    )
    _mat_tdc_dco0_conf_no_ui(hw, mid, de_on=de, tdcon=True, adj=dco0_adj, ctrl=dco0_ctrl)

    pix_calibrated = 0
    cur_pix = pix_min
    cur_pix_loop = 0
    cur_adjctr = 16 * dco0_adj + dco0_ctrl
    last_LSB = 5000.0
    went_back = False

    while pix_calibrated <= pix_max - pix_min and cur_pix_loop < 64:
        _mat_pixel_conf_no_ui(
            hw, mid, cur_pix, fe_on=False, pix_on=True, adj=cur_adjctr // 16, ctrl=cur_adjctr % 16
        )
        _mat_command_no_ui(hw, mid, 0, daq_res=False, g48_63=True, g32_47=True, g16_31=True, g00_15=True)
        read_until_empty(
            hw,
            loaded,
            cur_quad,
            16 * dco0_adj + dco0_ctrl,
            cur_adjctr,
            new_cal=False,
            threshold=params.fifo_threshold,
        )
        _mat_command_no_ui(hw, mid, 1, daq_res=False, g48_63=True, g32_47=True, g16_31=True, g00_15=True)
        read_until_empty(
            hw,
            loaded,
            cur_quad,
            16 * dco0_adj + dco0_ctrl,
            cur_adjctr,
            new_cal=False,
            threshold=params.fifo_threshold,
        )

        list0: list[CalibFIFOEntry] = []
        list1: list[CalibFIFOEntry] = []
        for mi in range(4, 8):
            e0 = loaded.get_by_key(mi, cur_pix, 1, 0)
            e1 = loaded.get_by_key(mi, cur_pix, 1, 1)
            if not e0 or not e1:
                p(f"DCOcal47: missing FIFO data MAT {mi} PIX {cur_pix}")
                list0.clear()
                break
            list0.append(_latest(e0))
            list1.append(_latest(e1))
        if len(list0) != 4:
            cur_pix_loop += 1
            continue

        last_LSB = 5000.0
        for num in range(4):
            d0 = list0[num].dco0_t_ps
            d1 = list1[num].dco1_t_ps
            if d0 is None or d1 is None:
                continue
            val = float(d0) - float(d1)
            last_LSB = min(last_LSB, val)

        if last_LSB < float(min_lsb):
            cur_adjctr += 1
            last_LSB = 5000.0
        elif last_LSB > float(min_lsb + 4) and not went_back:
            cur_adjctr -= 1
            went_back = True
        else:
            for num2 in range(4):
                d0 = list0[num2].dco0_t_ps
                d1 = list1[num2].dco1_t_ps
                if d0 is None or d1 is None:
                    continue
                num3 = float(d0) - float(d1)
                loaded.resolution_matrix[cur_quad][num2 + 4][cur_pix] = num3
                loaded.dco_conf_pairs[cur_quad][num2 + 4][cur_pix][0] = int(list0[num2].adjctrl_dco0 or 0)
                loaded.dco_conf_pairs[cur_quad][num2 + 4][cur_pix][1] = int(list1[num2].adjctrl_dco1 or 0)
                loaded.cal_matrix[cur_quad][num2 + 4][cur_pix][0] = float(d0)
                loaded.cal_matrix[cur_quad][num2 + 4][cur_pix][1] = float(d1)
                _mat_pixel_conf_no_ui(
                    hw,
                    mid,
                    cur_pix,
                    fe_on=False,
                    pix_on=False,
                    adj=cur_adjctr // 16,
                    ctrl=cur_adjctr % 16,
                )
            last_LSB = 5000.0
            cur_adjctr = 16 * dco0_adj + dco0_ctrl
            pix_calibrated += 1
            cur_pix += 1
            cur_pix_loop = -1
        cur_pix_loop += 1


def run_calib_dco_body(hw, params: CalibDCOParams, *, progress: Optional[Callable[[str], None]] = None) -> CalibLoadedData:
    """
    Execute DCO calibration (same control flow as C# `DCOcal`).
    Returns the in-memory calibration matrices; optionally writes a summary file if `output_path` is set.
    """
    p = progress or (lambda _m: None)
    if params.quadrant_combo_index == 5:
        raise ValueError("CalibDCO: quadrante BROADCAST non supportato (nel C# il loop è vuoto).")

    start_mux: Optional[int] = None
    try:
        start_mux = int(hw.read_mux_ctrl()) & 0xFF
    except Exception:
        pass

    try:
        hw.TopReadout("i2c")
    except Exception:
        pass

    loaded = CalibLoadedData()
    q_form = int(params.quadrant_combo_index)
    mat_num = int(params.mat_combo_index)
    pix_min = int(params.pix_min)
    pix_max = int(params.pix_max)
    all_pix = bool(params.all_pix)
    resolution = int(params.resolution_target_ps)
    cal_time = int(params.calibration_time)
    enable_de = bool(params.double_edge)
    single_adj = int(params.single_adj)
    single_ctrl = int(params.single_ctrl)
    cal47 = bool(params.calibrate_mat_4_7)

    dco0_adjctrl = 16 * single_adj + single_ctrl
    ON = True

    i_max_quad = 4 if q_form == 4 else 1

    try:
        for qi in range(i_max_quad):
            if q_form > 4:
                continue
            if q_form == 4:
                value = int(2**qi)
                cur_quad = qi
                _mux_send(hw, value)
                p(f"Quadrante {QUAD_INDEX_TO_STR[cur_quad]} (ALL)")
                hw.select_quadrant(QUAD_INDEX_TO_STR[cur_quad])
            else:
                value = int(2**q_form)
                cur_quad = q_form
                _mux_send(hw, value)
                p(f"Quadrante {QUAD_INDEX_TO_STR[cur_quad]}")
                hw.select_quadrant(QUAD_INDEX_TO_STR[cur_quad])

            if cal47:
                run_dco_cal_47(hw, loaded, cur_quad, params, progress=progress)

            if mat_num == 16:
                i_mat = 0
                i_max_mat = 15
            else:
                i_mat = mat_num
                i_max_mat = mat_num

            i_m = i_mat
            while i_m <= i_max_mat:
                if 3 < i_m < 8:
                    i_m += 1
                    continue
                p(f"Calibrazione MAT {i_m}")
                _mat_cal_conf_no_ui(hw, i_m, cal_mode=True, cal_time=cal_time)
                _mat_tdc_dco0_conf_no_ui(hw, i_m, de_on=enable_de, tdcon=ON, adj=single_adj, ctrl=single_ctrl)

                if all_pix and mat_num == 17:
                    pix_min = 0
                    pix_max = 63

                for i_pix in range(pix_min, pix_max + 1):
                    # Ignite32_Mat_PIX_conf_noUI(matID, pixID, FE_ON=null, PIXON=ON)
                    _mat_pixel_conf_no_ui(hw, i_m, i_pix, fe_on=None, pix_on=ON)
                    _mat_command_no_ui(hw, i_m, 0, daq_res=False, g48_63=True, g32_47=True, g16_31=True, g00_15=True)
                    read_until_empty(
                        hw,
                        loaded,
                        cur_quad,
                        dco0_adjctrl,
                        _read_dco_conf(hw, i_m, 1, i_pix),
                        new_cal=False,
                        threshold=params.fifo_threshold,
                    )

                    dco0_list = loaded.get_by_key(i_m, i_pix, 1, 0)
                    if not dco0_list:
                        p(f"AVVISO: nessun dato CAL DCO0 MAT {i_m} PIX {i_pix}")
                        continue
                    dco0 = _latest(dco0_list)

                    looped_once = False
                    adj_ctrl = dco0_adjctrl + min(5, 64 - dco0_adjctrl)
                    is_done = False
                    margin = 50000.0
                    start_scan = dco0_adjctrl + min(5, 64 - dco0_adjctrl)

                    while adj_ctrl < 64:
                        if is_done:
                            adj_ctrl += 1
                            break

                        _mat_pixel_conf_no_ui(
                            hw, i_m, i_pix, fe_on=False, pix_on=True, adj=adj_ctrl // 16, ctrl=adj_ctrl % 16
                        )
                        _mat_command_no_ui(hw, i_m, 1, daq_res=False, g48_63=True, g32_47=True, g16_31=True, g00_15=True)
                        read_until_empty(
                            hw,
                            loaded,
                            cur_quad,
                            _read_dco_conf(hw, i_m, 0, i_pix),
                            adj_ctrl,
                            new_cal=True,
                            threshold=params.fifo_threshold,
                        )

                        by_dco1 = loaded.get_by_key(i_m, i_pix, 1, 1)
                        if not by_dco1:
                            adj_ctrl += 1
                            continue
                        data_entry = _latest(by_dco1)
                        d0t = dco0.dco0_t_ps
                        d1t = data_entry.dco1_t_ps
                        if d0t is None or d1t is None:
                            adj_ctrl += 1
                            continue
                        num = float(resolution) - (float(d0t) - float(d1t))
                        if num + 3.0 > 0.0 and num + 3.0 < margin:
                            margin = num
                            loaded.resolution_matrix[cur_quad][dco0.mat][dco0.pix] = float(d0t) - float(d1t)
                            loaded.dco_conf_pairs[cur_quad][dco0.mat][dco0.pix][0] = int(dco0.adjctrl_dco0 or 0)
                            loaded.dco_conf_pairs[cur_quad][data_entry.mat][data_entry.pix][1] = int(
                                data_entry.adjctrl_dco1 or 0
                            )
                            loaded.cal_matrix[cur_quad][dco0.mat][dco0.pix][0] = float(d0t)
                            loaded.cal_matrix[cur_quad][data_entry.mat][data_entry.pix][1] = float(d1t)
                        if 0.0 < num < 4.0:
                            loaded.resolution_matrix[cur_quad][dco0.mat][dco0.pix] = float(d0t) - float(d1t)
                            loaded.dco_conf_pairs[cur_quad][dco0.mat][dco0.pix][0] = int(dco0.adjctrl_dco0 or 0)
                            loaded.dco_conf_pairs[cur_quad][data_entry.mat][data_entry.pix][1] = int(
                                data_entry.adjctrl_dco1 or 0
                            )
                            _mat_pixel_conf_no_ui(
                                hw,
                                i_m,
                                i_pix,
                                fe_on=None,
                                pix_on=False,
                                adj=adj_ctrl // 16,
                                ctrl=adj_ctrl % 16,
                            )
                            loaded.cal_matrix[cur_quad][dco0.mat][dco0.pix][0] = float(d0t)
                            loaded.cal_matrix[cur_quad][data_entry.mat][data_entry.pix][1] = float(d1t)
                            is_done = True
                        elif adj_ctrl == 63 and not looped_once:
                            adj_ctrl = 0
                            looped_once = True
                        elif adj_ctrl == start_scan and looped_once:
                            is_done = True
                            looped_once = False
                            adj_reserve = loaded.dco_conf_pairs[cur_quad][data_entry.mat][data_entry.pix][1]
                            _mat_pixel_conf_no_ui(
                                hw,
                                i_m,
                                i_pix,
                                fe_on=None,
                                pix_on=True,
                                adj=adj_reserve // 16,
                                ctrl=adj_reserve % 16,
                            )
                            _mat_command_no_ui(hw, i_m, 1, daq_res=False, g48_63=True, g32_47=True, g16_31=True, g00_15=True)
                            read_until_empty(
                                hw,
                                loaded,
                                cur_quad,
                                _read_dco_conf(hw, i_m, 0, i_pix),
                                adj_reserve,
                                new_cal=True,
                                threshold=params.fifo_threshold,
                            )
                            by_dco1b = loaded.get_by_key(i_m, i_pix, 1, 1)
                            if not by_dco1b:
                                break
                            data_entry = _latest(by_dco1b)
                            d0t = dco0.dco0_t_ps
                            d1t = data_entry.dco1_t_ps
                            if d0t is None or d1t is None:
                                break
                            loaded.resolution_matrix[cur_quad][dco0.mat][dco0.pix] = float(d0t) - float(d1t)
                            loaded.cal_matrix[cur_quad][dco0.mat][dco0.pix][0] = float(d0t)
                            loaded.cal_matrix[cur_quad][data_entry.mat][data_entry.pix][1] = float(d1t)
                            _mat_pixel_conf_no_ui(
                                hw,
                                i_m,
                                i_pix,
                                fe_on=None,
                                pix_on=False,
                                adj=adj_reserve // 16,
                                ctrl=adj_reserve % 16,
                            )
                        adj_ctrl += 1

                _mat_cal_conf_no_ui(hw, i_m, cal_mode=False, cal_time=cal_time)
                p(f"Fine MAT {i_m}")
                i_m += 1

        if params.output_path:
            _write_cal_summary(params.output_path, loaded)
            p(f"Scritto {params.output_path}")
    finally:
        if start_mux is not None:
            try:
                rc = int(hw.i2c_send_byte(224, start_mux))
                if rc != 0:
                    p(f"Mux restore warning: I2C_SendByte(224, 0x{start_mux:02X}) -> {rc}")
            except Exception as ex:
                p(f"Mux restore failed: {ex}")

    return loaded


def _write_cal_summary(path: str, loaded: CalibLoadedData) -> None:
    lines = ["quad\tmat\tpix\tdco0_adj\tdco1_adj\tres_ps\tcal0\tcal1"]
    for q in range(4):
        for m in range(16):
            for pix in range(64):
                r = loaded.resolution_matrix[q][m][pix]
                if r == 0.0:
                    continue
                c0 = loaded.dco_conf_pairs[q][m][pix][0]
                c1 = loaded.dco_conf_pairs[q][m][pix][1]
                k0 = loaded.cal_matrix[q][m][pix][0]
                k1 = loaded.cal_matrix[q][m][pix][1]
                lines.append(f"{q}\t{m}\t{pix}\t{c0}\t{c1}\t{r:g}\t{k0:g}\t{k1:g}")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
