# Release Notes

## Version 1.1.1

- Relicense: Re-licensed NuGet package to LGPL-3.0-or-later WITH LGPL-3.0-linking-exception.

## Version 1.0.2 (Initial Release)

### Features

- **Full IFileProvider Implementation**: Complete implementation of Microsoft.Extensions.FileProviders.IFileProvider interface
- **WebHDFS REST API Integration**: Native support for accessing HDFS through WebHDFS REST API
- **File Operations**: Read file content, get file information (size, timestamps, type), and browse directories
- **Change Detection**: Polling-based file system change monitoring with configurable intervals
- **Streaming Support**: Efficient streaming of large files from HDFS
- **Multi-Framework Support**: Comprehensive targeting for .NET Framework 4.6.2+, .NET Standard 2.0/2.1, .NET 8.0, and .NET 9.0
- **Cross-Platform Compatibility**: Works seamlessly on Windows, Linux, and macOS

### Core Components

- **WebHdfsFileProvider**: Main file provider implementation with configurable polling intervals
- **WebHdfsFileInfo**: File information implementation supporting both files and directories
- **WebHdfsDirectoryContents**: Directory enumeration with lazy loading
- **PollingFileChangeToken**: Change detection mechanism for file system monitoring
- **Error Handling**: Comprehensive error handling for network and HDFS-specific exceptions

### API Features

- **File Access**: Direct file access through WebHdfsFileInfo class
- **Directory Browsing**: Enumerate directory contents with file/directory type detection
- **Change Monitoring**: Watch file system changes with glob pattern support
- **Configuration**: Flexible NameNode URI configuration with HTTP/HTTPS support
- **Documentation**: Comprehensive XML documentation for all public APIs

### Performance & Security

- **Efficient HTTP Client Usage**: Optimized HTTP client usage with proper disposal patterns
- **Memory Management**: Efficient memory usage with streaming and IDisposable implementations
- **Connection Management**: Proper connection lifecycle management
- **Anonymous Access**: Secure anonymous access to HDFS clusters

### Compatibility

- **HDFS Version**: Compatible with Apache Hadoop 2.0+ with WebHDFS enabled
- **Network Requirements**: HTTP/HTTPS access to HDFS NameNode (default ports 9870/9871)
- **Dependencies**:
    - Microsoft.Extensions.FileProviders.Abstractions
    - System.Text.Json
    - System.Net.Http

### Supported Operations

| Operation          | Support | Implementation                                         |
| ------------------ | ------- | ------------------------------------------------------ |
| Read Files         | ✅      | Full streaming support with efficient memory usage     |
| Get File Info      | ✅      | Complete metadata including size, timestamps, and type |
| Browse Directories | ✅      | Lazy-loaded directory enumeration                      |
| Change Monitoring  | ✅      | Polling-based with configurable intervals              |
| File Watching      | ✅      | Glob pattern support for flexible monitoring           |

### Known Limitations

- **Read-Only Provider**: This version supports read operations only (write operations planned for future releases)
- **Authentication**: Currently supports anonymous access only (OAuth and Kerberos planned)
- **Polling-Based Changes**: Uses polling for change detection (may not be suitable for high-frequency monitoring scenarios)

### Technical Details

- **Default Polling Interval**: 5 seconds (configurable)
- **WebHDFS API Version**: Compatible with WebHDFS REST API v1
- **JSON Serialization**: Uses System.Text.Json for efficient parsing
- **Nullable Reference Types**: Full support on modern .NET versions (.NET 8.0+, .NET Standard 2.1)

### Future Roadmap

- OAuth authentication support
- Kerberos authentication support
- Write operations (create, update, delete files and directories)
- Glob pattern enhancements for file watching
- Performance optimizations for large-scale deployments
- Azure Data Lake Storage Gen2 compatibility
- Real-time change notifications (when supported by HDFS)

### Breaking Changes

None - this is the initial release.

### Migration Guide

This is the initial release, no migration required.

### Contributors

- Zhang Shuai - Initial implementation and design

---

For detailed usage examples and API documentation, see the [README.md](README.md) file.
