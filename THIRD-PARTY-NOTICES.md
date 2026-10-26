# Third-party dependencies

The repository's MIT license applies to its original code. Host, SDK and test
packages retain their own licenses; this file does not relicense them.

Development host references XrmToolBoxPackage 1.2025.10.74 and
MscrmTools.Xrm.Connection 1.2025.9.64, alongside Microsoft SDK/framework
reference assemblies and their transitive dependencies. Test tooling includes
xUnit and Microsoft.NET.Test.Sdk. Consult the restored package metadata and
upstream license texts for the exact versions and obligations.

The alpha package deliberately includes only this project's plugin assembly,
README and MIT license. The plugin embeds only this project's original core
library. It does not redistribute the XrmToolBox host, connection library, SDK
or Newtonsoft.Json DLLs; the supported XrmToolBox installation supplies them.

The connection package declares GPL-3.0 licensing in its package metadata.
The implications for plugin linking/distribution require explicit review before
public preview; absence of bundled third-party DLLs is not by itself legal
clearance. Dependency vulnerability and compatibility checks also remain release
gates; do not suppress audit warnings globally.

The Personal Views Migration project was consulted as an access-pattern
reference. No implementation was copied from it. Its reviewed repository did
not have a recognized license, so it was not treated as reusable source.
