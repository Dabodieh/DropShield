namespace DropShield.Demo;

/// <summary>
/// Formats scenario output as fixed-width readable lines, terminal- and screenshot-friendly.
/// Never receives a token, cookie, or key value — callers pass only outcomes and non-secret ids.
/// </summary>
public sealed class DemoReport
{
    private const int LabelWidth = 34;

    public void Title(string text)
    {
        Console.WriteLine();
        Console.WriteLine(text);
        Console.WriteLine(new string('-', text.Length));
    }

    public void Section(string text)
    {
        Console.WriteLine();
        Console.WriteLine(text);
    }

    public void Line(string label, string value) =>
        Console.WriteLine($"   {label.PadRight(LabelWidth, '.')} {value}");

    public void Fail(string message)
    {
        Console.WriteLine();
        Console.Error.WriteLine($"FAILED: {message}");
    }
}
