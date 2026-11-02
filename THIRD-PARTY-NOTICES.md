# Third-party dependencies

The project is licensed under GPL-3.0-only. Host, SDK and test packages retain
their own licenses; this file does not relicense them.

Development host references XrmToolBoxPackage 1.2025.10.74 and
MscrmTools.Xrm.Connection 1.2025.9.64, alongside Microsoft SDK/framework
reference assemblies and their transitive dependencies. Test tooling includes
xUnit and Microsoft.NET.Test.Sdk. Consult the restored package metadata and
upstream license texts for the exact versions and obligations.

The package deliberately includes only this project's plugin assembly, icon,
README, project, historical and dependency license texts, and these notices.
The plugin merges only this project's original core library. It does not redistribute the XrmToolBox host,
connection library, SDK or Newtonsoft.Json DLLs; the supported XrmToolBox
installation supplies them.

## Verified license declarations

| Component | Evidence and distribution boundary |
| --- | --- |
| XrmToolBox 1.2025.10.74 | The host's [tagged source license](https://github.com/MscrmTools/XrmToolBox/blob/ceb029dcaf185a2517f98bd54b1d1bcf812232d1/LICENSE.txt) is GPLv3. XrmToolBoxPackage does not declare a license expression. Host binaries are not included. |
| MscrmTools.Xrm.Connection 1.2025.9.64 | The [maintainer confirmed the repository moved to LGPL on May 5, 2020](https://github.com/MscrmTools/MscrmTools.Xrm.Connection/issues/92#issuecomment-624034522), matching the [license-change commit](https://github.com/MscrmTools/MscrmTools.Xrm.Connection/commit/b573cc1cd1afa448c225a6fb23ffda43a236c3c7). The [source declaring assembly version 1.2025.9.64](https://github.com/MscrmTools/MscrmTools.Xrm.Connection/tree/6808b06fe56ba91744a924ca5e9495080974d3c6) has an LGPLv3 LICENSE. The package's legacy URL still points to GPLv3; its metadata is inconsistent with that documented source license. Matching versions does not independently establish a reproducible binary-to-source mapping. The library remains an external host dependency. |
| Microsoft Dynamics 365 SDK packages | Microsoft.CrmSdk.CoreAssemblies 9.0.2.59 and XrmTooling CoreAssembly/WpfControls 9.1.1.65 link to [Microsoft's SDK terms](https://go.microsoft.com/fwlink/?linkid=2108407). The restored packages also contain SDK license/notice documents. They are not merged or included. |
| ILRepack.Lib.MSBuild.Task 2.0.48 | Its NuGet package declares MIT. This is a private build-time dependency; its binaries are not shipped. The task's license declaration must not be confused with the separate ILRepack engine's terms. |

## Source licensing and distribution

Future releases are offered under GPL-3.0-only, aligning the plugin's project
license with the GPLv3 host. Earlier revisions were offered under MIT (Expat),
which is [GPL-compatible](https://www.gnu.org/licenses/license-list.html#Expat).
Existing MIT permissions are not revoked. The original copyright and MIT text
are preserved in LICENSE-MIT-LEGACY for attribution and licensing history; that
file is not an alternative license grant for new GPL-only changes.

No separate plugin-linking exception was found in the reviewed XrmToolBox
license. This plugin loads in the host process and uses host interfaces and
classes. Keeping dependency DLLs outside the package is a packaging boundary,
not proof that combined-work obligations do not apply. Distribution must satisfy
the applicable licensing, notices and Corresponding Source requirements.
The project's GPL selection is not an exception to third-party terms.
See the [host's GPLv3 text](https://github.com/MscrmTools/XrmToolBox/blob/ceb029dcaf185a2517f98bd54b1d1bcf812232d1/LICENSE.txt),
particularly sections 5 and 6.

LGPLv3 permits combined works under terms of the distributor's choice subject
to its conditions, including notices, license copies and relinking or suitable
shared-library requirements. An external library is not automatically exempt
from those conditions. See [LGPLv3 section 4](https://www.gnu.org/licenses/lgpl-3.0.html#section4).

This plugin uses the host-provided MscrmTools.Xrm.Connection library; the library
and its use are covered by LGPLv3 under the version-matched source license
identified above. Copies of [GPLv3](LICENSE) and [LGPLv3](LICENSE-LGPL-3.0)
accompany the package. The library remains dynamically linked rather than
merged into the plugin. These LGPL notices apply to that dependency, not as an
alternative license for this project's GPL-3.0-only code.

The [Tool Library documentation](https://www.xrmtoolbox.com/documentation/for-developers/deploy-your-plugin-in-plugins-store/)
explicitly accommodates tools that are not open source. That is catalog policy,
not a separate copyright-license exception.

Before public binary distribution, provide access to the matching Corresponding
Source, including build scripts, with clear source-download directions alongside
the binary. Keep required notices and license texts. Review the pinned dependency
terms, including the Microsoft SDK terms and the connection package's inconsistent
metadata, with the upstream maintainers or a qualified reviewer where necessary.
The maintainer's LGPL statement establishes the connection repository's
relicensing intent; neither that statement nor changing this project's license
is blanket clearance for every binary combination.

The build audits direct and transitive dependencies against its configured
package sources and fails on reported vulnerabilities or audit errors. Results
are source- and time-dependent, not a guarantee of absence of vulnerabilities.
Do not suppress audit warnings globally or treat an unreachable feed as a clean
result. Host-installed runtime versions should be checked separately.

The Personal Views Migration project was consulted as an access-pattern
reference. No implementation was copied from it. Its reviewed repository did
not have a recognized license, so it was not treated as reusable source.
