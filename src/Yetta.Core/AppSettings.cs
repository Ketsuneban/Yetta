namespace Yetta.Core;

/// <summary>Only Yetta interface preferences; no Windows settings are changed.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Fluent";
    public bool ConfirmExternalLinks { get; set; } = true;

    public void Normalize()
    {
        if (Theme is not ("Fluent" or "Glass"))
            Theme = "Fluent";
    }
}
