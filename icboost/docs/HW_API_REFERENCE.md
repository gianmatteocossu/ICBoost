# Riferimento rapido

- **Installazione su Windows:** [`INSTALLAZIONE_WINDOWS.md`](INSTALLAZIONE_WINDOWS.md)

La **documentazione completa e navigabile** (indice, API per ambito, tabelle builtin/script, esempi pratici e flussi operativi) è nel file:

**[`GUIDA_HW_E_MACRO.md`](GUIDA_HW_E_MACRO.md)**

Contiene anche la premessa sulla responsabilità dell’operatore: le macro e le API sono funzionanti **quando** si conoscono rischi elettrici, configurazione del chip e sequenza corretta delle operazioni.

Per firme ed implementazione riga-per-riga consultare sempre `icboost/api.py` e `icboost/device.py` nel proprio albero sorgenti.

**Nomenclatura pixel (spesso fonte di errore):** nel registro `PixID` 0..63, **bit6 = PIXON** (uscita digitale) è ciò che comandano `AnalogChannelON`/`EnableDigPix` e `readAnalogChannelON`. **Bit7 = FEON** (front-end analogico): `setAnalogFEON` / `readAnalogFEON`; `readAnalogENPOW` è alias del bit7, non di PIXON. Dettaglio in **GUIDA** §1 e §5.3 e appendice §10.
