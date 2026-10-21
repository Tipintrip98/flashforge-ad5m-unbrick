// Stark HUD - StarkHUD-Setup.exe
// Un solo file da scaricare: contiene il pack completo (pack.zip incorporato), lo estrae in
// %LOCALAPPDATA%\StarkHUD\pack e apre l'installer grafico (StarkUI.ps1, Windows PowerShell 5.1).
// Non richiede privilegi di amministratore e non installa nulla da solo: decide l'utente nell'installer.
//
//   StarkHUD-Setup.exe                 apre l'installer
//   StarkHUD-Setup.exe --ripristina    ripristino d'emergenza (come Ripristina.cmd)
//   StarkHUD-Setup.exe --estrai DIR    estrae soltanto il pack in DIR (verifica in CI)
//   StarkHUD-Setup.exe --test PNG      estrae e apre l'installer in modalita' test, con screenshot
//
// Compilato da tools\Build-Setup.ps1 con il compilatore C# incluso in Windows (.NET Framework 4).
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Stark HUD - installer")]
[assembly: AssemblyDescription("Tema per Windows 10 in stile HUD futuristico. Progetto fan-made non ufficiale.")]
[assembly: AssemblyProduct("Stark HUD")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class StarkSetup
{
    const string Title = "Stark HUD";

    [STAThread]
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        string arg = args.Length > 1 ? args[1] : null;
        try
        {
            if (mode == "--estrai" && arg != null)
            {
                Extract(Path.GetFullPath(arg));
                return 0;
            }

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"StarkHUD\pack");
            try
            {
                Extract(dir);
            }
            catch (IOException)
            {
                // un installer gia' aperto tiene in uso i file della versione precedente
                MessageBox.Show("L'installer Stark HUD e' gia' aperto.\nChiudilo e riprova.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("L'installer Stark HUD e' gia' aperto.\nChiudilo e riprova.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }

            if (mode == "--ripristina")
            {
                string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                Start(cmd, "/c \"\"" + Path.Combine(dir, "Ripristina.cmd") + "\"\"", dir, false, true);
                return 0;
            }

            string script = "-File \"" + Path.Combine(dir, "StarkUI.ps1") + "\"";
            if (mode == "--test" && arg != null)
            {
                return Start(PowerShell(), "-NoProfile -ExecutionPolicy Bypass -STA " + script + " -SmokeTest -Screenshot \"" + Path.GetFullPath(arg) + "\"", dir, true, false);
            }
            Start(PowerShell(), "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden " + script, dir, false, false);
            return 0;
        }
        catch (Exception ex)
        {
            if (mode == "--estrai" || mode == "--test")
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            MessageBox.Show("Impossibile avviare l'installer:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static string PowerShell()
    {
        // processo a 64 bit su Windows a 64 bit: System32 e' quello giusto (StarkUI.ps1 controlla comunque)
        return Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
    }

    static int Start(string exe, string arguments, string dir, bool wait, bool visible)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe, arguments);
        psi.UseShellExecute = false;
        psi.WorkingDirectory = dir;
        psi.CreateNoWindow = !visible;
        Process p = Process.Start(psi);
        if (!wait) { return 0; }
        p.WaitForExit();
        return p.ExitCode;
    }

    static void Extract(string dir)
    {
        // copia pulita a ogni avvio: il pack non contiene dati dell'utente (backup e log stanno in StarkHUD\state e \logs)
        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
        Directory.CreateDirectory(dir);
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("StarkHUD.pack.zip"))
        {
            if (s == null) { throw new InvalidOperationException("pack mancante nell'eseguibile"); }
            using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                zip.ExtractToDirectory(dir);
            }
        }
    }
}
