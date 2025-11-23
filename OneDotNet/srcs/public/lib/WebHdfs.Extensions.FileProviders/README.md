# WebHdfs.Extensions.FileProviders

[![NuGet](https://img.shields.io/nuget/v/WebHdfs.Extensions.FileProviders.svg)](https://www.nuget.org/packages/WebHdfs.Extensions.FileProviders/)

A comprehensive file provider implementation for [Apache Hadoop HDFS](https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/HdfsDesign.html) through the [WebHDFS REST API](https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/WebHDFS.html), fully compatible with the [Microsoft.Extensions.FileProviders](https://github.com/aspnet/FileSystem) abstraction.

## Problem Statement

Working with HDFS in .NET applications typically requires custom clients or complex setup procedures. Developers often need to:

1. Learn Hadoop-specific APIs and protocols
2. Handle low-level HTTP requests to WebHDFS endpoints
3. Implement custom file system abstractions
4. Deal with authentication and connection management
5. Write boilerplate code for common file operations

This creates a barrier for .NET developers who want to integrate with Hadoop ecosystems in cloud-native or hybrid environments where HDFS is used for big data storage.

## Solution

This library provides a **standard IFileProvider implementation** that seamlessly integrates HDFS with the .NET ecosystem. It abstracts away the complexity of WebHDFS protocol while providing:

1. **Native .NET Integration**: Works with ASP.NET Core, dependency injection, and other .NET frameworks out of the box
2. **Familiar API**: Uses the same IFileProvider interface that .NET developers already know
3. **WebHDFS Protocol**: Leverages HTTP/HTTPS for cross-platform compatibility and firewall-friendly communication
4. **Change Monitoring**: Implements polling-based file system watching for reactive applications
5. **Performance Optimized**: Efficient streaming, connection reuse, and minimal memory footprint

## Features

- **Full IFileProvider Implementation**: Seamless integration with ASP.NET Core and other .NET applications
- **WebHDFS REST API**: Access HDFS files and directories through HTTP/HTTPS without custom clients
- **File Operations**: Read file content, get file information, and browse directories with streaming support
- **Change Detection**: Monitor file system changes with configurable polling-based change tokens
- **Cross-Platform**: Works on Windows, Linux, and macOS with no native dependencies
- **Production Ready**: Thread-safe, efficient, and designed for high-throughput scenarios
- **Multiple Framework Support**: .NET Framework 4.6.2+, .NET Standard 2.0+, .NET 8.0+, and .NET 9.0

## Installation

Install the package via NuGet:

```bash
dotnet add package WebHdfs.Extensions.FileProviders
```

Or via Package Manager Console:

```powershell
Install-Package WebHdfs.Extensions.FileProviders
```

## Usage

### Basic File Provider Usage

```csharp
using WebHdfs.Extensions.FileProviders;

// Create a file provider pointing to your HDFS NameNode
var nameNodeUri = new Uri("http://namenode:9870");
var fileProvider = new WebHdfsFileProvider(nameNodeUri);

// Get file info
var fileInfo = fileProvider.GetFileInfo("/path/to/your/file.txt");
if (fileInfo.Exists)
{
    Console.WriteLine($"File size: {fileInfo.Length} bytes");
    Console.WriteLine($"Last modified: {fileInfo.LastModified}");

    // Read file content
    using var stream = fileInfo.CreateReadStream();
    using var reader = new StreamReader(stream);
    var content = reader.ReadToEnd();
    Console.WriteLine($"Content: {content}");
}

// Browse directory
var directoryContents = fileProvider.GetDirectoryContents("/path/to/directory");
foreach (var item in directoryContents)
{
    Console.WriteLine($"{(item.IsDirectory ? "DIR" : "FILE")}: {item.Name}");
}
```

### Direct File Access

```csharp
using WebHdfs.Extensions.FileProviders;

var nameNodeUri = new Uri("http://namenode:9870");
var fileInfo = new WebHdfsFileInfo(nameNodeUri, "/path/to/file.txt");

if (fileInfo.Exists)
{
    Console.WriteLine($"File exists: {fileInfo.Exists}");
    Console.WriteLine($"File size: {fileInfo.Length} bytes");
    Console.WriteLine($"Is directory: {fileInfo.IsDirectory}");
    Console.WriteLine($"Last modified: {fileInfo.LastModified}");

    // Read file content
    using var stream = fileInfo.CreateReadStream();
    using var reader = new StreamReader(stream);
    var content = reader.ReadToEnd();
    Console.WriteLine($"Content: {content}");
}
```

### Change Monitoring

```csharp
using WebHdfs.Extensions.FileProviders;

var nameNodeUri = new Uri("http://namenode:9870");
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromSeconds(10)); // Poll every 10 seconds

// Monitor file changes
var changeToken = fileProvider.Watch("/path/to/watch/**");
changeToken.RegisterChangeCallback(_ =>
{
    Console.WriteLine("File system changed!");
}, null);
```

### ASP.NET Core Integration

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register HDFS file provider
        services.AddSingleton<IFileProvider>(serviceProvider =>
        {
            var nameNodeUri = new Uri("http://namenode:9870");
            return new WebHdfsFileProvider(nameNodeUri);
        });

        // Or register as a named file provider
        services.Configure<FileProviderOptions>(options =>
        {
            var nameNodeUri = new Uri("http://namenode:9870");
            options.FileProviders.Add(new WebHdfsFileProvider(nameNodeUri));
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Use HDFS for static files (read-only)
        var nameNodeUri = new Uri("http://namenode:9870");
        var hdfsProvider = new WebHdfsFileProvider(nameNodeUri);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = hdfsProvider,
            RequestPath = "/hdfs-content"
        });
    }
}
```

## How It Works

The WebHdfsFileProvider leverages the WebHDFS REST API to provide seamless access to HDFS:

1. **HTTP/HTTPS Communication**: All operations use standard HTTP requests to the HDFS NameNode
2. **JSON Responses**: Parses WebHDFS JSON responses for file metadata and directory listings
3. **Streaming Support**: Efficient streaming for large file reads without loading entire files into memory
4. **Connection Reuse**: Uses HttpClient connection pooling for optimal performance
5. **Polling-based Monitoring**: Implements change detection through periodic metadata checks

### Request Flow

```csharp
// Simplified version of the internal process
GET /webhdfs/v1/path/to/file?op=GETFILESTATUS
→ Returns: {"FileStatus": {"length": 1024, "modificationTime": 1234567890, ...}}

GET /webhdfs/v1/path/to/file?op=OPEN
→ Returns: File content stream

GET /webhdfs/v1/path/to/directory?op=LISTSTATUS
→ Returns: {"FileStatuses": {"FileStatus": [...]}}
```

## API Reference

### WebHdfsFileProvider Class

#### Constructors

**`WebHdfsFileProvider(Uri nameNodeUri)`**

Creates a new instance with default polling interval (5 seconds).

**Parameters:**

- `nameNodeUri`: The URI of the HDFS NameNode (e.g., `http://namenode:9870`)

**`WebHdfsFileProvider(Uri nameNodeUri, TimeSpan pollingInterval)`**

Creates a new instance with custom polling interval for change detection.

**Parameters:**

- `nameNodeUri`: The URI of the HDFS NameNode
- `pollingInterval`: How often to check for file system changes

#### Provider Methods

**`IFileInfo GetFileInfo(string subpath)`**

Gets file information for the specified path.

**Parameters:**

- `subpath`: The relative path to the file in HDFS

**Returns:** `IFileInfo` instance with file metadata

**`IDirectoryContents GetDirectoryContents(string subpath)`**

Gets the contents of a directory.

**Parameters:**

- `subpath`: The relative path to the directory in HDFS

**Returns:** `IDirectoryContents` containing directory items

**`IChangeToken Watch(string filter)`**

Monitors the specified path pattern for changes.

**Parameters:**

- `filter`: The path pattern to monitor (supports wildcards)

**Returns:** `IChangeToken` for change notifications

### WebHdfsFileInfo Class

#### File Info Constructors

**`WebHdfsFileInfo(Uri nameNodeUri, string path)`**

Creates a direct file info instance for the specified HDFS path.

**Parameters:**

- `nameNodeUri`: The URI of the HDFS NameNode
- `path`: The absolute path to the file in HDFS

#### Properties

- **`bool Exists`**: Whether the file or directory exists
- **`long Length`**: File size in bytes (0 for directories)
- **`string Name`**: File or directory name
- **`DateTimeOffset LastModified`**: Last modification timestamp
- **`bool IsDirectory`**: Whether this is a directory
- **`string PhysicalPath`**: Returns null (not applicable for HDFS)

#### File Info Methods

**`Stream CreateReadStream()`**

Creates a read-only stream for the file content.

**Returns:** `Stream` for reading file data

**Throws:** `InvalidOperationException` if the file doesn't exist or is a directory

## Configuration

### NameNode URI

The NameNode URI should include the protocol, hostname, and port:

```csharp
// HTTP (default HDFS WebHDFS port)
var nameNodeUri = new Uri("http://namenode:9870");

// HTTPS (secure WebHDFS port)
var nameNodeUri = new Uri("https://namenode:9871");

// Custom port configuration
var nameNodeUri = new Uri("http://hdfs-cluster:8080");
```

### Polling Interval

Configure the polling interval for change detection based on your requirements:

```csharp
// Default polling interval (5 seconds)
var fileProvider = new WebHdfsFileProvider(nameNodeUri);

// Fast polling for real-time applications (1 second)
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromSeconds(1));

// Slow polling for batch processing (30 seconds)
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromSeconds(30));

// Disable polling (for read-only scenarios)
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromMilliseconds(-1));
```

### HTTP Client Configuration

For advanced scenarios, you can configure the underlying HttpClient:

```csharp
// Configure with custom HttpClient
var httpClient = new HttpClient()
{
    Timeout = TimeSpan.FromSeconds(30)
};
httpClient.DefaultRequestHeaders.Add("User-Agent", "MyApp/1.0");

var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromSeconds(5), httpClient);
```

## Performance Considerations

- **Streaming**: The library uses efficient streaming for file reads, minimizing memory usage for large files
- **Connection Pooling**: Leverages HttpClient connection pooling for optimal network performance
- **Lazy Loading**: Directory contents and file information are loaded on-demand
- **Caching**: Change tokens use intelligent caching to reduce unnecessary WebHDFS requests
- **Thread Safety**: All operations are thread-safe and can be called concurrently

## Compatibility

This library supports the following target frameworks:

- .NET Standard 2.0 (for broad compatibility)
- .NET Standard 2.1
- .NET Framework 4.6.2+
- .NET 8.0
- .NET 9.0

It's compatible with:

- ASP.NET Core 2.0+
- .NET Framework applications using Microsoft.Extensions.FileProviders
- Any application using the Microsoft.Extensions.FileProviders package
- Azure Functions, AWS Lambda, and other serverless environments
- Docker containers and Kubernetes deployments

## Thread Safety

All methods in this library are thread-safe and can be called concurrently from multiple threads. The internal HTTP client and caching mechanisms use appropriate synchronization to ensure consistency.

## Requirements

- **Target Frameworks**: .NET Framework 4.6.2+, .NET Standard 2.0+, .NET 8.0+, .NET 9.0
- **HDFS Version**: Compatible with Apache Hadoop 2.0+ with WebHDFS enabled
- **Network Access**: HTTP/HTTPS access to HDFS NameNode WebHDFS port (default: 9870)
- **Permissions**: Read access to HDFS paths (write operations not supported)

## Dependencies

- Microsoft.Extensions.FileProviders.Abstractions
- System.Text.Json (for parsing WebHDFS JSON responses)
- System.Net.Http (for WebHDFS REST API communication)

## Supported Operations

| Operation | Supported | WebHDFS API | Notes |
|-----------|-----------|-------------|-------|
| Read Files | ✅ | `OPEN` | Full streaming support, efficient for large files |
| Get File Info | ✅ | `GETFILESTATUS` | Size, timestamps, permissions, type |
| Browse Directories | ✅ | `LISTSTATUS` | Recursive directory listing with metadata |
| Change Monitoring | ✅ | `GETFILESTATUS` | Polling-based with configurable intervals |
| Write Files | ❌ | N/A | Read-only provider by design |
| Create Directories | ❌ | N/A | Read-only provider by design |
| Delete Files | ❌ | N/A | Read-only provider by design |
| Move/Rename | ❌ | N/A | Read-only provider by design |

## Troubleshooting

### Common Issues

#### Connection Refused

```text
System.Net.Http.HttpRequestException: Connection refused
```

**Solution:**

- Verify HDFS NameNode is running and WebHDFS is enabled
- Check the NameNode URI (default port is 9870 for HTTP, 9871 for HTTPS)
- Ensure network connectivity and firewall rules allow access

#### File Not Found

```text
WebHdfsFileInfo.Exists returns false for existing files
```

**Solution:**

- Verify the file path is absolute (starts with `/`)
- Check HDFS permissions (user must have read access)
- Ensure the path exists in HDFS using `hdfs dfs -ls /path/to/file`

#### Slow Performance

**Solutions:**

- Increase polling interval for change monitoring
- Use connection pooling by reusing the same WebHdfsFileProvider instance
- Consider caching file information for frequently accessed files
- Check network latency between client and HDFS cluster

#### Authentication Errors

```text
HTTP 401 Unauthorized
```

**Solution:**

- This library currently supports anonymous access only
- Ensure HDFS cluster allows anonymous read access
- For authenticated access, consider extending the library or using a proxy

### Performance Tuning

#### Optimize Polling Interval

```csharp
// For real-time applications
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromSeconds(1));

// For batch processing
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromMinutes(5));

// Disable polling for read-only scenarios
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromMilliseconds(-1));
```

#### Connection Management

```csharp
// Reuse provider instances
private static readonly WebHdfsFileProvider SharedProvider =
    new WebHdfsFileProvider(new Uri("http://namenode:9870"));

// Configure HttpClient timeout
var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var fileProvider = new WebHdfsFileProvider(nameNodeUri, TimeSpan.FromSeconds(5), httpClient);
```

#### Memory Management

```csharp
// Use streaming for large files
using var stream = fileInfo.CreateReadStream();
using var bufferedStream = new BufferedStream(stream, 8192);

// Process in chunks instead of loading entire file
var buffer = new byte[4096];
int bytesRead;
while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
{
    // Process chunk
}
```

## Known Limitations

- **Read-Only Operations**: This provider only supports read operations. Write, delete, and create operations are not implemented by design
- **Anonymous Authentication**: Currently supports anonymous access only. For authenticated scenarios, consider using a proxy or extending the library
- **Polling-Based Monitoring**: Uses polling for change detection, which may not be suitable for high-frequency monitoring or real-time scenarios
- **No Transaction Support**: Operations are not transactional; partial reads may occur if files are modified during access
- **Limited Error Handling**: WebHDFS error responses are mapped to basic .NET exceptions; detailed HDFS error information may be lost

## Alternatives

If this library doesn't meet your requirements, consider these alternatives:

1. **Microsoft.Hadoop.Client**: Official Microsoft Hadoop client (deprecated, but may still work for some scenarios)
2. **HDInsight .NET SDK**: Azure HDInsight specific client with more features
3. **Custom WebHDFS Implementation**: Build your own WebHDFS client for specific authentication or feature requirements
4. **Hadoop Filesystem Bridge**: Use JNI or process invocation to call native HDFS commands
5. **Apache Knox**: Use Knox gateway for authentication and proxying to WebHDFS

## Related Projects

- [Microsoft.Extensions.FileProviders](https://github.com/aspnet/FileSystem) - The base file provider abstractions
- [Apache Hadoop WebHDFS](https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/WebHDFS.html) - Official WebHDFS documentation
- [Azure Data Lake Storage](https://docs.microsoft.com/en-us/azure/storage/blobs/data-lake-storage-introduction) - Microsoft's cloud-native HDFS alternative

## References

- [WebHDFS REST API Documentation](https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/WebHDFS.html)
- [HDFS Architecture Guide](https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/HdfsDesign.html)
- [ASP.NET Core File Providers](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/file-providers)
- [Microsoft.Extensions.FileProviders Source Code](https://github.com/dotnet/aspnetcore/tree/main/src/FileProviders)

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See the [LICENSE](https://dev.azure.com/zhangshuai89/Public/_git/OneDotNet?path=/LICENSE.LGPLv3-linking-exception.txt) file for details.

## Support

For questions, issues, or contributions, please visit the [GitHub repository](https://github.com/zhangshuai89/OneDotNet).
