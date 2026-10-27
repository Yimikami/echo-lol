# Third-party notices

Echo's original source is GPL-3.0-or-later. The emblem and design mockups were generated for Echo with OpenAI Imagegen; generation prompts are retained in `assets/brand/provenance.json` and `docs/design/`.

OBS Studio / libobs is copyright its contributors and licensed GPL-2.0-or-later. Echo uses the public C API through original declarations. No ObsKit.NET, Segra or RePlays source is incorporated. See https://github.com/obsproject/obs-studio and the OBS licenses copied into bundled runtimes.

FFmpeg is copyright its contributors. License obligations depend on the exact build and enabled codecs; this development setup copies an existing local build. A public binary release must include that build's license/configuration notices and corresponding source information. See https://ffmpeg.org/legal.html. Do not publish the locally assembled runtime as a release without completing that binary-distribution manifest. OBS win-capture may fetch its upstream compatibility metadata; Echo does not upload clips or account data.

PresentMon is an optional, separately downloaded measurement tool from Intel's GameTechDev/PresentMon project, under the MIT license. It is not bundled with Echo or required for recording. See https://github.com/GameTechDev/PresentMon.

WPF UI 4.3.0 is copyright Leszek Pomianowski and contributors, licensed MIT. Its Fluent System Icons and other included components retain their upstream notices in `assets/licenses/WPF-UI-ThirdPartyNotices.txt`; the package license is in `assets/licenses/WPF-UI-LICENSE.md`. These notices are copied into local app packages. See https://github.com/lepoco/wpfui.

Phosphor Core regular icons are copyright Phosphor Icons and licensed MIT. Exact upstream SVG assets and their commit are retained under `assets/icons/phosphor/`; the UI uses a format conversion into WPF geometries. See `assets/licenses/Phosphor-LICENSE.txt` and https://github.com/phosphor-icons/core.

Manrope is copyright the Manrope Project Authors and licensed SIL OFL 1.1. The original Google Fonts variable font is retained in `assets/font-source/`; 400/500/600/700 static instances are embedded in the UI. See `assets/licenses/Manrope-OFL.txt`, `assets/design-provenance.json` and https://github.com/google/fonts/tree/main/ofl/manrope. No system-wide font installation is required.

.NET and WPF are Microsoft projects under the MIT license. This initial package is framework-dependent and requires the .NET 10 Desktop Runtime.

Echo isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games, and all associated properties are trademarks or registered trademarks of Riot Games, Inc.
