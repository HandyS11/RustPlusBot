namespace RustPlusBot.Features.Workspace.Localization;

/// <summary>The in-memory string catalog: culture -> (key -> value). English is the fallback.</summary>
internal sealed class LocalizationCatalog
{
    /// <summary>culture -> key -> value.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; init; }

    /// <summary>The built-in EN/FR catalog.</summary>
    public static LocalizationCatalog Default { get; } = new()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category.global.name"] = "RustPlusBot",
                ["channel.information.name"] = "information",
                ["channel.setup.name"] = "setup",
                ["channel.settings.name"] = "settings",
                ["channel.info.name"] = "info",
                ["information.title"] = "RustPlusBot",
                ["information.body"] = "Connect your Rust+ account in #setup, then pair a server in-game to begin.",
                ["information.servers"] = "Servers registered: {0}",
                ["setup.title"] = "Connect your Rust+ account",
                ["setup.body"] =
                    "Click **Connect account** below and paste your Rust+ FCM credentials. Then pair a server in-game and its channels appear automatically.",
                ["setup.connect.button"] = "Connect account",
                ["settings.title"] = "Settings",
                ["settings.body"] = "Configure the bot for this server.",
                ["settings.language.label"] = "Language",
                ["server.info.endpoint"] = "Endpoint: {0}:{1}",
                ["server.info.status.label"] = "Status",
                ["server.info.status.connecting"] = "Connecting…",
                ["server.info.status.connected"] = "Connected",
                ["server.info.status.unreachable"] = "Server unreachable",
                ["server.info.status.nocredentials"] = "No working credentials",
                ["server.info.player.label"] = "Active player",
                ["server.info.players.label"] = "Players online",
                ["server.info.swap.placeholder"] = "Switch active player",
                ["server.info.none"] = "—",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category.global.name"] = "RustPlusBot",
                ["channel.information.name"] = "informations",
                ["channel.setup.name"] = "configuration",
                ["channel.settings.name"] = "parametres",
                ["channel.info.name"] = "info",
                ["information.title"] = "RustPlusBot",
                ["information.body"] =
                    "Connectez votre compte Rust+ dans #configuration, puis appairez un serveur en jeu.",
                ["information.servers"] = "Serveurs enregistres : {0}",
                ["setup.title"] = "Connectez votre compte Rust+",
                ["setup.body"] =
                    "Cliquez sur **Connecter le compte** ci-dessous et collez vos identifiants FCM Rust+. Appairez ensuite un serveur en jeu et ses salons apparaitront automatiquement.",
                ["setup.connect.button"] = "Connecter le compte",
                ["settings.title"] = "Parametres",
                ["settings.body"] = "Configurez le bot pour ce serveur.",
                ["settings.language.label"] = "Langue",
                ["server.info.endpoint"] = "Adresse : {0}:{1}",
                ["server.info.status.label"] = "État",
                ["server.info.status.connecting"] = "Connexion…",
                ["server.info.status.connected"] = "Connecté",
                ["server.info.status.unreachable"] = "Serveur injoignable",
                ["server.info.status.nocredentials"] = "Aucun identifiant valide",
                ["server.info.player.label"] = "Joueur actif",
                ["server.info.players.label"] = "Joueurs en ligne",
                ["server.info.swap.placeholder"] = "Changer le joueur actif",
                ["server.info.none"] = "—",
            },
        },
    };
}
