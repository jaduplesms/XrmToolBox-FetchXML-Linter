using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace FetchXmlLinter.XrmToolBox
{
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "FetchXML Linter")]
    [ExportMetadata("Description", "Read-only review of personal views and supplied FetchXML. No AI, query execution or automatic rewrites.")]
    [ExportMetadata("BackgroundColor", "#EAF3FB")]
    [ExportMetadata("PrimaryFontColor", "#164B78")]
    [ExportMetadata("SecondaryFontColor", "#334155")]
    public sealed class Plugin : PluginBase
    {
        static Plugin() => AppDomain.CurrentDomain.AssemblyResolve += ResolveCore;

        public override IXrmToolBoxPluginControl GetControl() => new LinterControl();

        private static Assembly? ResolveCore(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            if (requested.Name != "FetchXmlLinter.Core") return null;
            var owner = typeof(Plugin).Assembly;
            using (var stream = owner.GetManifestResourceStream("FetchXmlLinter.Core.dll"))
            {
                if (stream == null) throw new FileNotFoundException("Embedded FetchXmlLinter.Core library was not packaged.");
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return Assembly.Load(buffer.ToArray());
                }
            }
        }
    }
}
