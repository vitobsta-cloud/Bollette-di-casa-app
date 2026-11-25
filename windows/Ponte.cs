using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BolletteDiCasa
{
    /// <summary>
    /// Il "ponte" tra l'interfaccia e Windows: ha gli stessi nomi del ponte Android (AndroidBridge),
    /// così l'interfaccia del telefono funziona anche qui senza cambiare il modo di lavorare.
    /// Le funzioni con "id" rispondono dopo, con window.__bridgeCb(id, ok, testo).
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class Ponte
    {
        readonly Finestra f;
        public Ponte(Finestra f) { this.f = f; }

        void InSottofondo(string id, Func<string> lavoro)
        {
            Task.Run(() =>
            {
                try { f.Rispondi(id, true, lavoro()); }
                catch (Exception e) { f.Rispondi(id, false, Archivio.Spiega(e)); }
            });
        }

        // ---------- informazioni ----------
        public bool isPC() => true;
        public string versionePC() => Cartelle.Versione;
        public int getVersionCode() => 999;      // nessun APK da proporre sul PC
        public void setArchivio(string modo) { }  // sul PC l'archivio è sempre il NAS

        // ---------- NAS ----------
        public string getNasConfig() { try { return Archivio.ConfigJson(); } catch { return "{}"; } }
        public void setNasConfig(string json) { try { Archivio.SalvaConfig(json); } catch { } }
        public string getNasPassword() => Archivio.Password();
        public void nasTest(string id) => InSottofondo(id, Archivio.Test);
        public void nasPing(string id) => InSottofondo(id, Archivio.Ping);
        public void nasList(string id) => InSottofondo(id, Archivio.Elenco);
        public void nasRead(string id, string rel) => InSottofondo(id, () => Convert.ToBase64String(Archivio.Leggi(rel)));
        public void nasOpen(string id, string rel) => InSottofondo(id, () =>
        {
            var dati = Archivio.Leggi(rel);
            Apri(Path.GetFileName(rel.Replace('/', '\\')), dati);
            return "ok";
        });
        public void nasWriteFile(string id, string relDir, string nome, string b64) => InSottofondo(id, () => Archivio.ScriviFile(relDir, nome, Convert.FromBase64String(b64)));
        public void nasWriteText(string id, string relDir, string nome, string testo) => InSottofondo(id, () => { Archivio.ScriviTesto(relDir, nome, testo); return "ok"; });
        public void nasDeleteFolder(string id, string relDir) => InSottofondo(id, () => Archivio.EliminaCartella(relDir));

        // ---------- file: si aprono con il programma di Windows (PDF in Edge o Acrobat, foto nel visualizzatore) ----------
        public void openFile(string nome, string mime, string b64)
        {
            Task.Run(() =>
            {
                try { Apri(nome, Convert.FromBase64String(b64)); }
                catch (Exception e) { f.Avviso("Non riesco ad aprire il file: " + e.Message); }
            });
        }

        static void Apri(string nome, byte[] dati)
        {
            Directory.CreateDirectory(Cartelle.Apri);
            var vecchi = Directory.GetFiles(Cartelle.Apri);
            if (vecchi.Length > 40) foreach (var v in vecchi) { try { File.Delete(v); } catch { } }
            foreach (var c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '_');
            var p = Path.Combine(Cartelle.Apri, nome);
            try { File.WriteAllBytes(p, dati); }
            catch (IOException)   // già aperto in un altro programma: nome nuovo
            {
                p = Path.Combine(Cartelle.Apri, Path.GetFileNameWithoutExtension(nome) + "_" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(nome));
                File.WriteAllBytes(p, dati);
            }
            Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
        }

        /// <summary>Salva un file dove sceglie l'utente (catalogo CSV, backup…).</summary>
        public void saveDownload(string id, string nome, string mime, string b64)
        {
            f.BeginInvoke((Action)(() =>
            {
                try
                {
                    using (var d = new SaveFileDialog { FileName = nome, InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Title = "Dove salvo il file?" })
                    {
                        var ext = Path.GetExtension(nome);
                        if (ext != "") d.Filter = ext.TrimStart('.').ToUpper() + " (*" + ext + ")|*" + ext + "|Tutti i file|*.*";
                        if (d.ShowDialog(f) != DialogResult.OK) { f.Rispondi(id, false, "Salvataggio annullato."); return; }
                        File.WriteAllBytes(d.FileName, Convert.FromBase64String(b64));
                        f.Rispondi(id, true, d.FileName);
                    }
                }
                catch (Exception e) { f.Rispondi(id, false, e.Message); }
            }));
        }

        // ---------- aggiornamento dell'interfaccia (come sul telefono) ----------
        public string getPendingUpdate() => Aggiornamenti.InAttesa();
        public void applyUpdate() => f.BeginInvoke((Action)(() => { if (Aggiornamenti.Applica()) f.Ricarica(); }));
        public void checkUpdateNow() => Aggiornamenti.Controlla(f, true);
        public string erroreAggiornamento() => "";
        public void aggiornaRisorsa(string id, string nome) => InSottofondo(id, () => Aggiornamenti.Risorsa(nome));
    }
}
