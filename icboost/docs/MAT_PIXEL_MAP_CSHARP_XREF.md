# Cross-check: mappa MAT / pixel / indirizzi (C# ↔ Python)

Riferimento codice C#: `tb_Ignite64/MainForm.cs`.

## `Ignite32_MATID_ToIntDevAddr` (C# ~6362–6369)

```csharp
private int Ignite32_MATID_ToIntDevAddr(int MatID)
{
    if (MatID > 15)
        return 254;  // broadcast (es. DCOcal47)
    return 2 * Math.Abs(MatID);
}
```

**Python equivalente:** `Ignite64LowLevel.matid_to_devaddr` in `icboost/device.py`  
`return 2 * abs(mat_id)` con **`mat_id` obbligatorio in `0..15`** (negativi rifiutati).

| MatID (mattonella logica) | Valore `2*MatID` (usato come byte indirizzo 7-bit I2C nel flusso guarded) |
|---------------------------|--------------------------------------------------------------------------|
| 0 | 0 |
| 1 | 2 |
| 2 | 4 |
| 3 | 6 |
| … | … |
| 11 | 22 |
| 15 | 30 |
| >15 | C#: **254** (broadcast) — Python `calib_dco.MAT_BROADCAST_ID = 254` |

## Mattonelle di **controllo analogico** (quelle “giuste” per DAC column / CON_PAD)

In C#, operazioni che devono propagarsi alle quattro zone 2×2 usano esplicitamente **MatID 1, 3, 9, 11**:

- `MainForm.cs` ~5407–5410: `Ignite32_MAT_Write_Single(1,…)`, `(3,…)`, `(9,…)`, `(11,…)`
- Messaggio operatore ~5280: *«seleziona tra **1, 3, 9** o **11**»* (test DAC VLDO: `num` derivato da indirizzo /2 deve essere uno di questi)

**Non** sono gli “angoli numerici” del blocco in indice lineare **0, 2, 8, 10** (che sarebbero il min MatID di ciascun quadrante 2×2 nella griglia 4×4).  
Su IGNITE64 la mat **owner** del blocco è il secondo elemento della riga superiore o inferiore del 2×2, secondo layout chip — mappato in Python come:

- `icboost/gui_tk.py` → `BlockMapping.analog_owner_by_block = (1, 3, 9, 11)` per `block_id` 0..3.

### Griglia MatID `row*4 + col` (come `BlockMapping.mats_in_block`)

| block_id | MatID nei quattro angoli del blocco | **Owner analog (MatID)** |
|----------|-------------------------------------|---------------------------|
| 0 (TL) | 0, 1, 4, 5 | **1** |
| 1 (TR) | 2, 3, 6, 7 | **3** |
| 2 (BL) | 8, 9, 12, 13 | **9** |
| 3 (BR) | 10, 11, 14, 15 | **11** |

Se in documentazione o commenti compare **0, 2, 8, 10** come “mat di controllo”, è **errato** rispetto a C# e a `BlockMapping`.

## Pixel (canale)

- Registri pixel **0..63** su ciascun MatID: bit PIXON = **bit6** del byte (allineamento C# `Ignite32_Mat_PIX_conf_noUI` / letture GUI).
- FTDAC: registri **76..107**, 2 pixel per byte (nibble basso/alto) — vedi `readMatPixelsAndFTDAC` in `icboost/api.py` e costanti `MatRegs` nello stesso file.

## Dove guardare in C#

| Argomento | Funzione / regione |
|-----------|-------------------|
| Indirizzo MAT | `Ignite32_MATID_ToIntDevAddr` |
| Scrittura MAT singola | `Ignite32_MAT_Write_Single` |
| PIX / TDC / CAL | `Ignite32_Mat_PIX_conf_noUI`, `Ignite32_Mat_TDC_DCO0conf_noUI`, `Ignite32_Mat_CAL_conf_noUI` |
| FIFO | `FifoReadSingle`, `FifoReadNumWords`, burst I2C TOP |

Python: stessi nomi ad alto livello in `icboost/api.py` + `device.py`.
