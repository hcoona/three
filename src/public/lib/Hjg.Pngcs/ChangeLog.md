
# PngCs Changelog

A C# port of the PNG Java library.

## Version History on GitHub

Change logs for <https://github.com/hcoona/pngcs>.

### Version 1.3.1

- **License**: The NuGet package is distributed under the terms of LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See [LICENSE.LGPLv3-linking-exception.txt](https://dev.azure.com/zhangshuai89/Public/_git/OneDotNet?path=/LICENSE.LGPLv3-linking-exception.txt) for details.

### Version 1.2.1 (July 27, 2025)

- **Modern .NET support**: Multi-targeting for `netstandard2.0`, `netstandard2.1`, `net8.0`, and `net9.0`
- **Native AOT compatibility**: Full support for Native Ahead-of-Time compilation
- **NuGet package availability**: Official NuGet package with assembly name `Hjg.Pngcs`
- **Enhanced compression support**: Improved ZLib stream implementation
  - Native `ZLibStream` support for .NET 8.0+ targets
  - Unified compression stream architecture
- **Performance improvements**:
  - Enhanced string operations with ordinal comparison
  - Optimized dictionary lookups with `TryGetValue()` patterns
  - Eliminated unnecessary array allocations
- **Bug fixes**:
  - Fixed `PngChunkTIME.GetAsString()` format string issues
  - Improved stream disposal to prevent resource leaks
  - Fixed ThreadStatic field initialization problems
- **Breaking changes**:
  - Assembly renamed from `pngcs` to `Hjg.Pngcs`
  - Consolidated ZLib stream implementations (removed separate `ZlibInputStreamMs` and `ZlibOutputStreamMs` classes)
  - Updated to GPL-3.0+ licensing

## Version History on Google Code

Change logs for <http://code.google.com/p/pngcs/>.

### Version 1.1.4 (December 15, 2012)

- **Dual targeting support**: .NET 4.5 with CLR `DeflateStream` (with custom Zlib wrapper) and older versions with SharpZipLib (requires additional DLL)
  - New `Zlib` namespace/folder structure
  - Decoupled compression implementation from SharpZipLib/.NET CLR
  - Custom implementations of CRC32 and Adler checksums
- **Bug fixes**:
  - Fixed bad namespace in internal `PngInterlacer`
  - Replaced CRC test in `PngReader` with Adler32

### Version 1.1.3 (December 8, 2012)

- **Interlaced PNG support**: Full reading support for interlaced images
- **Enhanced sample storage**: Read/write operations now support both `INT` and `BYTE` for sample storage
  - `ImageLine` includes `sampleType` with dual buffers: `Scanline` (integer) and `ScanlineB` (byte)
  - New type-specific methods: `ReadRowInt`/`ReadRowByte`, `WriteRowInt`/`WriteRowByte`
- **Packed format support**: Bit depths 1-2-4 can be unpacked on-the-fly during read/write operations (see `PngReader.SetUnpackedMode()`)
- **Improved PngReader**:
  - `ReadRow` can skip rows efficiently (`getRow` is now deprecated)
  - More efficient decoding (only processes necessary data)
  - New batch operations: `ReadRows`/`WriteRows` returning `ImageLines` object
  - `PngReader.ReadSkippingAllRows` allows efficient pixel data skipping
- **Enhanced testing framework**:
  - Added `crctest` and `PngReaderTest` for improved internal testing
  - `PngSuiteTest` performs double-mirror comparison with original images (including interlaced)
- **API improvements**:
  - `ImageLineHelper.Pal2rgb` (renamed, corrected, supports alpha with TRNS)
  - Added `getMetadata().GetPalette()` and `getMetadata().CreateNewPalette()`
  - Fixed textual chunk issues (null bytes, empty texts) with comprehensive testing
- **Code organization**: Extensive polishing and reorganization of test/sample code into unified project

### Version 1.0.96 (October 21, 2012)

- **API enhancement**: `SetMaxXXX()` methods now accept zero for unlimited operation (e.g., `PngReader.setMaxTotalBytesRead(0)`)
- **New sample**: Added `SampleCustomChunk` demonstrating custom chunk registration for read/write operations
- **Bug fixes**:
  - Fixed `PNGChunkTIME` output format issues
  - Fixed `PngWriter.init()` not being called internally when row number not provided

### Version 1.0.95 (August 14, 2012)

- **Enhanced security**: Defensive limits and tuning for `PngReader`
  - New limits: `MaxTotalBytesRead` (200MB), `MaxBytesMetadata` (5MB), `SkipChunkMaxSize` (2MB)
  - Added `SkipChunkIds` for "fdAT" chunks
  - `PngChunkSkipped`: Truly skipped chunks (never loaded into memory)
  - All skipped chunks represented as `PngChunkSkipped` in `ChunksLists`
- **Code improvements**:
  - Removed `ParseChunkAndAddToList()`, added `ReadChunk()` method
  - Added 'offset' property to chunks (informational)
  - Updated `PngReader` offset to `long` type (supports >2GB files) with `MaxTotalBytesRead` validation

### Version 1.0.93 (June 23, 2012)

- **Non-sequential reading**: `PngReader.GetRow()` supports non-sequential access (useful for line skipping)
- **API update**: `PngReader.End()` no longer deprecated, now recommended
- **Bug fix**: Fixed `ImageLine.Pack()`/`ImageLine.Unpack()` buffer length requirements
- **New feature**: Added `PngWriter.ComputeCompressionRatio()`

### Version 1.0.92 (June 15, 2012)

- **License change**: Now released under Apache License
- **Standalone assembly**: Replaced external SharpZipLib with embedded `ICSharpCode.SharpZipLib.dll`
- **C# conventions**: Extensive refactoring to follow C# best practices
  - **Breaking changes**: Signature modifications (especially casing) break backward compatibility
  - Removed unnecessary LINQ imports and .NET 4.0 dependencies (now targets .NET 2.0)
- **Documentation**: Complete XML documentation for all classes and methods (`Pngcs.xml`)
- **Samples**: Reorganized and added new sample projects (e.g., `SampleCreateOrangeGradient`)
- **Documentation**: Added Doxygen documentation generation

### Version 1.0.91 (June 10, 2012)

- **Major reorganization**: Synchronized with Java port PngJ v0.91
  - Complete chunk implementation and full PNG Test Suite support
  - Public methods/properties now follow C# naming conventions

### Early Development

**September 22, 2011**: Feature parity achieved with current PngJ implementation. Testing, optimization, and validation pending.

**September 21, 2011**: Integrated SharpZipLib classes. Basic reading and writing functionality operational.

**January 2011**: Initial port from Java, assisted by j2cstranslator.sourceforge.net

---

## Credits

**Author**: Hernan J. Gonzalez
**Email**: <hgonzalez@gmail.com>
**Website**: <http://hjg.com.ar/>
