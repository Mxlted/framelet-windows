# Third-party notices

## .NET and Microsoft Visual C++ runtime distribution

Self-contained Windows packages include the .NET runtime and Windows Desktop runtime under their upstream licenses. The package's `licenses` directory includes each runtime's license and third-party notices. App-local Microsoft Visual C++ release runtime DLLs are redistributed unmodified from Visual Studio's redistributable directory; see `licenses/MICROSOFT-VC-RUNTIME.txt` in the package.

## FFmpeg

FFmpeg is not distributed with Framelet. It is an optional user-provided executable. Its license and codec configuration depend on the build the user installs; see https://ffmpeg.org/legal.html. Native recording has no FFmpeg dependency.

## ScreenRecorderLib 7.0.1

Framelet distributes ScreenRecorderLib.dll from the pinned NuGet package. Source: https://github.com/sskodje/ScreenRecorderLib. License source: the package LICENSE file and https://github.com/sskodje/ScreenRecorderLib/blob/master/LICENSE.
MIT License

Copyright (c) 2017 Sverre Skodje

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

## fifo_map (embedded by ScreenRecorderLib)

Source: https://github.com/sskodje/ScreenRecorderLib/blob/master/ScreenRecorderLibNative/fifo_map.h.

The code is licensed under the MIT License <http://opensource.org/licenses/MIT>:
Copyright (c) 2015-2017 Niels Lohmann.
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Microsoft desktop duplication sample shaders (embedded by ScreenRecorderLib)

The upstream PixelShader.hlsl and VertexShader.hlsl retain Microsoft copyright notices and the AS IS disclaimer. Corresponding original: https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/DXGIDesktopDuplication/cpp. License: https://github.com/microsoft/Windows-classic-samples/blob/main/LICENSE.

The MIT License (MIT)

Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
 all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 THE SOFTWARE.

Portions of this repo are provided under the SIL Open Font License.
See the LICENSE file in individual samples for additional details.

## Windows system APIs

Windows Graphics Capture, Direct3D 11, Media Foundation and WASAPI are operating-system components. Framelet uses the installed Windows APIs; their system binaries are not copied into this repository or redistributed as part of its source.

