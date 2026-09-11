# Third-party notices

The released archive ships the components below alongside this project's own assemblies.

Each is used unmodified, under its own licence, and each licence requires that its copyright notice travel with the binary — which is what this file is for.

Nothing from Siemens is redistributed. The Openness assemblies this project compiles against are licensed separately by Siemens, live outside this repository, and are resolved by TIA Portal from its own installation at run time.

---

## Newtonsoft.Json 13.0.4

Copyright © James Newton-King 2008 [https://www.newtonsoft.com/json](https://www.newtonsoft.com/json) · MIT

Reads and writes `config.json` in `Satellite.ConfigEditor`, and the JSON-RPC envelopes in `S7PlcWebserverApi`.

## DocumentFormat.OpenXml 3.5.1

© Microsoft Corporation. All rights reserved. [https://github.com/dotnet/Open-XML-SDK](https://github.com/dotnet/Open-XML-SDK) · MIT

Writes the `.xlsx` workbooks in `Satellite.DataBlockSnapshot`. Ships together with `DocumentFormat.OpenXml.Framework` 3.5.1, under the same copyright and licence.

---

## The MIT License

Both components above are distributed under the MIT License, reproduced here once:

```
Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

---

**Keeping this file true is part of taking a dependency.** Both live in project files as `PackageReference`, and the version numbers above are the ones that ship. A package added, removed or upgraded without this file following is a licence notice that has quietly stopped being accurate.
