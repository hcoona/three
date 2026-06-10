namespace Hcoona.DocumentTranslatorCli;

internal sealed class TextTranslationServiceException : Exception
{
    public TextTranslationServiceException(string message)
        : base(message)
    {
    }

    public TextTranslationServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
