using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal interface IInteractiveConsole
{
    bool CanPrompt { get; }

    bool Confirm(string prompt, bool defaultAnswer);

    string ReadSecret(string prompt);

    string ReadLine(string prompt);
}

internal sealed class SystemInteractiveConsole : IInteractiveConsole
{
    public bool CanPrompt => !Console.IsInputRedirected;

    public bool Confirm(string prompt, bool defaultAnswer)
    {
        string suffix = defaultAnswer ? " [Y/n]: " : " [y/N]: ";

        while (true)
        {
            Console.Write(prompt);
            Console.Write(suffix);

            string? response = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(response))
            {
                return defaultAnswer;
            }

            response = response.Trim();
            if (response.Equals("y", StringComparison.OrdinalIgnoreCase)
                || response.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (response.Equals("n", StringComparison.OrdinalIgnoreCase)
                || response.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("Please answer yes or no.");
        }
    }

    public string ReadSecret(string prompt)
    {
        Console.Write(prompt);

        StringBuilder builder = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length -= 1;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        return builder.ToString();
    }

    public string ReadLine(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine() ?? string.Empty;
    }
}
