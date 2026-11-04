# Third-party notices

Airlock itself is under the MIT License (`LICENSE`). Its container image also redistributes the software below, which
is not Airlock's own (DR-05). Each entry gives the version Airlock uses, its licence and the copyright line its package
states. The licence texts follow, or are in `licenses/`, which the image carries at `/app/licenses/`.

## NuGet packages

Every package that `src/Airlock.Web` restores, transitive ones included, and so every package in its publish output.
The packages that ASP.NET Core's shared framework already provides are pruned at restore and are not redistributed. A
repository test compares this table with `src/Airlock.Web/obj/project.assets.json`. So an update of a package, or of
the SDK, which brings `Microsoft.AspNetCore.App.Internal.Assets`, changes its line here, once its licence is checked.

| Package | Version | Licence | Copyright, as the package states it |
|---|---|---|---|
| Microsoft.AspNetCore.App.Internal.Assets | 10.0.14 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.AspNetCore.OpenApi | 10.0.13 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.OpenApi | 2.12.2 | MIT | © Microsoft Corporation. All rights reserved. |
| MudBlazor | 9.11.0 | MIT | Copyright 2026 MudBlazor |
| Polly.Core | 8.8.0 | BSD-3-Clause | Copyright (c) 2015-2026, App vNext |
| Polly.RateLimiting | 8.8.0 | BSD-3-Clause | Copyright (c) 2015-2026, App vNext |
| QuestPDF | 2026.9.1 | QuestPDF Community License | Marcin Ziąbek, QuestPDF contributors |
| Scalar.AspNetCore | 2.18.3 | MIT | None stated in the package; its authors are Scalar |

The two Microsoft.AspNetCore packages also ship ASP.NET Core's notices for the code they include from others:
`licenses/microsoft-aspnetcore/THIRD-PARTY-NOTICES.TXT`, copied from the packages, where both files are the same.

### QuestPDF

Airlock uses QuestPDF under the QuestPDF Community License. QuestPDF's own summary says it is free for individuals and
businesses with annual gross revenue under USD 1,000,000, and for charitable organisations, academic institutions and
open-source projects. Public-sector entities and publicly traded companies are not eligible, whatever their revenue.
The full terms decide who needs a paid licence. They are in `licenses/questpdf/LICENSE.md`, copied from the package.

QuestPDF's native libraries, `libQuestPdfSkia.so` and `libqpdf.so` on Linux, are built from the projects below. QuestPDF
lists them in its package, and their licence texts are in `licenses/questpdf/`, copied from there:

| Project | Licence | File |
|---|---|---|
| Skia | BSD-3-Clause | `skia.txt` |
| HarfBuzz | The "Old MIT" licence | `harfbuzz.txt` |
| FreeType | The FreeType Project License | `freetype.txt` |
| libpng | The PNG Reference Library License, version 2 | `libpng.txt` |
| libjpeg-turbo | The IJG License, BSD-3-Clause and the zlib License | `libjpeg-turbo.txt` |
| libwebp | BSD-3-Clause | `libwebp.txt` |
| zlib | The zlib License | `zlib.txt` |
| Expat | MIT | `expat.txt` |
| Wuffs | Apache-2.0 | `wuffs.txt` |
| qpdf | Apache-2.0 | `qpdf.txt` |
| libgrapheme | ISC | `libgrapheme.txt` |
| The Lato font | SIL Open Font License 1.1 | `lato.txt` |
| Emscripten SDK and Ninja, which QuestPDF builds with | MIT; Apache-2.0 | `emsdk.txt`; `ninja-build.txt` |

## Syft

Syft v1.55.0, by Anchore, is in the image at `/app/bin/syft`. It is under the Apache License 2.0. Its licence text is
`licenses/syft/LICENSE`, copied from Syft's v1.55.0 release, since Syft's own image carries none. Syft's binary
includes Go modules under their own licences. The SBOM attested with each published image lists them.

## Fonts

| Font | Version | Licence | Copyright |
|---|---|---|---|
| Inter | 4.1 | SIL Open Font License 1.1 | Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter) |
| JetBrains Mono | 2.304 | SIL Open Font License 1.1 | Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono) |

Their licence texts are served with them: `wwwroot/fonts/inter/OFL.txt` and `wwwroot/fonts/jetbrains-mono/OFL.txt`.

## In the base image

The image is built on Microsoft's `mcr.microsoft.com/dotnet/aspnet` chiseled Ubuntu 24.04 image, whose files keep
their own notices:
- .NET's licence and third-party notices, in `/usr/share/dotnet/`;
- the copyright file of each Ubuntu package it installs, under `/usr/share/doc/`.

## Licence texts

### MIT License

This text covers Microsoft.AspNetCore.App.Internal.Assets, Microsoft.AspNetCore.OpenApi, Microsoft.OpenApi and
Scalar.AspNetCore, with the copyright lines in the table above. MudBlazor's own licence file, with its copyright line,
follows it.

```text
MIT License

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

MudBlazor, from its package's `LICENSE`:

```text
MIT License

Copyright (c) 2021 MudBlazor

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

### BSD 3-Clause License

Polly.Core and Polly.RateLimiting, from their packages' `LICENSE`, which is the same file in both:

```text
BSD 3-Clause License

Copyright (c) 2015-2025, App vNext

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### The others

The Apache License 2.0 (Syft), the QuestPDF licences and the texts for QuestPDF's native libraries are in `licenses/`,
as listed above. The SIL Open Font License 1.1 is in each font's `OFL.txt`.
