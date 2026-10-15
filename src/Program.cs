using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Astonia World Editor")]
[assembly: AssemblyDescription("Standalone Astonia map editor with the flat Omega theme")]
[assembly: AssemblyCompany("Astonia Omega")]
[assembly: AssemblyProduct("Astonia World Editor")]
[assembly: AssemblyVersion("0.3.2.0")]
[assembly: AssemblyFileVersion("0.3.2.0")]

namespace Astonia.Editor
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread]
        private static void Main(string[] args)
        {
            SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string map = null, workspace = null;
            for (int i = 0; i < args.Length; i++) { if (args[i] == "--workspace" && i + 1 < args.Length) workspace = args[++i]; else if (args[i].EndsWith(".map", StringComparison.OrdinalIgnoreCase)) map = args[i]; }
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "Editor error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm(map, workspace));
        }
    }
}
