using System;
using System.Threading;
using System.Windows.Forms;

namespace BolletteDiCasa
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // una sola finestra aperta alla volta
            using (var m = new Mutex(true, "TR-software.BolletteDiCasa", out bool primo))
            {
                if (!primo)
                {
                    MessageBox.Show("Bollette di casa è già aperto.", "Bollette di casa", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Finestra());
            }
        }
    }
}
