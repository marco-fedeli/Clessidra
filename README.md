# Clessidra

🇮🇹 Italiano · [🇬🇧 English](README.en.md)

Un timer con conto alla rovescia per **Windows 10/11** che ti aiuta a restare concentrato.
Imposti quanto vuoi lavorare, la clessidra sta in silenzio vicino all'orologio e, quando il tempo
è finito, suona un piccolo **"ding"** e ti avvisa senza interrompere quello che stai facendo.

![Il pannello di Clessidra](docs/pannello.png)

## Come è nato

Non sono un programmatore: sono un consulente che aiuta le PMI a usare l'intelligenza artificiale.
Mi serviva un timer per non distrarmi mentre lavoro, senza finestre che saltano fuori e senza
suoni invadenti, così ho descritto cosa volevo e l'ho costruito con **Claude Code**.
Il codice l'ha scritto l'AI; io ho definito cosa mi serviva, provato e corretto il risultato.

Lo stesso metodo funziona per i piccoli fastidi operativi di un'azienda.
→ [marcofedeli.com](https://marcofedeli.com)

## Cosa fa

- Vive nell'**area di notifica**, vicino all'orologio. Si apre con **Ctrl+Alt+T** o con un clic sulla clessidra.
- **Durate rapide**: 5, 10, 15, 25, 45 e 60 minuti, con un clic. Oppure scrivi la tua:
  `25` (minuti), `1:30` (minuti e secondi), `90s`, `2,5`, `1h`.
- Un campo facoltativo **Attività** ("Scrittura relazione") ti ricorda a cosa stai lavorando.
- Mentre il timer scorre, l'**icona mostra i minuti che mancano** (ambra; grigia in pausa).
- **Countdown sullo schermo**: un piccolo riquadro sempre in primo piano con il tempo che manca e una
  barretta che si riempie. Non prende mai il focus, quindi non interrompe quello che scrivi.
  - Si **sposta** trascinandolo, e ricorda dove l'hai messo.
  - Il pulsante **↺** lo riporta alla durata di partenza (anche dopo "Finito").
  - Doppio clic apre il pannello, clic destro apre il menu.
  - Si può spegnere dal menu: **Countdown sullo schermo**.

  ![Countdown in corso](docs/countdown.png) ![Countdown in pausa](docs/countdown-pausa.png) ![Countdown finito](docs/countdown-finito.png)

- **Pausa**, **Riprendi**, **Annulla** e **Ricomincia**.
- Alla fine: un **suono breve e gradevole** (Campanella, Doppio rintocco o Gong leggero, oppure nessuno)
  e un **avviso silenzioso** di Windows con l'attività e la durata. Il suono è generato dal programma:
  non ci sono file audio, e la notifica di Windows non aggiunge il suo.
- Anche il pannello si **sposta** trascinandolo da una parte vuota. Se una finestra finisce fuori posto,
  dal menu c'è **Rimetti le finestre al loro posto**.
- Segue il **tema chiaro o scuro** di Windows.
- **Avvia con Windows**, se vuoi, dal menu (si attiva e disattiva con un clic).
- Nessuna installazione e nessuna dipendenza: un solo file `.exe` di circa 55 KB.

## Download

Scarica `Clessidra.exe` dalla pagina [**Releases**](../../releases) e avvialo con un doppio clic.
Comparirà la clessidra vicino all'orologio (se non la vedi, guarda tra le icone nascoste, sotto la freccia **^**).

> **Nota:** il programma non ha una firma digitale, quindi Windows potrebbe mostrare
> "Windows ha protetto il PC". Clicca **Ulteriori informazioni → Esegui comunque**.
> Se preferisci, puoi compilarlo tu dal codice sorgente (vedi sotto).

## Come si usa

| Dove | Cosa fai |
|---|---|
| `Ctrl+Alt+T` o clic sulla clessidra | Apre e chiude il pannello |
| Nel pannello: scrivi la durata e premi `Invio` | Avvia il timer (il pannello si chiude da solo) |
| Nel pannello: `Esc` o clic fuori | Chiude il pannello |
| Clic destro sulla clessidra o sul countdown | Menu: durate rapide, pausa, suono, avvio con Windows, esci |
| Countdown: trascina | Lo sposti dove vuoi |
| Countdown: **↺** | Ricomincia da capo |
| Countdown: clic su "Finito" | Lo fa sparire |

## Compilare dal codice sorgente

Non serve installare niente: si usa il compilatore C# già incluso in Windows (.NET Framework 4.x).

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Crea `Clessidra.exe` (e la sua icona) nella stessa cartella. Se Clessidra è aperta, lo script la chiude prima.

## Dove salva i dati

Ultima durata, suono scelto, countdown acceso o spento e posizione delle finestre sono in
`HKEY_CURRENT_USER\Software\Clessidra`. Con **Avvia con Windows** attivo c'è anche la voce `Clessidra` in
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.

**Disinstallare:** togli la spunta a **Avvia con Windows**, scegli **Esci**, elimina l'`.exe` e, se vuoi,
la chiave qui sopra.

## Licenza

[MIT](LICENSE)
