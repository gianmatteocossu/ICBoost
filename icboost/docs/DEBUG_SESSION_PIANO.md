# Piano sessione debug (IGNITE64 / ICBoost)

Checklist veloce per verifica funzionalità **con hardware**. Incrociare con `docs/MAT_PIXEL_MAP_CSHARP_XREF.md` e con i test **offline** (`pytest`).

## Prima di iniziare

- [ ] `cd icboost` → venv attivo → `pip install -e ".[dev]"` → `pytest -q`
- [ ] DLL in `icboost/`, SandroBOX / TCP come da `INSTALLAZIONE_WINDOWS.md`
- [ ] Variabile `IGNITE64_DEBUG=1` se servono log su stderr (`init_sandrobox_usb`)

## 1. Trasporto e enumerazione

| # | Azione | Atteso |
|---|--------|--------|
| 1.1 | `python examples/list_devices.py` | Elenco serial / nessun crash DLL |
| 1.2 | `python examples/hw_smoke_check.py` (se presente) | I2C minimo OK |
| 1.3 | GUI `examples/gui_monitor.py` con `OFFLINE=0` | Connessione, nessun access violation ripetuto |

## 2. Mux quadrante e indirizzi MAT

| # | Azione | Atteso |
|---|--------|--------|
| 2.1 | Per ogni quad `SW,NW,SE,NE`: `select_quadrant` + lettura mux (se esposta in GUI) | Coerente con C# |
| 2.2 | Verifica **MatID → indirizzo I2C** `addr = 2 * MatID` (MatID 1→2, 11→22) | Come `Ignite32_MATID_ToIntDevAddr` in `MainForm.cs` |
| 2.3 | **Mat analog “owner” per blocco**: **1, 3, 9, 11** (non 0,2,8,10) | Vedi xref doc + `BlockMapping` in `gui_tk.py` |

## 3. Pixel e FTDAC (una MAT)

| # | Azione | Atteso |
|---|--------|--------|
| 3.1 | Accendi/spegni singolo canale (GUI o macro `accendi_canale` / `pixel_on`) | PIXON (bit6) coerente con `readAnalogChannelON` |
| 3.1b | FEON separato: checkbox / `setAnalogFEON` vs PIXON | Bit7 coerente con `readAnalogFEON`; `AnalogChannelOFF` non deve azzerare FEON se non richiesto |
| 3.2 | FTDAC step su pochi canali | Codici 0..15, nibble basso/alternati reg 76..107 |
| 3.3 | `readMatPixelsAndFTDAC` vs griglia C# stessa MAT | Stessi pattern |

## 4. FIFO e readout TOP

| # | Azione | Atteso |
|---|--------|--------|
| 4.1 | `TopReadout("i2c")` poi `FifoDrain` / `FifoReadSingle` | rc==3 = vuoto come in C# |
| 4.2 | Macro `prepare_fifo_readout` | `ok: true`, readout `i2c` |

## 5. Calibrazioni “lunghe”

| # | Azione | Atteso |
|---|--------|--------|
| 5.1 | `CalibrateFTDAC` **un canale** prima di MAT intera | Completa o errore esplicito |
| 5.2 | `CalibDCO` | Trattare come **sperimentale**; log FIFO e tempi |

## 6. Macro GUI elenco

| # | Macro | Nota |
|---|--------|------|
| 6.1 | FTDAC delta / cal mat / cal ch | Worker thread dove indicato nel registry |
| 6.2 | TDC on/off, isolamento canale | Verifica una MAT “owner” 1/3/9/11 se coinvolge analog col |

## 7. Snapshot e config

| # | Azione | Atteso |
|---|--------|--------|
| 7.1 | Export snapshot testuale (macro o API) | Formato leggibile da `parse_full_configuration` |

---

**Fine giornata:** annotare MAT/quad usati, versione DLL (`get_dll_version`), e any mismatch vs C# (file + riga).
