"""
Test offline: indirizzi MAT, mux quadrante, mappa blocchi vs C# (MainForm.cs).

Nessun caricamento DLL / hardware.
"""

from __future__ import annotations

import pytest

from icboost.device import Ignite64LowLevel, _quad_to_mask
from icboost.gui_tk import BlockMapping
from icboost.calib_dco import raw_fifo_to_fields, _dco_period_ps, MAT_BROADCAST_ID


def _csharp_matid_to_int_dev_addr(mat_id: int) -> int:
    """Porta letterale della logica in MainForm.cs Ignite32_MATID_ToIntDevAddr."""
    if mat_id > 15:
        return 254
    return 2 * abs(mat_id)


class TestMatIdToDevAddrMatchesCSharp:
    """Ignite64LowLevel.matid_to_devaddr vs C# per MatID 0..15."""

    @pytest.mark.parametrize("mat_id", range(16))
    def test_range_0_15(self, mat_id: int) -> None:
        assert Ignite64LowLevel.matid_to_devaddr(mat_id) == _csharp_matid_to_int_dev_addr(mat_id)

    def test_broadcast_id_constant(self) -> None:
        assert MAT_BROADCAST_ID == 254
        assert _csharp_matid_to_int_dev_addr(16) == 254
        assert _csharp_matid_to_int_dev_addr(254) == 254

    def test_mat_id_negative_rejected_in_python(self) -> None:
        with pytest.raises(ValueError):
            Ignite64LowLevel.matid_to_devaddr(-1)


class TestQuadMuxMask:
    @pytest.mark.parametrize(
        "quad,mask",
        [
            ("SW", 0x01),
            ("NW", 0x02),
            ("SE", 0x04),
            ("NE", 0x08),
        ],
    )
    def test_masks(self, quad: str, mask: int) -> None:
        assert _quad_to_mask(quad) == mask
        assert _quad_to_mask(quad.lower()) == mask

    def test_invalid_quad(self) -> None:
        with pytest.raises(ValueError):
            _quad_to_mask("XX")


class TestBlockMappingControlMatsNotZeroTwoEightTen:
    """
    Mat analog owner per blocco = (1, 3, 9, 11) come MainForm.cs ~5407-5410.
    NON (0, 2, 8, 10) che sarebbero i min MatID geometrici dei quattro 2x2.
    """

    @pytest.fixture
    def m(self) -> BlockMapping:
        return BlockMapping()

    def test_analog_owner_tuple(self, m: BlockMapping) -> None:
        assert m.analog_owner_by_block == (1, 3, 9, 11)

    @pytest.mark.parametrize(
        "block_id,expected_owner,mats_in_block",
        [
            (0, 1, {0, 1, 4, 5}),
            (1, 3, {2, 3, 6, 7}),
            (2, 9, {8, 9, 12, 13}),
            (3, 11, {10, 11, 14, 15}),
        ],
    )
    def test_owner_per_block(self, m: BlockMapping, block_id: int, expected_owner: int, mats_in_block: set[int]) -> None:
        assert m.analog_owner_mat(block_id) == expected_owner
        assert set(m.mats_in_block(block_id)) == mats_in_block
        wrong_corner = min(mats_in_block)
        assert wrong_corner != expected_owner  # es. block 0: 0 != 1
        assert wrong_corner in (0, 2, 8, 10)  # proprio quegli angoli "sbagliati" se scambiati col owner

    def test_block_id_out_of_range(self, m: BlockMapping) -> None:
        with pytest.raises(ValueError):
            m.mats_in_block(-1)
        with pytest.raises(ValueError):
            m.analog_owner_mat(4)


class TestRawFifoToFieldsShape:
    def test_decode_zero_word(self) -> None:
        d = raw_fifo_to_fields(0)
        assert "raw_hex" in d and "mat" in d and "pix" in d


class TestDcoPeriodGuards:
    def test_cnt_tot_zero_raises(self) -> None:
        with pytest.raises(ValueError, match="cnt_tot"):
            _dco_period_ps(cal_mode=1, de=0, cal_time=3, cnt_tot=0)
