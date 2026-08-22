# Guida di Ripristino e Sblocco (Unbrick) per Flashforge Adventurer 5M
## Versione Firmware Primaria: 5.1.7 (Con Fallback a 3.2.7)

Questa cartella contiene il **Firmware Personalizzato di Ripristino** generato specificamente con la versione **5.1.7** (con tutte le correzioni ai bug Flashforge) e include anche la versione **3.2.7** come fallback.

---

## Struttura della Cartella

- `1_USB_DIRECT_FILES/`: **Versione 5.1.7** scompattata e pronta per il ripristino immediato da USB. **(Metodo 1 - Consigliato)**
- `1_USB_DIRECT_FILES_v5.1.7/`: Copia dedicata dei file versione 5.1.7.
- `1_USB_DIRECT_FILES_v3.2.7/`: Copia dedicata dei file versione 3.2.7 (se desideri la versione 3.2.7).
- `2_TGZ_UPDATE_PACKAGES/`: Pacchetti compressi `.tgz` (5.1.7 e 3.2.7).
- `GUIDA_UNBRICK_RIPRISTINO.md`: Questa guida dettagliata.
- `README.txt`: Istruzioni rapide.

---


---

## ☕ Supporta il Progetto (Offrimi un Caffè)

Se questa guida e il firmware personalizzato ti hanno permesso di salvare la stampante evitando una costosa sostituzione della scheda madre, puoi supportare il lavoro con una donazione simbolica:

👉 **[Supporta su Buy Me A Coffee](https://www.buymeacoffee.com/TUO_USERNAME)** ☕

---

## METODO 1: Ripristino Diretto da USB (Consigliato per Sblocco Immediato con 5.1.7)

### Requisiti
- Una chiavetta USB (da 4 GB a 32 GB).
- Formattare la chiavetta in **FAT32**.

### Procedura
1. **Formatta la chiavetta USB** in FAT32 (accertati che sia vuota).
2. Apri la cartella `1_USB_DIRECT_FILES/` (che contiene la versione **5.1.7** personalizzata).
3. **Copia TUTTI i 15 file** contenuti in `1_USB_DIRECT_FILES/` direttamente nella **radice (root)** della chiavetta USB:
   - `flashforge_init.sh`
   - `screwflag`
   - `software-5.1.7.tar.xz`
   - `library-2.5.0.tar.xz`
   - `kernel-2.1.6.tar.xz`
   - `control-2.2.3.tar.xz`
   - `boot.img`, `start.img`, `end.img`
   - `mencoder`, `play`, `inittab`, `shadow`, `freecach.sh`, `Cube-PLA-Test.gx`
   *(Importante: i file devono essere posizionati nella cartella principale della USB, non dentro cartelle).*
4. **Spegni completamente la stampante** tramite l'interruttore.
5. Inserisci la chiavetta USB nella porta USB della stampante.
6. **Accendi la stampante**:
   - Verrà caricata l'immagine di avvio e partirà l'installazione forzata a basso livello.
   - Verranno installati il kernel, il modulo di controllo MCU/Eboard, il supervisore corretto `ffstartup-arm` e l'ambiente software 5.1.7.
   - Verranno collegate le librerie NetEase Cloud `/opt/nim/lib` eliminando l'errore `exit status 127`.
7. Al termine dell'installazione:
   - Comparirà la schermata finale (`end.img`) o la stampante emetterà un segnale acustico.
   - **Rimuovi la chiavetta USB** e riavvia la stampante (spegni e riaccendi).
8. La stampante si avvierà con il firmware **5.1.7 personalizzato e perfettamente funzionante**.

---

## METODO 2: Installazione da Pacchetto TGZ

Se preferisci usare il file singolo:
1. Formatta la chiavetta USB in **FAT32**.
2. Copia `2_TGZ_UPDATE_PACKAGES/Adventurer5M-5.1.7-2.2.3-20260605.tgz` nella radice della chiavetta.
3. Inserisci la chiavetta nella stampante spenta e accendila.
