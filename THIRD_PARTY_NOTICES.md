# Third-party notices

Exo Cinema's own code is proprietary (see [LICENSE](LICENSE)). The installer also carries the components below, each under its own license. Nothing here changes those licenses; where a license needs its full text, the text ships with the app in `app\licenses` or is included here.

## libmpv (the player)

Exo Cinema plays video with libmpv, installed as `app\libmpv-2.dll`, a separate library you may replace with your own build of libmpv (see [LICENSE](LICENSE)).

| Component | Version | License | Source |
| --- | --- | --- | --- |
| mpv, by the mpv developers | v0.41.0-1349-gcf764c61d | LGPL-2.1-or-later | <https://github.com/mpv-player/mpv/tree/cf764c61d1> |
| FFmpeg, by the FFmpeg developers (built in) | N-128186-gc0f6ae570 | LGPL-3.0-or-later | <https://github.com/FFmpeg/FFmpeg/tree/c0f6ae570> |
| libplacebo, libass and the other libraries the build links in | as built | each LGPL-compatible, as listed by the build | <https://github.com/shinchiro/mpv-winbuild-cmake#information-about-packages> |

The library is zhongfly's LGPL build of libmpv, release `2026-10-29-cf764c61d1`, file `mpv-dev-lgpl-x86_64-20261029-git-cf764c61d1.7z` (SHA-256 `f62fd6192e7310397a8b4288330e8f2f838fe85a8d1e5a0f242940fae940f04d`), made by the scripts at <https://github.com/zhongfly/mpv-winbuild> with mpv built with `-Dgpl=false` and FFmpeg without its GPL parts. Exo Cinema uses it unmodified. Its license texts ship in `app\licenses`: `LGPL-2.1.txt`, `LGPL-3.0.txt` and `GPL-3.0.txt` (which LGPL 3 builds on). Its source is at the links above; for help getting it, open an issue at <https://github.com/UhhErix/ExoCinema/issues>.

## Built into Exo Cinema

| Component | License | Source |
| --- | --- | --- |
| .NET runtime, by the .NET Foundation and contributors | MIT | <https://github.com/dotnet/runtime> |
| Windows App SDK and WinUI 3, by Microsoft | MIT | <https://github.com/microsoft/WindowsAppSDK> |
| Microsoft Edge WebView2 SDK | BSD-3-Clause, below | <https://www.nuget.org/packages/Microsoft.Web.WebView2> |
| Tabler Icons, by Paweł Kuna | MIT, below | <https://github.com/tabler/tabler-icons> |
| Inter, by Rasmus Andersson | SIL Open Font License 1.1 (`app\wwwroot\fonts\LICENSE-Inter.txt`) | <https://github.com/rsms/inter> |
| ArtCNN shaders C4F32_DS and C4F16_DS, by João Chrisóstomo and Kacper Michajłow (changed only in when they run, marked `Exo_Supersample_`) | MIT (`app\Shaders\ArtCNN\LICENSE`) | <https://github.com/Artoriuz/ArtCNN> |

The installer is made with NSIS (zlib/libpng license, <https://nsis.sourceforge.io>).

## Microsoft Edge WebView2

Exo Cinema's interface runs on the Microsoft Edge WebView2 Runtime, which is part of Windows and is not distributed with Exo Cinema. It is Microsoft's, under Microsoft's terms. Setup installs it from Microsoft when a PC doesn't have it.

The WebView2 SDK's parts in `app` and its loader, built into Setup, are under this license:

```text
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## The MIT License

The .NET runtime (Copyright (c) .NET Foundation and Contributors), the Windows App SDK (Copyright (c) Microsoft Corporation) and Tabler Icons (Copyright (c) Paweł Kuna) are under the MIT License:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
