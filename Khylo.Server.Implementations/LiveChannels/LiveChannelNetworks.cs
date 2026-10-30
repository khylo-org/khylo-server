using System.Collections.Generic;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// TV networks and streaming services that get their own channel when a library has their shows.
/// Series metadata lists networks and production companies together, so only these names count as
/// networks. Each entry lists the names metadata providers use for the same network.
/// The position in this list fixes the channel number (200 + index), so add new networks at the end.
/// </summary>
internal static class LiveChannelNetworks
{
    /// <summary>
    /// Gets the known networks: display name, every name it appears under, and whether it airs animation.
    /// </summary>
    public static IReadOnlyList<(string Name, string[] Aliases, bool AirsAnimation)> All { get; } =
    [
        // US broadcast
        ("ABC", ["ABC", "ABC (US)", "American Broadcasting Company"], false),
        ("CBS", ["CBS"], false),
        ("NBC", ["NBC"], false),
        ("FOX", ["FOX", "Fox", "FOX (US)", "Fox Broadcasting Company"], false),
        ("The CW", ["The CW", "CW"], false),
        ("PBS", ["PBS"], false),
        ("The WB", ["The WB", "WB"], false),
        ("UPN", ["UPN"], false),

        // US premium cable
        ("HBO", ["HBO"], false),
        ("Showtime", ["Showtime", "SHOWTIME"], false),
        ("Starz", ["Starz", "STARZ"], false),
        ("Cinemax", ["Cinemax"], false),
        ("MGM+", ["MGM+", "Epix", "EPIX"], false),

        // US cable
        ("AMC", ["AMC"], false),
        ("FX", ["FX", "FX Network", "FX on Hulu"], false),
        ("FXX", ["FXX"], false),
        ("USA Network", ["USA Network", "USA"], false),
        ("TNT", ["TNT"], false),
        ("TBS", ["TBS"], false),
        ("truTV", ["truTV", "TruTV"], false),
        ("Syfy", ["Syfy", "SYFY", "Sci Fi Channel", "SciFi"], false),
        ("Comedy Central", ["Comedy Central"], true),
        ("Paramount Network", ["Paramount Network", "Spike", "Spike TV"], false),
        ("A&E", ["A&E"], false),
        ("Bravo", ["Bravo"], false),
        ("E!", ["E!"], false),
        ("Freeform", ["Freeform", "ABC Family"], false),
        ("Lifetime", ["Lifetime"], false),
        ("Hallmark Channel", ["Hallmark Channel", "Hallmark Movies & Mysteries"], false),
        ("IFC", ["IFC"], false),
        ("BET", ["BET"], false),
        ("OWN", ["OWN", "Oprah Winfrey Network"], false),
        ("CMT", ["CMT"], false),
        ("MTV", ["MTV"], false),
        ("VH1", ["VH1"], false),
        ("Oxygen", ["Oxygen"], false),
        ("TV Land", ["TV Land"], false),
        ("WGN America", ["WGN America"], false),
        ("Sundance TV", ["Sundance TV", "SundanceTV"], false),
        ("BBC America", ["BBC America"], false),

        // US factual and lifestyle
        ("Discovery", ["Discovery", "Discovery Channel"], false),
        ("History", ["History", "History Channel", "The History Channel"], false),
        ("National Geographic", ["National Geographic", "National Geographic Channel", "Nat Geo", "Nat Geo Wild"], false),
        ("Animal Planet", ["Animal Planet"], false),
        ("Science Channel", ["Science Channel", "Science"], false),
        ("Investigation Discovery", ["Investigation Discovery", "ID"], false),
        ("TLC", ["TLC"], false),
        ("HGTV", ["HGTV"], false),
        ("Food Network", ["Food Network"], false),
        ("Travel Channel", ["Travel Channel"], false),
        ("Smithsonian Channel", ["Smithsonian Channel"], false),
        ("ESPN", ["ESPN", "ESPN+"], false),

        // Kids and animation
        ("Cartoon Network", ["Cartoon Network"], true),
        ("Adult Swim", ["Adult Swim", "[adult swim]"], true),
        ("Nickelodeon", ["Nickelodeon"], true),
        ("Nick Jr.", ["Nick Jr.", "Nick Jr"], true),
        ("Nicktoons", ["Nicktoons"], true),
        ("Disney Channel", ["Disney Channel"], true),
        ("Disney Junior", ["Disney Junior"], true),
        ("Disney XD", ["Disney XD"], true),
        ("Boomerang", ["Boomerang"], true),
        ("PBS Kids", ["PBS Kids", "PBS KIDS"], true),

        // Streaming
        ("Netflix", ["Netflix"], true),
        ("Prime Video", ["Prime Video", "Amazon Prime Video", "Amazon", "Amazon Studios"], true),
        ("Hulu", ["Hulu"], true),
        ("Disney+", ["Disney+", "Disney Plus"], true),
        ("Max", ["Max", "HBO Max"], true),
        ("Apple TV+", ["Apple TV+", "Apple TV"], true),
        ("Paramount+", ["Paramount+", "Paramount+ with Showtime", "CBS All Access"], true),
        ("Peacock", ["Peacock"], true),
        ("AMC+", ["AMC+"], false),
        ("Shudder", ["Shudder"], false),
        ("Crunchyroll", ["Crunchyroll"], true),
        ("Funimation", ["Funimation"], true),
        ("YouTube Premium", ["YouTube Premium", "YouTube Red", "YouTube"], false),
        ("Freevee", ["Freevee", "IMDb TV"], false),
        ("BritBox", ["BritBox"], false),
        ("Acorn TV", ["Acorn TV"], false),
        ("Tubi", ["Tubi"], false),
        ("The Roku Channel", ["The Roku Channel", "Roku Channel"], false),

        // UK and Ireland
        ("BBC One", ["BBC One", "BBC 1"], false),
        ("BBC Two", ["BBC Two", "BBC 2"], false),
        ("BBC Three", ["BBC Three", "BBC 3"], true),
        ("BBC Four", ["BBC Four", "BBC 4"], false),
        ("ITV", ["ITV", "ITV1", "ITVX"], false),
        ("Channel 4", ["Channel 4"], false),
        ("E4", ["E4"], false),
        ("Channel 5", ["Channel 5"], false),
        ("Sky Atlantic", ["Sky Atlantic"], false),
        ("Sky One", ["Sky One", "Sky 1", "Sky Max"], false),
        ("Dave", ["Dave"], false),
        ("RTÉ", ["RTÉ One", "RTE One", "RTÉ"], false),

        // Canada
        ("CBC", ["CBC", "CBC Television"], false),
        ("CTV", ["CTV"], false),
        ("Global", ["Global", "Global TV"], false),
        ("Crave", ["Crave", "CraveTV"], false),
        ("Showcase", ["Showcase"], false),
        ("Teletoon", ["Teletoon"], true),
        ("YTV", ["YTV"], true),

        // Australia and New Zealand
        ("ABC (AU)", ["ABC (AU)", "ABC TV", "ABC Kids"], true),
        ("Network 10", ["Network 10", "Network Ten", "10"], false),
        ("Nine Network", ["Nine Network", "Channel 9"], false),
        ("Seven Network", ["Seven Network", "Channel 7"], false),
        ("SBS", ["SBS"], false),
        ("Stan", ["Stan"], false),

        // Rest of the world
        ("TF1", ["TF1"], false),
        ("Canal+", ["Canal+"], false),
        ("ZDF", ["ZDF"], false),
        ("Das Erste", ["Das Erste", "ARD"], false),
        ("RAI", ["Rai 1", "RAI"], false),
        ("Antena 3", ["Antena 3"], false),
        ("SVT", ["SVT1", "SVT"], false),
        ("NRK", ["NRK1", "NRK"], false),
        ("DR", ["DR1", "DR"], false),
        ("TV Tokyo", ["TV Tokyo"], true),
        ("Fuji TV", ["Fuji TV"], true),
        ("Tokyo MX", ["Tokyo MX"], true),
        ("NHK", ["NHK", "NHK G"], true),
        ("tvN", ["tvN"], false),
        ("JTBC", ["JTBC"], false),
        ("KBS2", ["KBS2", "KBS"], false),
        ("MBC", ["MBC"], false),
        ("SBS (KR)", ["SBS (KR)"], false)
    ];
}
