# Macro (`source nome.py`)

Funzioni operative richiamabili dalla GUI (**Macro** → **Source**): `nome(hw, quad)` con `quad` dal menu **Quadrant**.

Documentazione unificata API + builtin + flussi: **[`../../docs/GUIDA_HW_E_MACRO.md`](../../docs/GUIDA_HW_E_MACRO.md)**.

Le macro che accendono/spegnono “canali” o “pixel” operano sul **bit PIXON (bit6)** (`AnalogChannelON`/`OFF`), non sul **FEON (bit7)** del front-end analogico, salvo dove esplicitamente indicato nella guida.

---

## Elenco file

| File | Parametri (blocco in testa al file) | Descrizione |
|------|--------------------------------------|-------------|
| `spegnitutto.py` | — | Tutti i pixel OFF nel quadrante. |
| `spegni_mat.py` | `MAT` | OFF tutti i canali di una MAT. |
| `accendi_canale.py` | `MAT`, `CHANNEL` | ON un canale. |
| `accendi_mat.py` | `MAT` | ON 64 canali. |
| `accendi_quad_tutti.py` | — | ON 16×64 canali. |
| `calib_ftdac_mat.py` | `MAT` | Calibrazione FIFO tutta la MAT. |
| `ftdac_cal_un_canale.py` | `MAT`, `CHANNEL` | Calibrazione FIFO un canale. |
| `ftdac_cal_canali_scelta.py` | `MAT`, `CHANNELS` | Lista canali, stessa MAT. |
| `ftdac_incrementa_mat.py` | `MAT`, `DELTA` | Offset su tutti gli FTDAC. |
| `soglia_vthr_step.py` | `MAT`, `DAC`, `DELTA` | Step VTHR_H / VTHR_L. |
| `leggi_codici_ftdac.py` | `MAT`, `PER_LINE` | Stampa codici FTDAC 0..63. |
| `prepare_fifo_readout.py` | — | `TopReadout('i2c')` + mux. |
| `abil_tdc_mat.py` | `MAT`, `ENABLE` | TDC on/off. |
| `isol_canale_misura.py` | `MAT`, `CHANNEL` | Isola un canale (misura/FIFO). |
| `leggi_stato_mat.py` | `MAT` | Conteggio PIX on, min/max FTDAC. |
| `svuota_fifo.py` | `MAX_WORDS` | Svuota FIFO. |
| `verifica_power_analogico.py` | — | Legge Analog Power globale. |
| `esporta_snapshot_config.py` | `OUTPUT_PATH` | Salva snapshot TOP+MAT+IOext su file. |

Implementazione condivisa: `icboost.macros_library` (`builtin_*`).
