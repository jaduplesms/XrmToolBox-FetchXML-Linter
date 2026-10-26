using System.ComponentModel.Composition.Hosting;
using FetchXmlLinter.XrmToolBox;
using XrmToolBox.Extensibility.Interfaces;
using Xunit;

namespace FetchXmlLinter.Host.Tests
{
    public sealed class DiscoveryTests
    {
        [Fact]
        public void HostDiscoversPluginWithRequiredMetadata()
        {
            using (var catalog = new AssemblyCatalog(typeof(Plugin).Assembly))
            using (var container = new CompositionContainer(catalog))
            {
                var export = Assert.Single(container.GetExports<IXrmToolBoxPlugin, IPluginMetadata>());
                Assert.Equal("FetchXML Linter", export.Metadata.Name);
                Assert.NotEmpty(export.Metadata.Description);
                Assert.NotEmpty(export.Metadata.BackgroundColor);
                Assert.NotEmpty(export.Metadata.PrimaryFontColor);
                Assert.NotEmpty(export.Metadata.SecondaryFontColor);
                Assert.Equal(string.Empty, export.Metadata.SmallImageBase64);
                Assert.Equal(string.Empty, export.Metadata.BigImageBase64);
                Assert.IsType<Plugin>(export.Value);
            }
        }
    }
}
