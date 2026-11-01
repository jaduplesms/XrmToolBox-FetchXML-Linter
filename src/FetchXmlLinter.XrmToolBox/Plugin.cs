using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace FetchXmlLinter.XrmToolBox
{
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "FetchXML Linter")]
    [ExportMetadata("Description", "Read-only review of personal views, offline-profile filters and supplied FetchXML. No AI, query execution or automatic rewrites.")]
    [ExportMetadata("BackgroundColor", "#EAF3FB")]
    [ExportMetadata("PrimaryFontColor", "#164B78")]
    [ExportMetadata("SecondaryFontColor", "#334155")]
    [ExportMetadata("SmallImageBase64", PluginImages.Small)]
    [ExportMetadata("BigImageBase64", PluginImages.Big)]
    public sealed class Plugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl() => new LinterControl();
    }
}
