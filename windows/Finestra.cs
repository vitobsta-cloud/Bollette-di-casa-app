using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BolletteDiCasa
{
    /// <summary>La finestra del programma: dentro c'è l'interfaccia di Bollette di casa (la stessa del telefono, in versione PC).</summary>
    public class Finestra : Form
    {
        const string HOST = "bollette.tr-software.local";
        readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(238, 241, 245) };
        CoreWebView2Environment amb;
        bool primaVolta = true;

        // il ponte con gli stessi nomi di quello Android: l'interfaccia lo trova in window.AndroidBridge
        const string PONTE_JS = @"(() => {
  if (!window.chrome || !chrome.webview || !chrome.webview.hostObjects) return;
  const h = chrome.webview.hostObjects.sync.ponte;
  const nomi = ['isPC','versionePC','getVersionCode','setArchivio','getNasConfig','setNasConfig','getNasPassword','nasTest','nasPing','nasList','nasRead','nasOpen',
    'nasWriteFile','nasWriteText','nasDeleteFolder','openFile','saveDownload','getPendingUpdate','applyUpdate','checkUpdateNow','erroreAggiornamento','aggiornaRisorsa'];
  const A = {};
  for (const n of nomi) A[n] = (...a) => h[n](...a.map(x => x === undefined || x === null ? '' : x));
  window.AndroidBridge = A;
  window.SUL_PC = true;
})();";

        public Finestra()
        {
            Text = "Bollette di casa";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(area.Left + area.Width / 20, area.Top + area.Height / 20, Math.Min(1500, area.Width * 9 / 10), Math.Min(950, area.Height * 9 / 10));
            RecuperaPosizione();
            Controls.Add(web);
            Load += (s, e) => Avvia();
            Activated += (s, e) =>
            {
                if (web.CoreWebView2 == null) return;
                Aggiornamenti.Controlla(this, false);
                Esegui("window.onAndroidResume && window.onAndroidResume()");
            };
            FormClosing += (s, e) => SalvaPosizione();
        }

        async void Avvia()
        {
            try
            {
                Cartelle.AzzeraSeProgrammaNuovo();
                Directory.CreateDirectory(Cartelle.WebView);
                amb = await CoreWebView2Environment.CreateAsync(null, Cartelle.WebView);
                await web.EnsureCoreWebView2Async(amb);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                if (MessageBox.Show("Per funzionare, Bollette di casa ha bisogno di «Microsoft Edge WebView2», un componente gratuito di Windows.\n\nVuoi aprire la pagina per installarlo? Poi riapri il programma.",
                        "Bollette di casa", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    ApriFuori("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                Close();
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Il programma non è riuscito a partire:\n" + ex.Message, "Bollette di casa", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            var w = web.CoreWebView2;
            w.Settings.IsStatusBarEnabled = false;
            w.Settings.AreDevToolsEnabled = false;
            w.Settings.AreBrowserAcceleratorKeysEnabled = false;   // F5, F3… li usa l'app (i tasti in basso)
            w.Settings.IsZoomControlEnabled = true;
            w.AddHostObjectToScript("ponte", new Ponte(this));
            await w.AddScriptToExecuteOnDocumentCreatedAsync(PONTE_JS);

            // le pagine arrivano da https://bollette.tr-software.local/… (come sul telefono): prima la versione scaricata, poi quella inclusa
            w.AddWebResourceRequestedFilter("https://" + HOST + "/*", CoreWebView2WebResourceContext.All);
            w.WebResourceRequested += Servi;
            w.NavigationStarting += (s, e) =>
            {
                var u = e.Uri ?? "";
                if ((u.StartsWith("http://") || u.StartsWith("https://")) && !u.StartsWith("https://" + HOST + "/")) { e.Cancel = true; ApriFuori(u); }
            };
            w.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                if (e.Uri.StartsWith("http")) ApriFuori(e.Uri);
            };
            w.NavigationCompleted += (s, e) =>
            {
                if (!primaVolta) return;
                primaVolta = false;
                Aggiornamenti.Controlla(this, true);
            };
            w.Navigate("https://" + HOST + "/index.html");
        }

        void Servi(object s, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var rel = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath).TrimStart('/');
                if (rel == "") rel = "index.html";
                if (rel.Contains("..")) { e.Response = amb.CreateWebResourceResponse(null, 403, "Forbidden", ""); return; }
                rel = rel.Replace('/', '\\');
                var p = Path.Combine(Cartelle.Web, rel);
                if (!File.Exists(p)) p = Path.Combine(Cartelle.WebIncluso, rel);
                if (!File.Exists(p)) { e.Response = amb.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
                e.Response = amb.CreateWebResourceResponse(new MemoryStream(File.ReadAllBytes(p)), 200, "OK",
                    "Content-Type: " + Tipo(p) + "\r\nCache-Control: no-cache");
            }
            catch
            {
                e.Response = amb.CreateWebResourceResponse(null, 500, "Error", "");
            }
        }

        static string Tipo(string p)
        {
            switch (Path.GetExtension(p).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": case ".mjs": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".wasm": return "application/wasm";
                default: return "application/octet-stream";
            }
        }

        // ---------- usati dal ponte ----------

        public void Rispondi(string id, bool ok, string testo)
        {
            Esegui("window.__bridgeCb(" + Cartelle.Json.Serialize(id ?? "") + "," + (ok ? "true" : "false") + "," + Cartelle.Json.Serialize(testo ?? "") + ")");
        }

        public void Esegui(string js)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(async () =>
            {
                try { if (web.CoreWebView2 != null) await web.CoreWebView2.ExecuteScriptAsync(js); } catch { }
            }));
        }

        public void Avviso(string testo)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(() => MessageBox.Show(this, testo, "Bollette di casa", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
        }

        public void Ricarica() { try { web.CoreWebView2?.Reload(); } catch { } }

        static void ApriFuori(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        // ---------- la finestra si riapre dove e grande come l'ultima volta ----------

        void RecuperaPosizione()
        {
            try
            {
                var d = Cartelle.Leggi();
                var t = Cartelle.Testo(d, "finestra").Split(',');
                if (t.Length < 5) return;
                var r = new Rectangle(int.Parse(t[0]), int.Parse(t[1]), int.Parse(t[2]), int.Parse(t[3]));
                foreach (var sc in Screen.AllScreens)
                    if (sc.WorkingArea.IntersectsWith(r)) { Bounds = r; if (t[4] == "max") WindowState = FormWindowState.Maximized; return; }
            }
            catch { }
        }

        void SalvaPosizione()
        {
            try
            {
                var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                var d = Cartelle.Leggi();
                d["finestra"] = r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (WindowState == FormWindowState.Maximized ? "max" : "norm");
                Cartelle.Scrivi(d);
            }
            catch { }
        }
    }
}
