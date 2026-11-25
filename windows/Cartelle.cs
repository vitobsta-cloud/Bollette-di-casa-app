using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace BolletteDiCasa
{
    /// <summary>Dove il programma tiene i suoi file e le impostazioni (non l'archivio: quello è sul NAS).</summary>
    static class Cartelle
    {
        public static readonly string Dati = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TR-software", "Bollette di casa");
        public static readonly string Web = Path.Combine(Dati, "web");                    // interfaccia scaricata da GitHub
        public static readonly string WebIncluso = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "www");   // quella inclusa nel programma
        public static readonly string WebView = Path.Combine(Dati, "WebView2");
        public static readonly string Apri = Path.Combine(Path.GetTempPath(), "Bollette di casa");
        static readonly string FileImpostazioni = Path.Combine(Dati, "programma.json");

        public static string Versione => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        /// <summary>Ramo di GitHub da cui arriva l'interfaccia (main per tutti; pc durante le prove).</summary>
        public static string Ramo
        {
            get
            {
                foreach (AssemblyMetadataAttribute a in Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                    if (a.Key == "Ramo" && !string.IsNullOrWhiteSpace(a.Value)) return a.Value;
                return "main";
            }
        }

        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static Dictionary<string, object> Leggi()
        {
            try
            {
                if (File.Exists(FileImpostazioni))
                    return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(FileImpostazioni)) ?? new Dictionary<string, object>();
            }
            catch { }
            return new Dictionary<string, object>();
        }

        public static void Scrivi(Dictionary<string, object> d)
        {
            Directory.CreateDirectory(Dati);
            File.WriteAllText(FileImpostazioni, Json.Serialize(d));
        }

        public static string Testo(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) : "";

        /// <summary>Dopo un programma nuovo si riparte dall'interfaccia inclusa (come sul telefono dopo un APK nuovo).</summary>
        public static void AzzeraSeProgrammaNuovo()
        {
            var d = Leggi();
            if (Testo(d, "programma") == Versione + "/" + Ramo) return;
            try { if (Directory.Exists(Web)) Directory.Delete(Web, true); } catch { }
            d["programma"] = Versione + "/" + Ramo;
            Scrivi(d);
        }
    }
}
