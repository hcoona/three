namespace Hcoona.DocumentTranslatorCli;

internal delegate ValueTask OutputWriter(
    string outputPath,
    BinaryData content,
    bool overwrite,
    CancellationToken cancellationToken);
