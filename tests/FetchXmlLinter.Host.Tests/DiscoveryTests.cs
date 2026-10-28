using System;
using System.ComponentModel.Composition.Hosting;
using System.Drawing;
using System.IO;
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
                AssertIcon(export.Metadata.SmallImageBase64, 32);
                AssertIcon(export.Metadata.BigImageBase64, 80);
                Assert.IsType<Plugin>(export.Value);
            }
        }

        private static void AssertIcon(string encoded, int size)
        {
            using (var stream = new MemoryStream(Convert.FromBase64String(encoded)))
            using (var icon = new Bitmap(stream))
            {
                Assert.Equal(size, icon.Width);
                Assert.Equal(size, icon.Height);
                Assert.Equal(0, icon.GetPixel(0, 0).A);
                Assert.True(icon.GetPixel(size / 2, size / 2).A > 0);
            }
        }
    }
}
