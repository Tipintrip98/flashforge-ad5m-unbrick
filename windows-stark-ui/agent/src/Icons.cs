// Tipaldi AI - icone dei contesti (cartella icons accanto all'exe, generate da tools/build-icons.js).
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace TipaldiAI
{
    public static class Icons
    {
        public static string Dir { get { return Path.Combine(Paths.AppDir, "icons"); } }

        /// <summary>File .ico per un contesto (id o nome) o per le categorie Installer/Archivi.</summary>
        public static string ForContext(string idOrName)
        {
            string id = idOrName;
            ContextDef c = Contexts.All.FirstOrDefault(x => x.Id == idOrName || x.Name == idOrName);
            if (c != null) { id = c.Glyph; }
            else if (idOrName == "Installer") { id = "installer"; }
            else if (idOrName == "Archivi") { id = "archive"; }
            string p = Path.Combine(Dir, id + ".ico");
            return File.Exists(p) ? p : null;
        }

        /// <summary>Icona Tipaldi AI su una cartella creata dal riordino (desktop.ini, come fa Esplora file).</summary>
        public static void ApplyFolderIcon(string folder, string context)
        {
            try
            {
                string ico = ForContext(context);
                if (ico == null) { return; }
                string ini = Path.Combine(folder, "desktop.ini");
                if (File.Exists(ini)) { return; }
                File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + ico + ",0\r\nInfoTip=Ordinato da Tipaldi AI\r\n", Encoding.Unicode);
                File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            }
            catch (Exception) { }
        }
    }
}
