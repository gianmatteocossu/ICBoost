"""
Macro: svuotamento FIFO (parole hit in uscita readout I2C)
Legge e scarta fino a FIFO vuota o fino al limite ``MAX_WORDS``. Eseguire dopo ``prepare_fifo_readout``
se serve una linea pulita prima di trigger o calibrazione.
"""

from icboost.macros_library import builtin_fifo_drain

# --- Parametri operativi ---
MAX_WORDS = 512


def svuota_fifo(hw, quad):
    print(f"[svuota_fifo]  quad={quad!r}  MAX_WORDS={MAX_WORDS}")
    r = builtin_fifo_drain(hw, quad, max_words=MAX_WORDS)
    print(f"[svuota_fifo]  parole lette={r.get('n_words_read')}  primi valori u64={r.get('first_words_u64')}")
    return r
