using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace BolletteDiCasa
{
    /// <summary>
    /// L'interfaccia si aggiorna da GitHub come sul telefono: si scarica index.html, se è cambiato
    /// resta "in attesa" e la pagina mostra "Aggiorna ora".
    /// </summary>
    static class Aggiornamenti
    {
        static string Base => "https://raw.githubusercontent.com/vitobsta-cloud/Bollette-di-casa-app/" + Cartelle.Ramo + "/app/src/main/assets/www/";
        static string FileAttuale => Path.Combine(Cartelle.Web, "index.html");
        static string FileInAttesa => Path.Combine(Cartelle.Web, "pending.html");
        static DateTime ultimo = DateTime.MinValue;

        static Aggiornamenti()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        static byte[] Scarica(string url)
        {
            using (var w = new WebClient())
            {
                w.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                return w.DownloadData(url + "?t=" + DateTime.UtcNow.Ticks);
            }
        }

        static byte[] Attuale()
        {
            var p = File.Exists(FileAttuale) ? FileAttuale : Path.Combine(Cartelle.WebIncluso, "index.html");
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }

        /// <summary>Controlla GitHub (al massimo ogni 10 minuti, se non è forzato).</summary>
        public static void Controlla(Finestra f, bool forza)
        {
            if (!forza && DateTime.Now - ultimo < TimeSpan.FromMinutes(10)) return;
            ultimo = DateTime.Now;
            Task.Run(() =>
            {
                try
                {
                    var dati = Scarica(Base + "index.html");
                    var html = Encoding.UTF8.GetString(dati);
                    if (dati.Length < 20000 || !html.Contains("AndroidBridge") || !html.Contains("</html>")) return;
                    var att = Attuale();
                    if (att != null && att.SequenceEqual(dati)) { try { File.Delete(FileInAttesa); } catch { } return; }
                    Directory.CreateDirectory(Cartelle.Web);
                    File.WriteAllBytes(FileInAttesa, dati);
                    f.Esegui("window.onWebUpdate && window.onWebUpdate()");
                }
                catch { /* niente internet: resta la versione attuale */ }
            });
        }

        public static string InAttesa()
        {
            try { return File.Exists(FileInAttesa) ? File.ReadAllText(FileInAttesa, Encoding.UTF8) : ""; } catch { return ""; }
        }

        public static bool Applica()
        {
            if (!File.Exists(FileInAttesa)) return false;
            if (File.Exists(FileAttuale)) File.Delete(FileAttuale);
            File.Move(FileInAttesa, FileAttuale);
            return true;
        }

        /// <summary>Aggiorna un altro file dell'interfaccia (per esempio pdfjs/pdf.min.js).</summary>
        public static string Risorsa(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome) || nome.Contains("..")) throw new ArgumentException("Nome non valido");
            var dati = Scarica(Base + nome.Replace('\\', '/'));
            var p = Path.Combine(Cartelle.Web, nome.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllBytes(p, dati);
            return "ok";
        }
    }
}
