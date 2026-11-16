namespace BatteryTray.Ui;

/// <summary>
/// Nome que aparece para o usuario ("YRAWAH", identidade de 26/10/2026):
/// trocar aqui troca em todo lugar visivel. Exe, pasta de dados, autostart e
/// mutex tambem viraram "YRAWAH" (AppPaths e Autostart migram o "BatteryTray"
/// antigo); so os namespaces do codigo continuam BatteryTray.
///
/// Canal de teste (`-p:YRAWAHChannel=teste`, ver tools/release.ps1): outro
/// app de verdade -- Id, pasta, mutex e autostart proprios, entao roda ao lado
/// do main -- e diferente a olho: nome "YRAWAH Teste", violeta no lugar do
/// amarelo e um ponto violeta no icone da bandeja.
/// </summary>
public static class AppInfo
{
#if JOULE_TESTE
    public static readonly bool IsTest = true;
    public const string Name = "YRAWAH Teste";
    /// <summary>Pasta de dados, mutex, evento e valor do autostart.</summary>
    public const string Id = "YRAWAHTeste";
    private const string Channel = "teste";
#else
    public static readonly bool IsTest = false;
    public const string Name = "YRAWAH";
    public const string Id = "YRAWAH";
    private const string Channel = "main";
#endif

    /// <summary>
    /// De onde o atualizador baixa (Velopack). O main, do GitHub Releases do
    /// repositorio oficial (item 4, 2026-11-12); o Teste continua so neste PC, na
    /// pasta escrita pelo tools/release.ps1, que passa o caminho na compilacao
    /// (YRAWAHTestFeed). O --selftest do diag confere os dois.
    /// </summary>
#if JOULE_TESTE
    public static readonly string UpdateFeed = TestFeedOrEmpty(typeof(AppInfo).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "YRAWAHTestFeed")?.Value);
#else
    // YRAWAH: no updater. Empty = the app never checks or downloads anything from the network.
    public static readonly string UpdateFeed = "";
#endif

    /// <summary>O Teste so aceita uma pasta local terminada em releases\teste: nada de internet nem de rede. Vazio = sem atualizador.</summary>
    public static string TestFeedOrEmpty(string? feed) =>
        feed is { Length: > 2 } && feed[1] == ':' && Path.IsPathRooted(feed) && !feed.Contains("://")
        && feed.TrimEnd('\\').EndsWith(@"\releases\teste", StringComparison.OrdinalIgnoreCase) ? feed : "";

    /// <summary>
    /// Onde o "Mandar meu aparelho" abre o relato.
    /// shortcut: aponta para o repositorio privado de hoje (so o dono abre); troca para o publico no item 3 da fila.
    /// </summary>
    public const string Repository = "github.com/oookbaaa/YRAWAH";
    public const string NewIssueUrl = "https://" + Repository + "/issues/new";

    /// <summary>Versao do Fonts/Geist-SemiBold.ttf (tabela name); trocar junto com o arquivo.</summary>
    public const string GeistVersion = "1.900";

    /// <summary>
    /// Link de apoio (Stripe) da tela depois de atualizar. Vazio esconde o botao e a frase do cafe.
    /// So aceita https://buy.stripe.com/ (--selftest do diag).
    /// </summary>
    public const string SupportUrl = ""; // YRAWAH: no donation link

    /// <summary>E-mail "dev" do Dillan, público. Vazio esconde o caminho por e-mail.</summary>
    public const string ContactEmail = ""; // YRAWAH: the upstream author's inbox is not ours to fill

    /// <summary>YRAWAH is a renamed build of Joule (AGPL-3.0) by Dillan Cidral.</summary>
    public const string Upstream = "https://github.com/DillanCidral/Joule";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
}
