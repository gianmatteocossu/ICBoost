"""
Macro: preparazione readout FIFO (TOP I2C + mux)
Imposta ``TopReadout('i2c')`` e seleziona il quadrante del menu «Quadrant». Da eseguire prima di
``CalibrateFTDAC``, ``FifoDrain`` o letture hit se il readout non è già su I2C.
"""

from icboost.macros_library import builtin_prepare_fifo_readout


def prepare_fifo_readout(hw, quad):
    print(f"[prepare_fifo_readout]  quad={quad!r}")
    r = builtin_prepare_fifo_readout(hw, quad)
    print(f"[prepare_fifo_readout]  {r}")
    return r
