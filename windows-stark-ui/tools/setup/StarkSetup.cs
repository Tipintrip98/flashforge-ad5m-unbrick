// Tipaldi AI - TipaldiAI-Setup.exe
// Un solo file da scaricare: contiene il pack completo (pack.zip incorporato), lo estrae in
// %LOCALAPPDATA%\TipaldiAI\pack e apre l'installer grafico (StarkUI.ps1).
// Lo script gira dentro questo exe (motore di Windows PowerShell 5.1 ospitato, nessun powershell.exe):
// per Windows l'installer e' "Tipaldi AI" con la sua icona, non una finestra di PowerShell.
// Non richiede privilegi di amministratore e non installa nulla da solo: decide l'utente nell'installer.
//
//   TipaldiAI-Setup.exe                 apre l'installer
//   TipaldiAI-Setup.exe --ripristina    ripristino d'emergenza (come Ripristina.cmd)
//   TipaldiAI-Setup.exe --estrai DIR    estrae soltanto il pack in DIR (verifica in CI)
//   TipaldiAI-Setup.exe --test PNG      estrae e apre l'installer in modalita' test, con screenshot
//
// Compilato da tools\Build-Setup.ps1 con il compilatore C# incluso in Windows (.NET Framework 4).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Tipaldi AI - installer")]
[assembly: AssemblyDescription("Tema per Windows 10 in stile HUD futuristico. Progetto fan-made non ufficiale.")]
[assembly: AssemblyProduct("Tipaldi AI")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class StarkSetup
{
    const string Title = "Tipaldi AI";

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

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TipaldiAI\pack");
            try
            {
                Extract(dir);
            }
            catch (IOException)
            {
                // un installer gia' aperto tiene in uso i file della versione precedente
                MessageBox.Show("L'installer Tipaldi AI e' gia' aperto.\nChiudilo e riprova.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("L'installer Tipaldi AI e' gia' aperto.\nChiudilo e riprova.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }

            if (mode == "--ripristina")
            {
                string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                Start(cmd, "/c \"\"" + Path.Combine(dir, "Ripristina.cmd") + "\"\"", dir, false, true);
                return 0;
            }

            string script = Path.Combine(dir, "StarkUI.ps1");
            Dictionary<string, object> p = new Dictionary<string, object>();
            if (mode == "--test" && arg != null)
            {
                p["SmokeTest"] = true;
                p["Screenshot"] = Path.GetFullPath(arg);
            }
            return RunHosted(script, dir, p);
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

    /// <summary>Esegue lo script nel motore PowerShell ospitato, sul thread STA principale (WPF).</summary>
    static int RunHosted(string script, string dir, Dictionary<string, object> parameters)
    {
        // equivalente di -ExecutionPolicy Bypass, solo per questo processo
        Environment.SetEnvironmentVariable("PSExecutionPolicyPreference", "Bypass");
        Directory.SetCurrentDirectory(dir);
        using (Runspace rs = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault()))
        {
            rs.ApartmentState = ApartmentState.STA;
            rs.ThreadOptions = PSThreadOptions.UseCurrentThread;
            rs.Open();
            rs.SessionStateProxy.Path.SetLocation(dir);
            using (PowerShell ps = PowerShell.Create())
            {
                ps.Runspace = rs;
                ps.AddCommand(script);
                foreach (KeyValuePair<string, object> kv in parameters) { ps.AddParameter(kv.Key, kv.Value); }
                ps.Streams.Information.DataAdded += (s, e) => Console.WriteLine(ps.Streams.Information[e.Index]);
                ps.Streams.Error.DataAdded += (s, e) => Console.Error.WriteLine(ps.Streams.Error[e.Index]);
                try { ps.Invoke(); }
                catch (RuntimeException ex)
                {
                    // errore fermante: lo script l'ha gia' mostrato (trap) prima di chiudersi
                    Console.Error.WriteLine(ex);
                    return 1;
                }
                object code = rs.SessionStateProxy.GetVariable("LASTEXITCODE");
                return code is int ? (int)code : 0;
            }
        }
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
