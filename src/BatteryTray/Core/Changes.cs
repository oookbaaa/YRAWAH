namespace BatteryTray.Core;

/// <summary>Uma novidade da tela "O que mudou" (2026-11-12). Glyph = traco 20x20, como os desenhos das telas.</summary>
public sealed record Change(string Version, string Glyph, string TitlePt, string TitleEn, string TextPt, string TextEn)
{
    public string Title => L.T(TitlePt, TitleEn);
    public string Text => L.T(TextPt, TextEn);
}

/// <summary>
/// Tela depois de cada atualizacao (mostra aprovada em 2026-11-12:
/// https://claude.ai/artifact/EpnC2HpjG4dvKsRHs8uBuS): resumo e apoio, depois
/// "O que mudou". Novidade nova entra no topo de All, com a versao do main.
/// </summary>
public static class Changes
{
    public static readonly Change[] All =
    {
        new("0.4.0", "M10 3.5v9M6.5 9 10 12.5 13.5 9M4 16h12",
            "Atualizações no app", "Updates in the app",
            "Quando sai uma versão nova, o YRAWAH avisa no painel e se atualiza com um clique.", "When a new version is out, YRAWAH tells you in the panel and updates with one click."),
        new("0.4.0", "M3 7h14v7H3zM6 10h1M9 10h1M12 10h1M6 12h8",
            "Atalho do seu jeito", "Your own shortcut",
            "Escolha a tecla que abre o painel em Configurações.", "Choose the key that opens the panel in Settings."),
        new("0.4.0", "M4 8h3l4-3v10l-4-3H4zM14 7.5a3.5 3.5 0 0 1 0 5",
            "Mixer de volume", "Volume mixer",
            "Volume geral e de cada app, direto no painel.", "Master and per-app volume, right in the panel."),
        new("0.4.0", "M4 13v-2a6 6 0 0 1 12 0v2M4 13h2.5v3.5H4zM13.5 13H16v3.5h-2.5z",
            "Mais aparelhos", "More devices",
            "Fone Logitech G PRO X 2 LIGHTSPEED e DualSense por Bluetooth.", "Logitech G PRO X 2 LIGHTSPEED headset and DualSense over Bluetooth."),
        new("0.4.0", "M10 3v6M6.2 5.5a6 6 0 1 0 7.6 0",
            "Desligado aparece", "Off devices show",
            "Mouse e fone desligados ficam apagados no painel, com o último valor e um som.", "Switched-off mice and headsets show dimmed in the panel, with their last value and a sound."),
    };

    /// <summary>Novidades mais novas que a versao de antes ("" = veio de antes da tela existir: todas).</summary>
    public static IReadOnlyList<Change> Since(string last) =>
        Version.TryParse(last, out var from)
            ? All.Where(c => Version.Parse(c.Version) > from).ToList()
            : All;

    /// <summary>
    /// Versao de onde veio, quando a tela deve abrir agora; null quando nao.
    /// Abre uma vez por versao nova, nunca na primeira instalacao (as boas-vindas ja explicam).
    /// </summary>
    public static string? UpdatedFrom(string lastVersion, string currentVersion, bool onboardingDone) =>
        lastVersion == currentVersion || !onboardingDone ? null : lastVersion;
}
