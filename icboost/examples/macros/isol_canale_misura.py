"""
Macro: isolamento di un solo canale sulla MAT (test rumore / FIFO senza calibrazione FTDAC)
Spegne tutti i pixel della mattonella, poi accende solo ``CHANNEL`` e il TDC — come la fase iniziale
di ``CalibrateFTDAC``, senza modificare i codici FineTune.
"""

from icboost.macros_library import builtin_isolate_one_channel

# --- Parametri operativi ---
MAT = 0
CHANNEL = 0  # 0..63


def isol_canale_misura(hw, quad):
    print(f"[isol_canale_misura]  quad={quad!r}  MAT={MAT}  CH={CHANNEL}")
    r = builtin_isolate_one_channel(hw, quad, mat=MAT, channel=CHANNEL)
    print(f"[isol_canale_misura]  OK  {r}")
    return r
