## ICBoost (`icboost`)

Wrapper Python (Windows) per usare **le stesse DLL** del tool C# (`TCPtoI2C.dll`, `USBtoI2C32.dll`) e pilotare l'hardware via I2C con un'API ad alto livello.

Questo pacchetto vive di solito dentro un repository più ampio: alla **radice** del clone trovi `README.md`, `AGENTS.md` (contesto per Cursor), `CONTRIBUTING.md` e il progetto C# di riferimento `tb_Ignite64/`.

### Requisiti
- Python 3.10+ su Windows
- Le DLL accessibili nel `PATH` **oppure** in una cartella che passerai a `Ignite64(..., dll_dir=...)`

### Quick start

```python
from icboost.api import Ignite64

hw = Ignite64(dll_dir=r"C:\path\to\dlls")

# selezione trasporto (USB oppure TCP)
hw.enable_usb()
# hw.enable_tcp()
# hw.connect_tcp(serial_number=0, ip="192.168.0.10", port=5000)

hw.select_quadrant("NW")

hw.AnalogChannelOFF("NW", mattonella=1, canale=23)
hw.AnalogChannelON("NW", mattonella=1, canale=23)

hw.AnalogColumnSetDAC("NW", block=0, dac="VTHR_H", valore=76)
hw.AnalogColumnDACon("NW", block=0, dac="VTHR_H", valore=True)

hw.AnalogColumnVinjMux("NW", block=0, vinj="VinjH", valore="VDDA")
hw.AnalogColumnVinjMux("NW", block=0, vinj="VinjL", valore="GNDA")

hw.AnalogChannelFineTune("NW", block=0, mattonella=1, canale=23, valore=7)

hw.StartTP(numberOfRepetition=10)
```

### Documentazione API e macro

- **Sviluppo / Cursor** (contesto persistente, clone): [`docs/SVILUPPO_CURSOR.md`](docs/SVILUPPO_CURSOR.md)
- **Installazione su Windows** (Python, dipendenze, DLL, avvio GUI): [`docs/INSTALLAZIONE_WINDOWS.md`](docs/INSTALLAZIONE_WINDOWS.md)
- **Guida principale** (indice, API per settore, builtin, macro, esempi e flussi): [`docs/GUIDA_HW_E_MACRO.md`](docs/GUIDA_HW_E_MACRO.md)
- **Puntatore rapido**: [`docs/HW_API_REFERENCE.md`](docs/HW_API_REFERENCE.md)
- **Macro** (`source nome.py` dalla GUI): [`examples/macros/README.md`](examples/macros/README.md)
- **Piano debug (checklist HW)**: [`docs/DEBUG_SESSION_PIANO.md`](docs/DEBUG_SESSION_PIANO.md)
- **MAT / pixel cross-check con C#**: [`docs/MAT_PIXEL_MAP_CSHARP_XREF.md`](docs/MAT_PIXEL_MAP_CSHARP_XREF.md)

### Test automatici (senza hardware)

Dalla cartella `icboost`:

```text
pip install -e ".[dev]"
pytest
```

### Note importanti (mappatura dal C#)
- **Selezione quadrante**: tramite MUX I2C (bitmask: SW=1, NW=2, SE=4, NE=8).
- **Addressing MAT**: nel tool C# l'indirizzo I2C del MAT è `MatID*2` (MatID 0..15).
- **Mat “owner” analog per blocco 2×2**: **1, 3, 9, 11** (come `MainForm.cs` e `BlockMapping` in GUI Python) — **non** confondere con gli angoli indice 0, 2, 8, 10.
- **Registri MAT**:
  - `PIX` config: registro = `PixID` (0..63), layout: `FE_ON` bit7, `PIXON` bit6, `adj` bits5..4, `ctrl` bits3..0
  - DAC interni: reg 70..75 (`VTH_H`, `VTH_L`, `VINJ_H`, `VINJ_L`, `VLDO`, `VFB`), layout: enable bit7 + code 0..127
  - FineTune DAC: reg 76..107, 2 canali per byte (nibble basso = canale pari, nibble alto = canale dispari)
  - Vinj mux: reg 69, bit5 selezione VinjH, bit4 selezione VinjL
- **Registri TOP** (TP): reg 9..11 (come in `AFE_PULSE_Change` del tool C#)

