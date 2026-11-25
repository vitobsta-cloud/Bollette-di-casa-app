using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BolletteDiCasa
{
    /// <summary>
    /// L'archivio sul NAS, letto e scritto come fa Esplora file (\\192.168.1.13\Public\bollette di casa).
    /// Fa le stesse cose della parte Android (Nas.java): stessi percorsi, stesso elenco, stessi messaggi.
    /// </summary>
    static class Archivio
    {
        const int MAX_DEPTH = 4;
        const string INFO_FILE = "bolletta.json";
        static readonly Regex DOC_EXT = new Regex(@"\.(pdf|jpe?g|png|webp|bmp|heic|heif|tiff?|gif)$", RegexOptions.IgnoreCase);
        static readonly object blocco = new object();

        // ---------- impostazioni del NAS (la password è cifrata per l'utente di Windows) ----------

        public class Cfg { public string host = "", share = "", folder = "", user = "", domain = "", password = ""; }

        public static Cfg Carica()
        {
            var d = Cartelle.Leggi();
            var c = new Cfg
            {
                host = Cartelle.Testo(d, "nasHost"), share = Cartelle.Testo(d, "nasShare"), folder = Cartelle.Testo(d, "nasFolder"),
                user = Cartelle.Testo(d, "nasUser"), domain = Cartelle.Testo(d, "nasDomain")
            };
            try
            {
                var p = Cartelle.Testo(d, "nasPassword");
                if (p != "") c.password = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(p), null, DataProtectionScope.CurrentUser));
            }
            catch { }
            return c;
        }

        public static string ConfigJson()
        {
            var c = Carica();
            return Cartelle.Json.Serialize(new Dictionary<string, object> {
                {"host", c.host}, {"share", c.share}, {"folder", c.folder}, {"user", c.user}, {"domain", c.domain}, {"hasPassword", c.password != ""} });
        }

        public static void SalvaConfig(string json)
        {
            var o = Cartelle.Json.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
            var d = Cartelle.Leggi();
            foreach (var k in new[] { "host", "share", "folder", "user", "domain" })
                if (o.ContainsKey(k)) d["nas" + char.ToUpper(k[0]) + k.Substring(1)] = Convert.ToString(o[k] ?? "").Trim();
            var pw = o.ContainsKey("password") ? Convert.ToString(o["password"] ?? "") : "";
            if (pw != "") d["nasPassword"] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(pw), null, DataProtectionScope.CurrentUser));
            Cartelle.Scrivi(d);
            collegato = null;
        }

        public static string Password() => Carica().password;

        // ---------- percorsi ----------

        static string Pulisci(string p) => (p ?? "").Replace('/', '\\').Trim('\\');

        static string Condivisione(Cfg c) => @"\\" + c.host.Trim('\\', '/') + @"\" + Pulisci(c.share);

        static string Radice(Cfg c)
        {
            if (c.host.Trim() == "" || c.share.Trim() == "") throw new InvalidOperationException("Il NAS non è ancora impostato: apri Impostazioni → Dove salvare.");
            var f = Pulisci(c.folder);
            return f == "" ? Condivisione(c) : Condivisione(c) + @"\" + f;
        }

        /// <summary>Unisce la cartella dell'archivio e un percorso relativo ("Casa mia/Enel - Luce/2026"), senza uscire dall'archivio.</summary>
        static string Unisci(string radice, string rel)
        {
            var parti = Pulisci(rel).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parti.Any(p => p == ".." || p == ".")) throw new ArgumentException("Percorso non valido");
            return parti.Length == 0 ? radice : radice + @"\" + string.Join(@"\", parti);
        }

        static bool Salta(string nome) => nome.StartsWith(".") || nome.StartsWith("@") || nome.StartsWith("#") || nome.StartsWith("_Cestino")
            || nome.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || nome.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

        // ---------- collegamento (con utente e password, se Windows non è già collegato) ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class NETRESOURCE { public int dwScope, dwType, dwDisplayType, dwUsage; public string lpLocalName, lpRemoteName, lpComment, lpProvider; }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetAddConnection2(NETRESOURCE r, string password, string user, int flags);

        static string collegato;   // condivisione già aperta in questa sessione

        static Cfg Collega()
        {
            var c = Carica();
            Radice(c);
            var share = Condivisione(c);
            if (collegato == share) return c;
            if (!Directory.Exists(share) && c.user != "")
            {
                var u = c.domain != "" ? c.domain + @"\" + c.user : c.user;
                int r = WNetAddConnection2(new NETRESOURCE { dwType = 1, lpRemoteName = share }, c.password, u, 0);
                // 1219 = Windows è già collegato a quel NAS con un altro utente: si usa quello
                if (r != 0 && r != 1219 && r != 85)
                    throw new IOException(r == 86 || r == 1326 ? "Utente o password del NAS non corretti: controllali in Impostazioni → Dove salvare."
                        : r == 53 || r == 67 || r == 1231 || r == 1203 ? "Il NAS non risponde: controlla che il PC sia nella rete di casa e che il NAS sia acceso."
                        : "Collegamento al NAS non riuscito (errore " + r + ").");
            }
            if (!Directory.Exists(share)) throw new IOException("Il NAS non risponde: controlla che il PC sia nella rete di casa e che il NAS sia acceso.");
            collegato = share;
            return c;
        }

        public static string Spiega(Exception e)
        {
            if (e is UnauthorizedAccessException) return "Accesso negato alla cartella del NAS: controlla utente e password in Impostazioni → Dove salvare.";
            if (e is DirectoryNotFoundException) return "Cartella non trovata nel NAS.";
            if (e is FileNotFoundException) return "File non trovato nel NAS.";
            if (e is IOException && Regex.IsMatch(e.Message, "rete|network|percorso di rete", RegexOptions.IgnoreCase))
                return "Il NAS non risponde: controlla che il PC sia nella rete di casa e che il NAS sia acceso.";
            return e.Message;
        }

        // ---------- operazioni (le stesse del telefono) ----------

        public static string Ping()
        {
            lock (blocco)
            {
                var c = Collega(); var r = Radice(c);
                if (Pulisci(c.folder) != "" && !Directory.Exists(r))
                    throw new IOException("Cartella \"" + c.folder + "\" non trovata nella condivisione " + c.share + ": apri Impostazioni → Dove salvare e tocca Avvia verifica.");
                return "ok";
            }
        }

        public static string Test()
        {
            lock (blocco)
            {
                var c = Collega(); var r = Radice(c);
                bool creata = false;
                if (!Directory.Exists(r)) { Directory.CreateDirectory(r); creata = true; }
                int n = new DirectoryInfo(r).GetDirectories().Count(d => !Salta(d.Name));
                var prova = Path.Combine(r, ".bollette_test_" + DateTime.Now.Ticks + ".tmp");
                File.WriteAllText(prova, "ok"); File.Delete(prova);
                return (creata ? "Cartella dell'archivio creata. " : "") + "NAS raggiungibile e scrivibile: " + n + " cartelle in " + r + ".";
            }
        }

        /// <summary>JSON: [{path, files:[{name,size,mtime}], info}] per ogni cartella con documenti o bolletta.json.</summary>
        public static string Elenco()
        {
            lock (blocco)
            {
                var c = Collega(); var r = Radice(c);
                var out_ = new List<object>();
                Cammina(r, "", 0, out_);
                return Cartelle.Json.Serialize(out_);
            }
        }

        static readonly DateTime EPOCA = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static void Cammina(string radice, string rel, int livello, List<object> out_)
        {
            var dir = new DirectoryInfo(Unisci(radice, rel));
            var files = new List<object>();
            string info = null;
            var sotto = new List<string>();
            foreach (var x in dir.EnumerateFileSystemInfos())
            {
                if (Salta(x.Name)) continue;
                if ((x.Attributes & FileAttributes.Directory) != 0) { if (livello < MAX_DEPTH) sotto.Add(x.Name); }
                else if (x.Name.Equals(INFO_FILE, StringComparison.OrdinalIgnoreCase)) { try { info = File.ReadAllText(x.FullName, Encoding.UTF8); } catch { } }
                else if (DOC_EXT.IsMatch(x.Name))
                    files.Add(new Dictionary<string, object> { { "name", x.Name }, { "size", ((FileInfo)x).Length }, { "mtime", (long)(x.LastWriteTimeUtc - EPOCA).TotalMilliseconds } });
            }
            if (rel != "" && (files.Count > 0 || info != null))
            {
                var e = new Dictionary<string, object> { { "path", rel }, { "files", files } };
                if (info != null) e["info"] = info;
                out_.Add(e);
            }
            foreach (var d in sotto) Cammina(radice, rel == "" ? d : rel + "/" + d, livello + 1, out_);
        }

        public static byte[] Leggi(string rel)
        {
            lock (blocco) { var c = Collega(); return File.ReadAllBytes(Unisci(Radice(c), rel)); }
        }

        /// <summary>Scrive un file nuovo; se il nome c'è già aggiunge _2, _3… Restituisce il nome usato.</summary>
        public static string ScriviFile(string relDir, string nome, byte[] dati)
        {
            lock (blocco)
            {
                var c = Collega(); var dir = Unisci(Radice(c), relDir);
                Directory.CreateDirectory(dir);
                string b = Path.GetFileNameWithoutExtension(nome), ext = Path.GetExtension(nome), usato = nome;
                for (int k = 2; File.Exists(Path.Combine(dir, usato)); k++) usato = b + "_" + k + ext;
                File.WriteAllBytes(Path.Combine(dir, usato), dati);
                return usato;
            }
        }

        public static void ScriviTesto(string relDir, string nome, string testo)
        {
            lock (blocco)
            {
                var c = Collega(); var dir = Unisci(Radice(c), relDir);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, nome), testo, new UTF8Encoding(false));
            }
        }

        /// <summary>Elimina i file della cartella della bolletta e la cartella (non tocca sottocartelle); poi la cartella sopra, se vuota.</summary>
        public static string EliminaCartella(string relDir)
        {
            lock (blocco)
            {
                if (Pulisci(relDir) == "") throw new ArgumentException("Cartella non valida");
                var c = Collega(); var r = Radice(c); var dir = Unisci(r, relDir);
                if (!Directory.Exists(dir)) return "già eliminata";
                bool sottocartelle = false;
                foreach (var x in new DirectoryInfo(dir).GetFileSystemInfos())
                {
                    if ((x.Attributes & FileAttributes.Directory) != 0) sottocartelle = true;
                    else { x.Attributes = FileAttributes.Normal; x.Delete(); }
                }
                if (!sottocartelle) Directory.Delete(dir, false);
                var sopra = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(sopra) && !sopra.Equals(r, StringComparison.OrdinalIgnoreCase) && Directory.Exists(sopra) && !Directory.EnumerateFileSystemEntries(sopra).Any())
                    Directory.Delete(sopra, false);
                return "ok";
            }
        }
    }
}
