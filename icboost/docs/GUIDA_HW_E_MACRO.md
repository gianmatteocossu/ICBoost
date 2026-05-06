# Guida hardware, API `Ignite64` e macro

Documento di riferimento per operatori che usano **icboost** da script, barra **Cmd** della GUI o macro **`source`**. Le funzioni sono realmente eseguite sul dispositivo: **ogni chiamata presuppone consapevolezza** di alimentazione, gestione termica, cablaggio e stato del chip.

**Installazione ambiente (Windows):** vedere **[INSTALLAZIONE_WINDOWS.md](INSTALLAZIONE_WINDOWS.md)** (Python, Pillow, DLL, avvio `gui_monitor.py`).

---

## Indice

1. [Premessa operativa](#1-premessa-operativa)
2. [Concetti: quadrante, MAT, canale](#2-concetti-quadrante-mat-canale)
3. [Oggetto `hw` e file sorgente](#3-oggetto-hw-e-file-sorgente)
4. [Connessione trasporto (USB / TCP)](#4-connessione-trasporto-usb--tcp)
5. [API per ambito (metodi pubblici)](#5-api-per-ambito-metodi-pubblici)
   - [5.1 FIFO e readout TOP](#51-fifo-e-readout-top)
   - [5.2 Registri TOP](#52-registri-top)
   - [5.3 Pixel, TDC, potenza](#53-pixel-tdc-potenza)
   - [5.4 DAC colonna e Vinj](#54-dac-colonna-e-vinj)
   - [5.5 FineTune e lettura MAT](#55-finetune-e-lettura-mat)
   - [5.6 Calibrazione FTDAC](#56-calibrazione-ftdac)
   - [5.7 Hitor e ATPulse](#57-hitor-e-atpulse)
   - [5.8 Bias cell e VDDA](#58-bias-cell-e-vdda)
   - [5.9 Snapshot e caricamento file](#59-snapshot-e-caricamento-file)
   - [5.10 Calibrazione DCO](#510-calibrazione-dco)
   - [5.11 Avvio `start_config`](#511-avvio-start_config)
   - [5.12 GUI Tk (monitor)](#512-gui-tk-monitor)
6. [Libreria `macros_library` (builtin)](#6-libreria-macro_library-builtin)
7. [Script in `examples/macros/`](#7-script-in-examplesmacros)
8. [Esempi pratici (frammenti)](#8-esempi-pratici-frammenti)
9. [Flussi consigliati](#9-flussi-consigliati)
10. [Appendice: note sui registri](#10-appendice-note-sui-registri)

---

## 1. Premessa operativa

- Le **macro** in `examples/macros/` sono implementazioni che chiamano API testate; **non sostituiscono** il giudizio dell’operatore su ordine delle operazioni, limiti di corrente e stato del banco.
- **FIFO / calibrazione FTDAC**: richiedono di norma `TopReadout("i2c")`, mux sul quadrante corretto e pixel/TDC configurati come da procedura; la macro `prepare_fifo_readout` imposta readout I2C + `select_quadrant`.
- **MAT 4..7 (nota bus)**: su alcuni banchi l’indirizzamento I2C **diretto** verso MAT 4–7 può “stackare” il bus; in **icboost** le scritture MAT in `start_config` saltano 4–7 e molte azioni GUI su “tutto il quadrante” usano **broadcast** (dev 254) con lettura di verifica su MAT0. Le calibrazioni per-canale che richiedono accesso MAT-specifico possono essere limitate su quelle MAT.
- **`AnalogChannelON` / `AnalogChannelOFF` / `readAnalogChannelON` / `EnableDigPix` / `readEnableDigPix`**: stesso registro pixel per canale; agiscono solo sul **bit PIXON (bit6)** — uscita digitale del pixel. Il nome “AnalogChannel…” è **legacy dal C#** e non indica il front-end analogico.
- **Front-end analogico per pixel (FEON)**: **`readAnalogFEON` / `setAnalogFEON`** sul **bit7** (`FE_ON` / ENPOW nel layout byte). **`readAnalogENPOW`** è alias di `readAnalogFEON` (bit7), non va confuso con PIXON.
- **Alimentazione analogica globale** (scheda / IOext): `readAnalogPower` / `setAnalogPower` — concetto distinto sia da PIXON sia da FEON per-pixel.

---

## 2. Concetti: quadrante, MAT, canale

| Concetto | Valori | Note |
|----------|--------|------|
| **Quadrante** | `SW`, `NW`, `SE`, `NE` | Selezionato con `select_quadrant(quad)` sul mux I2C; nella GUI è il menu **Quadrant**. |
| **Mattonella (MAT)** | `0` … `15` | Indirizzo I2C del dispositivo MAT: `MatID * 2` (come nel tool C#). |
| **Canale / pixel** | `0` … `63` | Indice pixel sulla MAT; registro I2C = indice. |

---

## 3. Oggetto `hw` e file sorgente

- **`icboost.api.Ignite64`**: API ad alto livello (metodi elencati in §5).
- **`icboost.device.Ignite64LowLevel`**: enumerazione USB/TCP, `select_quadrant`, lettura/scrittura I2C raw.
- Per **firme esatte, eccezioni e commenti** inline, aprire sempre i file `icboost/api.py` e `icboost/device.py` nella propria revisione.

---

## 4. Connessione trasporto (USB / TCP)

Metodi tipici su `Ignite64` / low-level (vedi `device.py`):

| Metodo | Uso |
|--------|-----|
| `enable_usb()` | Usa stack USB (DLL `USBtoI2C32`). |
| `enable_tcp()` | Prepara stack TCP (`TCPtoI2C.dll`). |
| `connect_tcp(serial_number=, ip=, port=)` | Connessione TCP al convertitore. |
| `init_sandrobox_usb(...)` | Enumerazione, selezione serial, init bridge (opzioni GPIO/IOext). |
| `get_serial_numbers()` / `select_by_serial_number(sn)` | Gestione dispositivi. |

Dopo la connessione, **`select_quadrant("NW")`** (o altro) prima di accessi MAT/TOP del quadrante desiderato.

---

## 5. API per ambito (metodi pubblici)

Legenda: *quad* = stringa quadrante. Parametri tra parentesi sono keyword salvo dove indicato.

### 5.1 FIFO e readout TOP

| Metodo | Significato |
|--------|-------------|
| `FifoReadSingle() -> int` | Una parola 64 bit; `0` se FIFO vuota (**rc==3**). Attenzione: un hit reale con parola **numericamente 0** non è distinguibile da “vuoto” con questa sola API. |
| `FifoReadSingleRobust(..., quad=?, retries=?, backoff_s=?, do_bus_recovery=?, ensure_i2c_readout=?) -> int` | Come `FifoReadSingle`, con **retry** e tentativo di **bus recovery**; utile in loop di calibrazione quando il bridge restituisce errori transitori (es. `rc=1`). |
| `FifoReadNumWords(n_words: 1..24) -> list[int]` | Burst di parole. |
| `FifoDrain(max_words=4096) -> list[int]` | Svuota usando **rc** del bridge: si ferma solo su **rc==3** (FIFO vuota), così una parola valida **0x0000…00** non viene persa. |

**Prerequisito**: readout TOP su **I2C** (`TopReadout("i2c")`) affinché la FIFO sia leggibile dal bridge.

### 5.2 Registri TOP

| Metodo | Significato |
|--------|-------------|
| `TopDriverSTR(v)` / `readTopDriverSTR()` | Forza driver uscite. |
| `TopReadout("i2c"\|"ser"\|"none")` / `readTopReadout()` | Selezione interfaccia dati. |
| `TopSLVS(mode)` / `readTopSLVS()` | Modalità SLVS. |
| `TopFePolarity(p)` / `readTopFePolarity()` | Polarità FE. |
| `readTopSnapshot(quad) -> dict` | Istantanea decodificata per GUI/log. |
| `StartTP(numberOfRepetition: 0..63)` / `readStartTP() -> dict` | Impulso AFE (reg 11). |
| `unlock_top_default_config(...)` | Sblocco scrittura config TOP. |

### 5.3 Pixel, TDC, potenza

| Metodo | Significato |
|--------|-------------|
| `AnalogChannelON(quad, mattonella=, canale=)` / `AnalogChannelOFF(...)` | PIXON bit6. |
| `readAnalogChannelON(quad, mattonella=, canale=) -> bool` | Lettura PIXON bit6. |
| `EnableDigPix(quad, Mattonella=, Channel=, enable=)` | Stesso bit PIXON, naming “digitale”. |
| `readEnableDigPix(quad, Mattonella=, Channel=) -> bool` | Lettura PIXON bit6 (stesso di `readAnalogChannelON`). |
| `readAnalogFEON(...) -> bool` / `setAnalogFEON(..., on=)` | FEON bit7 (front-end analogico); non modifica PIXON. |
| `readAnalogENPOW(...)` | Stesso bit7 di FEON (alias di `readAnalogFEON`); **non** è PIXON. |
| `EnableTDC(quad, Mattonella=, enable=, double_edge=?)` | TDC on/off per MAT. |
| `readEnableTDC(quad, Mattonella=) -> dict` | `tdc_on`, `double_edge`. |
| `readAnalogPower() -> bool` | Alimentazione analogica globale (IOext). |

### 5.4 DAC colonna e Vinj

| Metodo | Significato |
|--------|-------------|
| `AnalogColumnSetDAC(quad, block=, dac=, valore=)` | `dac`: `VTHR_H`, `VTHR_L`, `VINJ_H`, `VINJ_L`, `VLDO`, `VFB` — codice 0..127. |
| `AnalogColumnDACon(quad, block=, dac=, valore=)` | Abilita/disabilita DAC. |
| `readAnalogColumnDAC(quad, block=, dac=) -> dict` | `enable`, `code`. |
| `AnalogColumnVinjMux(quad, block=, vinj=, valore=)` | `vinj`: `VinjH` / `VinjL`; valori come in `api.py`. |
| `readAnalogColumnVinjMux(quad, block=) -> dict` | |
| `AnalogColumnConnect2PAD(quad, block=, valore=)` | Connessione PAD (calibrazioni). |

### 5.5 FineTune e lettura MAT

| Metodo | Significato |
|--------|-------------|
| `AnalogChannelFineTune(quad, block=, mattonella=, canale=, valore=)` | Codice 0..15 per pixel. |
| `readMatPixelsAndFTDAC(quad, mattonella=) -> dict` | Chiavi `pix_on` (64 bool), `fe_on` (64 bool, FE enable), `ftdac` (64 int). |

### 5.6 Calibrazione FTDAC

| Metodo | Significato |
|--------|-------------|
| `CalibrateFTDAC(quad, Mattonella: int, Channel)` | `Channel` intero 0..63 oppure `"ALL"` per tutti i canali della MAT. Usa FIFO; imposta FT iniziali, isola canale, decrementa codice fino a hit. |

### 5.7 Hitor e ATPulse

| Metodo | Significato |
|--------|-------------|
| `Hitor(quad, mattonella=, valore=)` / `readHitor(quad, mattonella=)` | Config hit-OR MAT. |
| `ATPulse(quad, mattonella=, canale=)` / `readATPulse(quad, mattonella=)` | Impulso test AFE per MAT. |

### 5.8 Bias cell e VDDA

| Metodo | Significato |
|--------|-------------|
| `AnalogColumnBiasCell(quad, block=, csa=, disc=, krum=)` | Scrive celle bias AFE. |
| `readAnalogColumnBiasCell(quad, block=) -> dict` | |
| `measureVDDA(quad, block=) -> float` | Misura VDDA. |
| `TuneVDDA(quad, block=, target_v=, step=, ...)` | Anello chiuso su codice `VLDO`. |
| `AnalogSetIREF(valore_mv: float)` | DAC esterno IREF (vedi implementazione). |
| `readAnalogIREF()` | Non implementato (solleva `Ignite64NotMappedYet`). |

### 5.9 Snapshot e caricamento file

| Metodo | Significato |
|--------|-------------|
| `snapshot_full_configuration(quad, path=)` | Esporta testo TOP + MAT + IOext. |
| `load_full_configuration(path)` | Carica file configurazione completo; sezioni **MAT 4..7** vengono **saltate** (accesso diretto disabilitato come in `start_config`). |
| `load_ioext_and_mux_from_full_configuration(path)` | |
| `apply_ioext_registers_from_full_configuration(path, regs)` | |
| `load_top_and_mats_from_full_configuration(path)` | Come sopra per le MAT: **salta MAT 4..7**. |
| `loadClockSetting(path)` | SI5340. |

### 5.10 Calibrazione DCO

| Metodo | Significato |
|--------|-------------|
| `run_calib_dco(...)` | Wrapper alto livello. |
| `CalibDCO(...)` | Classe / helper (dettagli in `calib_dco.py`). |

La calibrazione DCO in `calib_dco.py` è stata resa più **robusta a errori I2C transitori** del bridge USB (`rc=1` / `WDU_Transfer`):

- Le operazioni per‑pixel e per‑`AdjCtrl` includono retry ad alto livello con backoff esponenziale leggero.
- I default interni corrispondono all’assetto raccomandato in laboratorio:
  - `IGNITE64_CALIB_PIXEL_RETRIES` default **12**
  - `IGNITE64_CALIB_PIXEL_BACKOFF_S` default **0.02**
  - `IGNITE64_CALIB_ADJ_RETRIES` default **8**
  - `IGNITE64_CALIB_ADJ_BACKOFF_S` default **0.02**
- I valori possono ancora essere sovrascritti via variabili d’ambiente se necessario per debug; in assenza di env vengono usati i default sopra.

In caso di errore persistente su un pixel, la calibrazione **ritenta più volte lo stesso pixel** e solo alla fine lo marca come non calibrato, senza interrompere l’intera scansione della MAT.

### 5.11 Avvio `start_config`

`start_config(quadrant, ...)` esegue sequenza: USB, IOext, clock SI5340, scrittura TOP+MAT da file in `ConfigurationFiles/`. Parametri opzionali: percorsi file, registri IOext, tempo stabilizzazione mux. Vedere docstring in `api.py`.

**Bring-up senza riscrivere TOP/MAT**: `init_hw(quadrant, ...)` seleziona USB, opzionalmente applica default IOext + clock SI5340, seleziona il quadrante e sblocca TOP, **senza** caricare la configurazione completa dal file. È pensato per avvio GUI in modalità “preserva stato chip” (`START_CONFIG=auto` in `examples/gui_monitor.py`).

### 5.12 GUI Tk (monitor)

Panoramica funzioni aggiunte rispetto alla sola navigazione menu (dettaglio variabili d’ambiente in **[INSTALLAZIONE_WINDOWS.md](INSTALLAZIONE_WINDOWS.md)**):

- **FIFO**: pannello con decodifica parola, drain, letture robuste durante calibrazione; attenzione a hit “stale” — le routine GUI filtrano spesso per MAT/canale atteso.
- **Analyze FIFO (istogrammi)**: dal pannello FIFO puoi aprire **Analyze…** per visualizzare distribuzioni **TA** / **TOT** e scatter **TA vs TOT** con filtro **MAT/CH**. Nota: la finestra Analyze usa lo **stesso buffer** della lista decodificata mostrata nella form FIFO; quindi i grafici mostrano solo i campioni effettivamente acquisiti nel pannello FIFO. Il pulsante **Clear** del pannello FIFO cancella anche i campioni decodificati; in Analyze il pulsante **Clear data** svuota lo stesso buffer.
- **Calibrazione FTDAC**: da popup pixel (**Calibra canale…**) e da vista blocco (**Calibra canali…** per sweep MAT); riprende **Start MAT / Start CH** e interrompe in caso di errori DLL/trasporto gravi.
- **Check Calibration**: campionamento burst + statistiche canali rumorosi; azione **Turn OFF offending channels** (PIX/FE/TDC come da implementazione corrente) quando serve ripulire dopo calibrazione.
- **Reconnect USB**: recovery best-effort dopo errori tipo `WDU_Transfer` (bus recovery + `init_hw()` senza riscrittura TOP/MAT).
- **Quadrant “ALL” / broadcast**: per MAT 4–7 e operazioni su intero quadrante, preferire i percorsi GUI che usano broadcast invece di I2C diretto per MAT.
- **Save config (compatibile C#)**: nella pagina **Quadrant → Blocks** c’è **Save config…** che salva un file `.txt` nello stesso formato del tool C# (vedi `snapshot_full_configuration` in §5.9). Questo file può essere ricaricato anche dal menu *Load config* del programma C#.
- **Clock IO board (SI5340 / SI_CLK)**: nella pagina **TOP** puoi selezionare la sorgente **SI_CLK IN** (Crystal vs SMA) e applicare un file di configurazione **SI5340** scegliendolo da `ConfigurationFiles/` (o via browse).

---

## 6. Libreria `macros_library` (builtin)

Ogni funzione `builtin_*` ha firma `(hw, quad, **kwargs)` salvo `pixels_all_off_quad` e `quad_all_pixels_on` che non richiedono kwargs oltre `quad`.

| ID registry | Funzione | Ruolo |
|-------------|----------|--------|
| `ftdac_delta_mat` | `builtin_ftdac_delta_mat` | Δ su tutti i FTDAC della MAT. |
| `ftdac_cal_mat` | `builtin_ftdac_cal_mat` | Calibrazione FIFO tutta la MAT. |
| `ftdac_cal_ch` | `builtin_ftdac_cal_ch` | Un canale. |
| `ftdac_cal_channels` | `builtin_ftdac_cal_channels` | Lista canali stessa MAT. |
| `pixels_all_off_quad` | `builtin_pixels_all_off_quad` | Tutti i pixel OFF nel quad. |
| `pixel_on` / `pixel_off` | … | Singolo canale. |
| `mat_all_pixels_on` / `mat_all_pixels_off` | … | 64 canali una MAT. |
| `quad_all_pixels_on` | … | 16×64 ON. |
| `vthr_bump` | … | Step VTHR_H / VTHR_L. |
| `ftdac_dump` | … | Legge array FTDAC. |
| `prepare_fifo_readout` | … | `TopReadout("i2c")` + mux. |
| `tdc_mat` | … | Abilita/disabilita TDC. |
| `isolate_one_channel` | … | Isola un canale (come prep calib). |
| `mat_summary` | … | Conteggi e min/max FTDAC. |
| `fifo_drain` | … | `FifoDrain` con limite. |
| `read_analog_power_state` | … | `readAnalogPower()`. |

Invocazione programmatica: `from icboost.macros_library import run_builtin` oppure import diretto della `builtin_*`.

---

## 7. Script in `examples/macros/`

Ogni file `nome.py` espone `def nome(hw, quad):`. Modificare il blocco **Parametri operativi** in testa al file.

| File | Parametri principali | Contenuto |
|------|----------------------|-----------|
| `spegnitutto.py` | — | Tutti i pixel OFF nel quadrante. |
| `spegni_mat.py` | `MAT` | OFF una MAT. |
| `accendi_canale.py` | `MAT`, `CHANNEL` | ON un pixel. |
| `accendi_mat.py` | `MAT` | ON 64 canali. |
| `accendi_quad_tutti.py` | — | ON intero quadrante. |
| `calib_ftdac_mat.py` | `MAT` | Calib FIFO 64 canali. |
| `ftdac_cal_un_canale.py` | `MAT`, `CHANNEL` | Calib FIFO un canale. |
| `ftdac_cal_canali_scelta.py` | `MAT`, `CHANNELS` | Lista canali. |
| `ftdac_incrementa_mat.py` | `MAT`, `DELTA` | Offset globale FTDAC. |
| `soglia_vthr_step.py` | `MAT`, `DAC`, `DELTA` | Step soglia. |
| `prepare_fifo_readout.py` | — | TOP I2C + mux. |
| `abil_tdc_mat.py` | `MAT`, `ENABLE` | TDC on/off. |
| `isol_canale_misura.py` | `MAT`, `CHANNEL` | Isolamento per misura. |
| `leggi_stato_mat.py` | `MAT` | Riepilogo PIX + FTDAC. |
| `leggi_codici_ftdac.py` | `MAT`, `PER_LINE` | Stampa tabellare codici. |
| `svuota_fifo.py` | `MAX_WORDS` | Drain FIFO. |
| `verifica_power_analogico.py` | — | Stato Analog Power. |
| `esporta_snapshot_config.py` | `OUTPUT_PATH` | File snapshot config. |

---

## 8. Esempi pratici (frammenti)

### 8.1 Selezionare quadrante e spegnere un pixel

```python
hw.select_quadrant("NW")
hw.AnalogChannelOFF("NW", mattonella=3, canale=17)
```

### 8.2 Leggere soglia alta su una MAT

```python
hw.select_quadrant("SW")
r = hw.readAnalogColumnDAC("SW", block=0, dac="VTHR_H")
print(r["enable"], r["code"])
```

### 8.3 Preparare FIFO e leggere una parola

```python
hw.TopReadout("i2c")
hw.select_quadrant("SW")
w = hw.FifoReadSingle()
print(hex(w))
```

### 8.4 Usare una builtin da script Python esterno

```python
from icboost.macros_library import builtin_mat_summary

builtin_mat_summary(hw, "SW", mat=2)
```

### 8.5 Barra Cmd nella GUI

La barra **Cmd** valuta espressioni con `hw`, `quad`, `gui` nel contesto. Esempio:

```python
hw.readTopSnapshot(quad)
```

---

## 9. Flussi consigliati

### Calibrazione FTDAC su alcuni canali

1. Impostare nel menu il **quadrante** corretto.
2. Eseguire `prepare_fifo_readout` (o verificare manualmente readout I2C).
3. Eseguire `ftdac_cal_canali_scelta` con `MAT` e `CHANNELS` aggiornati.

### Prima misura dopo accensione rumore

1. `prepare_fifo_readout`
2. `isol_canale_misura` per il canale sotto test
3. `svuota_fifo` per azzerare coda hit
4. Trigger esterni / `StartTP` come da procedura laboratorio

### Archiviazione stato

1. `esporta_snapshot_config` con `OUTPUT_PATH` su cartella persistente

---

## 10. Appendice: note sui registri

Riassunto allineato al **README** del pacchetto `icboost`:

- **PIX**: registro = `PixID` 0..63; **bit6 = PIXON** (digitale); **bit7 = FEON / FE_ON** (front-end analogico per quel pixel). `AnalogChannelON`/`OFF` toccano solo il bit6.
- **DAC interni**: reg 70..75; bit7 enable + codice 7 bit.
- **FineTune**: reg 76..107, due pixel per byte (nibble basso/alto).
- **TOP TP**: reg 9..11 per impulso AFE / ripetizioni.

Per il layout completo dei byte TOP/MAT dai file di configurazione, usare `snapshot_full_configuration` e confrontare con il tool C# originale.

---

*Ultimo aggiornamento documentazione: allineato al modulo `icboost.api.Ignite64` (distinzione esplicita FEON bit7 vs PIXON bit6, nomi legacy C#).*
