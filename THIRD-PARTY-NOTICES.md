# Third-party dependencies

The repository's MIT license applies to its original code. Host, SDK and test
packages retain their own licenses; this file does not relicense them.

Development host references XrmToolBoxPackage 1.2025.10.74 and
MscrmTools.Xrm.Connection 1.2025.9.64, alongside Microsoft SDK/framework
reference assemblies and their transitive dependencies. Test tooling includes
xUnit and Microsoft.NET.Test.Sdk. Consult the restored package metadata and
upstream license texts for the exact versions and obligations.

The package deliberately includes only this project's plugin assembly, icon,
README, MIT license and these notices. The plugin merges only this project's
original core library. It does not redistribute the XrmToolBox host,
connection library, SDK or Newtonsoft.Json DLLs; the supported XrmToolBox
installation supplies them.

## Verified license declarations

| Component | Evidence and distribution boundary |
| --- | --- |
| XrmToolBox 1.2025.10.74 | The host's [tagged source license](https://github.com/MscrmTools/XrmToolBox/blob/ceb029dcaf185a2517f98bd54b1d1bcf812232d1/LICENSE.txt) is GPLv3. XrmToolBoxPackage does not declare a license expression. Host binaries are not included. |
| MscrmTools.Xrm.Connection 1.2025.9.64 | The package's legacy license URL points to GPLv3, but [source declaring this assembly version](https://github.com/MscrmTools/MscrmTools.Xrm.Connection/tree/6808b06fe56ba91744a924ca5e9495080974d3c6) has an LGPLv3 LICENSE. This metadata/source discrepancy is unresolved; matching the assembly version does not prove a reproducible binary-to-source mapping. The library remains an external host dependency. |
| Microsoft Dynamics 365 SDK packages | Microsoft.CrmSdk.CoreAssemblies 9.0.2.59 and XrmTooling CoreAssembly/WpfControls 9.1.1.65 link to [Microsoft's SDK terms](https://go.microsoft.com/fwlink/?linkid=2108407). The restored packages also contain SDK license/notice documents. They are not merged or included. |
| ILRepack.Lib.MSBuild.Task 2.0.48 | Its NuGet package declares MIT. This is a private build-time dependency; its binaries are not shipped. The task's license declaration must not be confused with the separate ILRepack engine's terms. |

No separate plugin-linking exception was found in the reviewed XrmToolBox
license. Public binary distribution remains subject to qualified license review,
including the host's GPL terms, the connection metadata discrepancy and any
applicable combined-work obligations. MIT describes this project's original
code, not legal clearance for every combination. Absence of bundled
third-party DLLs is not by itself legal clearance.

The build audits direct and transitive dependencies against its configured
package sources and fails on reported vulnerabilities or audit errors. Results
are source- and time-dependent, not a guarantee of absence of vulnerabilities.
Do not suppress audit warnings globally or treat an unreachable feed as a clean
result. Host-installed runtime versions should be checked separately.

The Personal Views Migration project was consulted as an access-pattern
reference. No implementation was copied from it. Its reviewed repository did
not have a recognized license, so it was not treated as reusable source.
