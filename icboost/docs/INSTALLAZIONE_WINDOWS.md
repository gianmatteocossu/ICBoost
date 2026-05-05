# Installazione e uso su Windows

Guida per preparare l’ambiente **ICBoost** (modulo Python `icboost`) su Windows: interprete Python, dipendenze, DLL hardware, avvio della GUI e script di esempio. Se la cartella del progetto nel tuo clone si chiama ancora `ignite64py`, rinominala in `icboost` oppure adatta i percorsi dei comandi `cd` di conseguenza.

---

## Indice

1. [Versione di Python](#1-versione-di-python)
2. [Installazione di Python (Windows)](#2-installazione-di-python-windows)
3. [Struttura della cartella del progetto](#3-struttura-della-cartella-del-progetto)
4. [Ambiente virtuale (consigliato)](#4-ambiente-virtuale-consigliato)
5. [Dipendenze Python](#5-dipendenze-python)
6. [DLL di comunicazione (hardware)](#6-dll-di-comunicazione-hardware)
7. [Installare il pacchetto `icboost`](#7-installare-il-pacchetto-icboost)
8. [Variabili d’ambiente utili](#8-variabili-dambiente-utili)
9. [Avvio dell’interfaccia grafica (monitor)](#9-avvio-dellinterfaccia-grafica-monitor)
10. [Altri modi d’uso](#10-altri-modi-duso)
11. [Riferimenti documentazione](#11-riferimenti-documentazione)

---

## 1. Versione di Python

| Requisito | Note |
|-----------|------|
| **Consigliato** | **Python 3.10, 3.11 o 3.12** (a 64 bit), installer ufficiale da [python.org](https://www.python.org/downloads/windows/). |
| Minimo dichiarato | `>=3.9` nel file `pyproject.toml`; restare su 3.10+ evita sorprese con tool e librerie. |

**Tkinter** (finestre GUI) è incluso nell’installer **standard** di Python per Windows. Durante l’installazione, lasciare selezionata l’opzione per **tcl/tk** (di solito è già attiva). Senza di esso la GUI non parte.

---

## 2. Installazione di Python (Windows)

1. Scaricare l’installer **Windows installer (64-bit)** dalla pagina di Python.
2. Avviare l’installer e abilitare **“Add python.exe to PATH”** (aggiunge Python e `pip` al PATH utente).
3. Completare l’installazione e aprire un **nuovo** Prompt dei comandi o PowerShell.

Verifica:

```text
python --version
pip --version
```

Devono puntare allo stesso prefisso di installazione (es. `C:\Users\...\AppData\Local\Programs\Python\Python3xx\`).

---

## 3. Struttura della cartella del progetto

Dopo aver estratto o clonato il sorgente, la parte rilevante è in genere:

```text
icboost\                    ← cartella **progetto** (contiene pyproject.toml)
  pyproject.toml
  requirements.txt
  icboost\                  ← pacchetto Python importabile `icboost`
    assets\                    immagini die (es. ignite64.jpg), ecc.
    api.py, gui_tk.py, ...
  examples\
    gui_monitor.py             avvio GUI tipico
    basic_sequence.py
    list_devices.py
    macros\
    ...
  docs\
    GUIDA_HW_E_MACRO.md
    INSTALLAZIONE_WINDOWS.md   questo file
  ConfigurationFiles\          file di configurazione chip / SI5340 (usati da start_config)
```

Le **DLL** del bridge USB/TCP vanno rese visibili al caricamento (vedi §6); molti script di esempio assumono le DLL nella cartella **`icboost`** stessa (livello di `pyproject.toml`), ossia la directory padre del pacchetto.

---

## 4. Ambiente virtuale (consigliato)

Isola le dipendenze dal sistema:

```text
cd percorso\verso\icboost
python -m venv .venv
.venv\Scripts\activate
python -m pip install --upgrade pip
```

Da ora i comandi `python` e `pip` usano il venv finché la sessione è attiva (`deactivate` per uscire).

---

## 5. Dipendenze Python

Dal **`pyproject.toml`** il vincolo dichiarato è:

- **pillow** — necessario per caricare la foto del die nella schermata Quadrants (`ignite64.jpg` / JPEG).

Installazione:

```text
python -m pip install -r requirements.txt
```

oppure, dalla cartella progetto `icboost`:

```text
python -m pip install -e .
```

Il comando `-e .` installa il pacchetto in modalità editabile **e** risolve le dipendenze elencate nel progetto (incluso Pillow).

**Non** sono richieste altre librerie PyPI per il nucleo del wrapper; **tkinter** è parte della standard library di Python su Windows.

---

## 6. DLL di comunicazione (hardware)

Il software usa le stesse DLL del tool C#:

| File | Ruolo |
|------|--------|
| **TCPtoI2C.dll** | Stack principale (modalità affini al tool legacy). |
| **USBtoI2C32.dll** | USB / caricamento come backend I2C. |

Nel **clone del repository** questi due file sono già nella cartella **`icboost/`** (stesso livello di `pyproject.toml`). Se in un working copy mancano, copiali dall’installazione del tool C# o da un clone aggiornato.

**Come renderle disponibili**

1. **Cartella nota + PATH**  
   Copiare le DLL in una cartella e aggiungerla alla variabile d’ambiente **PATH** utente o di sistema, **oppure**

2. **Cartella del progetto `icboost`**  
   Posizionare `TCPtoI2C.dll` e `USBtoI2C32.dll` nella directory che contiene `pyproject.toml`. Su Python 3.8+ il caricamento può usare anche la directory delle DLL se gli script passano `dll_dir` a `Ignite64(...)` — gli esempi in `examples/` usano spesso la cartella padre del file come `dll_dir`.

3. **`os.add_dll_directory`**  
   Se si passa `dll_dir=` al costruttore `Ignite64`, il codice registra quella cartella per il caricamento DLL (vedi `icboost/dll.py`).

Se le DLL non sono trovate, si ottengono errori all’import o alla prima chiamata native.

---

## 7. Installare il pacchetto `icboost`

Dalla cartella **`icboost`** (quella con `pyproject.toml`):

```text
python -m pip install -e .
```

Così da qualsiasi directory puoi eseguire script che fanno `import icboost`, purché il venv sia attivo (o l’installazione sia nel Python di sistema).

### Se `pip install -e .` fallisce per `setuptools`/`wheel` (assenza rete / DNS)

In alcuni ambienti (offline, proxy, DNS bloccati) `pip` può provare a scaricare le **build dependencies** dichiarate in `pyproject.toml` (tipicamente `setuptools` e `wheel`) e fallire con errori tipo *`getaddrinfo failed`* o *`No matching distribution found for setuptools`*.

Per evitare l’installazione in build-isolation (usa i pacchetti già presenti nel tuo ambiente):

```text
python -m pip install -e . --no-build-isolation
```

Se serve ancora più compatibilità (disabilita PEP517):

```text
python -m pip install -e . --no-use-pep517
```

Senza installazione, è possibile aggiungere manualmente il percorso del progetto a `PYTHONPATH` o usare gli script in `examples/` che inseriscono il repo in `sys.path` (come `gui_monitor.py`).

---

## 8. Variabili d’ambiente utili

| Variabile | Effetto tipico |
|-----------|----------------|
| **OFFLINE** | `1` = nessun accesso reale all’hardware (GUI e routine in modalità simulata/logica ridotta). `gui_monitor.py` imposta di default `OFFLINE=1` se non è già definita. Per il banco reale: `OFFLINE=0`. |
| **START_CONFIG** | Controlla cosa fa la GUI all’avvio con hardware (`OFFLINE=0`): `1` = esegue `hw.start_config(...)` e riscrive TOP/MAT da file; `0` = salta `start_config` (solo letture / stato corrente); `auto` = bring‑up USB/I2C con `init_hw()` **senza** riscrivere TOP/MAT (preserva lo stato del chip). Se **non** imposti `START_CONFIG`, `gui_monitor.py` usa **`auto`** per evitare di riscrivere la config per sbaglio. |
| **BASE_CONFIG_FILE** | Nome file in `ConfigurationFiles/` o percorso completo: usato da `start_config` / `init_hw` per applicare i registri IOext “chiave” (es. 9/10) quando serve coerenza col file C#. |
| **SI5340_CONFIG_FILE** | File SI5340 in `ConfigurationFiles/` (default come in `api.py`). |
| **IGNITE64_I2C_DELAY_S** | Ritardo opzionale (secondi) tra scritture I2C ravvicinate (es. broadcast “ALL”); default piccolo in GUI. |
| **IGNITE64_STARTCFG_DO_GPIO_INIT** / **IGNITE64_STARTCFG_DO_BUS_RECOVERY** | `1` abilita passi extra di init/recovery durante bring‑up (vedi `api.start_config` / `init_hw`). |
| **ICBOOST_GUI_DEBUG** | `0` disattiva i messaggi `[icboost-gui …]` stampati dalla GUI; default tipicamente `1`. |
| **QUAD** | Quadrante iniziale della GUI: `SW`, `NW`, `SE`, `NE`. |
| **IGNITE_DIE_PHOTO** | Percorso file immagine del die (JPEG/PNG). Se assente, lo script può puntare automaticamente a `icboost/assets/ignite64.jpg`. |
| **IGNITE_DIE_CHIP_BOX** | Rettangolo normalizzato sulla foto (left,top,right,bottom). |
| **IGNITE_DIE_TOP_BAND** | Fascia “TOP” cliccabile sulla foto. |
| **IGNITE64_DEBUG** | Debug trasporto I2C (vedi `device.py`). |

In PowerShell, per una sessione:

```powershell
$env:OFFLINE = "0"
$env:START_CONFIG = "auto"   # consigliato: non riscrive TOP/MAT all'avvio
python examples\gui_monitor.py
```

---

## 9. Avvio dell’interfaccia grafica (monitor)

Dalla cartella progetto **`icboost`** (con venv attivo e dipendenze installate):

```text
python examples\gui_monitor.py
```

Cosa succede (semplificato):

- Carica Pillow; se manca, stampa istruzioni per `pip install pillow`.
- Imposta eventuale `IGNITE_DIE_PHOTO` verso gli asset del repo.
- Se **OFFLINE** non è impostata, la imposta a **1** (solo UI senza DLL HW — ideale per esplorare menu e navigazione).
- Chiama `run_gui()` da `icboost.gui_tk`: crea `Ignite64()`, esegue bring‑up/config secondo **START_CONFIG** se non offline, e avvia la finestra **IGNITE64 monitor (tk)**.

**Uso della GUI (panoramica)**

- **Quadrant**: quadrante attivo per comandi e navigazione.
- **Home / Back**: navigazione tra viste (Quadrants, blocchi, analogico, …).
- **Cmd**: espressioni Python con `hw`, `quad`, `gui`; oppure `source nome.py` per eseguire macro dalla cartella `examples/macros/`.
- **Macro**: elenco file `.py`; **Source** esegue lo script e la funzione omonima `nome(hw, quad)`.
- **Calib DCO…**: dialog per calibrazione DCO (richiede hardware).
- **Reconnect USB**: tentativo di recovery dopo errori tipo `WDU_Transfer` (bus recovery + `init_hw()` senza riscrivere TOP/MAT).
- Schermata **Quadrants**: foto die (se Pillow + asset), clic su quadrante o fascia TOP per dettaglio.
- Vista **Quadrant → Blocks**: pannello FIFO, **Check Calibration** (post‑calibrazione), pulsante **FIFO…** nella vista **Block** per monitorare senza tornare al quadrante.
- Pagina **TOP**: oltre a Driver STR / Readout / SLVS / StartTP, permette anche di selezionare **SI_CLK IN** (Crystal vs SMA) e di caricare/applicare un file di configurazione **SI5340** dalla cartella `ConfigurationFiles/`.

---

## 10. Altri modi d’uso

| Script / comando | Scopo |
|------------------|--------|
| `python examples\list_devices.py` | Elenco serial USB / verifica DLL (richiede DLL e percorso come negli esempi). |
| `python examples\recover_bus_and_usb.py` | Recovery/diagnostica dopo errori bridge USB (es. `WDU_Transfer`): re-init con bus recovery + prova `select_quadrant` + letture minime TOP/IOext. |
| `python examples\hw_smoke_check.py` | Checklist rapida I2C dopo lunga inattività. |
| `python examples\basic_sequence.py` | Console interattiva con oggetto `hw` (vedi messaggi nel file). |
| Macro | `examples\macros\*.py` + documentazione in `examples\macros\README.md`. |

Tutti gli script assumono di essere lanciati dal contesto corretto di `PYTHONPATH` / installazione del pacchetto e DLL disponibili come da §6.

---

## 11. Riferimenti documentazione

| Documento | Contenuto |
|-----------|-----------|
| [`GUIDA_HW_E_MACRO.md`](GUIDA_HW_E_MACRO.md) | API `hw.*`, macro builtin, script macro, esempi e flussi operativi. |
| [`HW_API_REFERENCE.md`](HW_API_REFERENCE.md) | Punto d’ingresso rapido con link alla guida precedente. |
| [`SVILUPPO_CURSOR.md`](SVILUPPO_CURSOR.md) | Clone, Cursor, `AGENTS.md`, `.cursor/rules/`. |
| [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md) | Git: primo commit, cosa versionare, DLL. |
| [`../../README.md`](../../README.md) | Panoramica **intero repository** (Python + C#). |
| [`../README.md`](../README.md) | Panoramica pacchetto `icboost` e quick start codice. |

---

### Riepilogo checklist installazione

1. Python 3.10+ a 64 bit con **PATH** e **tcl/tk**.
2. `python -m venv .venv` → attivare `.venv`.
3. `python -m pip install -e .` (o `pip install -r requirements.txt` + aggiunta manuale del pacchetto al path se serve).
4. Copiare **TCPtoI2C.dll** e **USBtoI2C32.dll** nella cartella progetto o nel **PATH**.
5. Per provare solo l’interfaccia: `python examples\gui_monitor.py` (OFFLINE=1 di default).
6. Con hardware: `OFFLINE=0`, verificare **START_CONFIG** (`auto` consigliato per non riscrivere la config) e collegamento USB/TCP secondo il vostro banco.

Le macro e i comandi **modificano davvero** i registri sul chip quando non siete in OFFLINE: usare sempre procedure di laboratorio consapevoli.
