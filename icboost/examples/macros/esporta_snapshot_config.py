"""
Macro: esporta snapshot testuale TOP + MAT + IOext per il quadrante selezionato
Scrive un file di testo adatto a confronti e archiviazione (formato come tool legacy).
Modificare ``OUTPUT_PATH`` prima di Source.
"""

from pathlib import Path

# --- Parametri operativi: percorso file in uscita ---
OUTPUT_PATH = Path.home() / "ignite64_snapshot.txt"


def esporta_snapshot_config(hw, quad):
    q = str(quad).strip().upper()
    p = Path(OUTPUT_PATH).resolve()
    print(f"[esporta_snapshot_config]  quad={q!r}  →  {p}")
    hw.snapshot_full_configuration(q, path=p)
    print(f"[esporta_snapshot_config]  file scritto  ({p.stat().st_size} byte)")
    return {"path": str(p), "quad": q}
