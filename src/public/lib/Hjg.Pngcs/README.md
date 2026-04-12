# PNGCS

A small library to read/write huge PNG files in C#.

## Overview

PngCs is a C# library to read/write PNG images.

It provides a simple API for progressive (sequential line-oriented) reading and writing. It's specially suitable for huge images, which one does not want to load fully in memory.

## Installation

Install the NuGet package:

```powershell
dotnet add package IO.Github.Hcoona.Pngcs
```

Or via Package Manager Console:

```powershell
Install-Package IO.Github.Hcoona.Pngcs
```

## Features

- **Modern .NET support**: Multi-targeting for `netstandard2.0`, `netstandard2.1`, `net8.0`, and `net9.0`
- **Native AOT compatibility**: Full support for Native Ahead-of-Time compilation
- Supports all PNG spec color models and bitdepths:
    - RGB8/RGB16/RGBA8/RGBA16
    - G8/4/2/1
    - GA8/4/2/1
    - PAL8/4/2/1
- All filters/compression settings
- Support for Chunks (metadata)
- **API Compatibility**: API aligned with `Hjg.Pngcs` v1.1.4. See `CompatibilitySuppressions.xml` for details.
- **Note**: Does not support interlaced images

## Origin

This is a port of the [PngJ library (Java)](http://code.google.com/p/pngj/). The API, documentation and samples from PNGJ apply also to this PngCs library: <http://code.google.com/p/pngj/wiki/Overview>

## History

See `ChangeLog.md`

## Authors

1. Hernan J Gonzalez - <hgonzalez@gmail.com> - [Stack Overflow Profile](http://stackoverflow.com/users/277304/leonbloy) - The original author.
2. Shuai Zhang - <zhangshuai.ustc@gmail.com> - Current maintainer.

## License

This project is licensed under the GNU General Public License v3.0 or later (GPL v3+). See `LICENSE.GPL.txt` for details.

The NuGet package is distributed under the terms of LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See [LICENSE.LGPLv3-linking-exception.txt](https://github.com/hcoona/three/blob/main/LICENSE.LGPLv3-linking-exception.txt) for details.

Portions of the code are derived from an open source project originally licensed under the Apache License, Version 2.0 by Hernán J. González. The original copyright and license notice are preserved in accordance with the Apache License.

For more information, see the files:

- `LICENSE.GPL.txt` (GPL v3+)
- `LICENSE.LGPLv3-linking-exception.txt` (LGPL v3.0 with linking exception)
- `LICENSE.APACHE.txt` (Apache 2.0)
