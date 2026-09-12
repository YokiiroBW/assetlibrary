# Third-party components

This preview contains the Microsoft .NET runtime, published as a self-contained Windows x64 application.
Its LICENSE.txt and ThirdPartyNotices.txt from the runtime publish output remain in this package.

The Settings application includes the Windows App SDK / WinUI runtime selected by its exact package lock.
Applicable package license and third-party notices must accompany the final build.

AssetLibrary.Explorer.dll is compiled with the licensed Visual Studio 2022 C++ tools and the Windows SDK,
using the approved statically linked release C/C++ runtime. The build operator supplies the applicable
Visual Studio / Windows SDK license and redistributable notices to the packaging script with --notice.
The package neither installs Visual Studio nor grants a development-tool license.

AssetLibrary is not affiliated with or endorsed by Microsoft. The preview is unsigned; checksums verify
package consistency and do not establish publisher identity or replace an Authenticode signature.
