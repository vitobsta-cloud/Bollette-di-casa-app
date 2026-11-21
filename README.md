# Bollette di casa per Android

App gemella di **Garanzie & Scontrini** (TR/software) per archiviare le bollette di casa: luce, gas, acqua, rifiuti, telefono e condominio.

Ogni bolletta va nella cartella del suo ente nell'archivio sul NAS:

```
\\192.168.1.13\Public\bollette di casa\<Ente - Tipo>\<AAAA_MM_GG scad. importo€>\
    bolletta.pdf
    bolletta.json
```

## Cosa fa

- **Acquisisci:** legge il PDF arrivato per email, anche aperto da Gmail con **Condividi → Bollette**, oppure la foto della bolletta di carta.
  - Dal PDF legge il testo direttamente (pdf.js); le foto e i PDF scansionati passano dall'OCR di Google ML Kit sul telefono.
  - Riconosce ente, tipo, importo, scadenza, periodo, numero di fattura, POD/PDR e consumi.
  - Con la chiave gratuita di Google (Gemini) la lettura viene incrociata con quella online: se importo o scadenza non coincidono, il campo diventa giallo.
- **Archivio:** un riquadro per ogni ente, con le sue bollette e la ricerca.
- **Scadenze:** le bollette da pagare, con il tasto **Pagata**.
  - Una notifica arriva verso le 9: qualche giorno prima, il giorno stesso e, se la bolletta è scaduta, ogni giorno.
  - Le bollette domiciliate hanno solo un avviso informativo.
- **Grafici:** spesa per mese e per ente, confronto con l'anno prima, consumi e costo medio al kWh o allo Smc.
- **Stampa:** una scheda A4 in PDF, con i dati a sinistra e la prima pagina della bolletta a destra.
- **Altre funzioni:** utenti di casa con invito, backup delle impostazioni, catalogo `catalogo_bollette.csv` per Excel, guida all'uso illustrata.

## Aggiornamenti

- **Interfaccia:** tutta l'interfaccia è in `app/src/main/assets/www/index.html`. L'app la scarica da GitHub e mostra "Aggiorna ora": non serve reinstallare.
- **Altri file dell'interfaccia:** per esempio `pdfjs/`. Si possono aggiornare con `aggiornaRisorsa(nome)` del ponte, sempre senza reinstallare.
- **APK:** lo compila GitHub Actions a ogni push su `main`. Si scarica da `releases/latest/download/BolletteDiCasa.apk`.

## Firma

La chiave **non** è nel repository. Il workflow la ricrea dai Secrets del repository `KEYSTORE_BASE64` e `KEYSTORE_PASSWORD` (alias `bollette`). È una chiave diversa da quella di Garanzie & Scontrini.
