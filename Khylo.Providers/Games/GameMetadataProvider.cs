#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Microsoft.Extensions.Logging;

namespace Khylo.Providers.Games
{
    /// <summary>
    /// Local metadata provider for classic console ROM images.
    /// Performs title normalization, CRC32 hashing, and retro metadata enrichment.
    /// Excludes optical disc ISOs per Phase 1 scope directive.
    /// </summary>
    public partial class GameMetadataProvider : ILocalMetadataProvider<Game>, IHasItemChangeMonitor
    {
        private readonly ILogger<GameMetadataProvider> _logger;

        // Built-in curated metadata repository for classic console titles
        private static readonly Dictionary<string, RomMetadataEntry> _knownTitles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Super Mario Bros",
                    new RomMetadataEntry(
                        "Super Mario Bros.",
                        "Nintendo",
                        "Nintendo",
                        1985,
                        2,
                        ["Platformer", "Action"],
                        "Mario and Luigi embark on a quest through the Mushroom Kingdom to rescue Princess Toadstool from King Bowser.")
                },
                {
                    "Super Mario World",
                    new RomMetadataEntry(
                        "Super Mario World",
                        "Nintendo EAD",
                        "Nintendo",
                        1990,
                        2,
                        ["Platformer", "Adventure"],
                        "Mario, Luigi, and Yoshi explore Dinosaur Land to defeat Bowser and his Koopalings.")
                },
                {
                    "The Legend of Zelda",
                    new RomMetadataEntry(
                        "The Legend of Zelda",
                        "Nintendo",
                        "Nintendo",
                        1986,
                        1,
                        ["Action-Adventure", "Fantasy"],
                        "Link journeys across the land of Hyrule to collect the eight fragments of the Triforce of Wisdom and rescue Princess Zelda.")
                },
                {
                    "The Legend of Zelda - A Link to the Past",
                    new RomMetadataEntry(
                        "The Legend of Zelda: A Link to the Past",
                        "Nintendo",
                        "Nintendo",
                        1991,
                        1,
                        ["Action-Adventure", "RPG"],
                        "Link must travel between the Light World and the Dark World to defeat the evil wizard Agahnim and rescue the descendants of the Seven Sages.")
                },
                {
                    "Chrono Trigger",
                    new RomMetadataEntry(
                        "Chrono Trigger",
                        "Square",
                        "Square",
                        1995,
                        1,
                        ["RPG", "Sci-Fi"],
                        "Crono and his companions travel through prehistory, the Middle Ages, the present, and the post-apocalyptic future to prevent the destruction of the world by Lavos.")
                },
                {
                    "Sonic the Hedgehog",
                    new RomMetadataEntry(
                        "Sonic the Hedgehog",
                        "Sonic Team",
                        "Sega",
                        1991,
                        1,
                        ["Platformer", "Action"],
                        "Sonic the Hedgehog runs at supersonic speed through South Island to thwart Dr. Ivo Robotnik and collect the Chaos Emeralds.")
                },
                {
                    "Sonic the Hedgehog 2",
                    new RomMetadataEntry(
                        "Sonic the Hedgehog 2",
                        "Sega Technical Institute",
                        "Sega",
                        1992,
                        2,
                        ["Platformer", "Action"],
                        "Sonic and his partner Miles 'Tails' Prower team up to stop Dr. Robotnik from completing the Death Egg.")
                },
                {
                    "Pokemon Red",
                    new RomMetadataEntry(
                        "Pokémon Red Version",
                        "Game Freak",
                        "Nintendo",
                        1996,
                        2,
                        ["RPG", "Monster Taming"],
                        "Become the Pokémon Champion of the Kanto region and complete the Pokédex with all 151 Pokémon.")
                },
                {
                    "Pokemon Blue",
                    new RomMetadataEntry(
                        "Pokémon Blue Version",
                        "Game Freak",
                        "Nintendo",
                        1996,
                        2,
                        ["RPG", "Monster Taming"],
                        "Explore the Kanto region, battle Gym Leaders, defeat Team Rocket, and catch all wild Pokémon.")
                },
                {
                    "Pokemon Emerald",
                    new RomMetadataEntry(
                        "Pokémon Emerald Version",
                        "Game Freak",
                        "Nintendo",
                        2004,
                        2,
                        ["RPG", "Monster Taming"],
                        "Travel across the Hoenn region, stop Team Magma and Team Aqua from awakening Groudon and Kyogre, and tame Rayquaza.")
                },
                {
                    "Super Mario 64",
                    new RomMetadataEntry(
                        "Super Mario 64",
                        "Nintendo EAD",
                        "Nintendo",
                        1996,
                        1,
                        ["3D Platformer", "Action"],
                        "Mario explores Princess Peach's castle and jumps through magical paintings to recover 120 Power Stars stolen by Bowser.")
                },
                {
                    "The Legend of Zelda - Ocarina of Time",
                    new RomMetadataEntry(
                        "The Legend of Zelda: Ocarina of Time",
                        "Nintendo EAD",
                        "Nintendo",
                        1998,
                        1,
                        ["Action-Adventure", "Fantasy"],
                        "Link journeys through time using the mystical Ocarina of Time to stop Ganondorf from obtaining the Triforce.")
                },
                {
                    "Metroid",
                    new RomMetadataEntry(
                        "Metroid",
                        "Nintendo R&D1",
                        "Nintendo",
                        1986,
                        1,
                        ["Action-Adventure", "Sci-Fi"],
                        "Bounty hunter Samus Aran infiltrates the subterranean world of planet Zebes to retrieve parasitic Metroid organisms from Space Pirates.")
                },
                {
                    "Super Metroid",
                    new RomMetadataEntry(
                        "Super Metroid",
                        "Nintendo R&D1",
                        "Nintendo",
                        1994,
                        1,
                        ["Action-Adventure", "Sci-Fi"],
                        "Samus Aran returns to Zebes to rescue the stolen infant Metroid from the resurrected Space Pirate leader Ridley.")
                },
                {
                    "Castlevania - Symphony of the Night",
                    new RomMetadataEntry(
                        "Castlevania: Symphony of the Night",
                        "Konami",
                        "Konami",
                        1997,
                        1,
                        ["Action-Adventure", "RPG"],
                        "Alucard awakens to explore Dracula's resurrected castle and confront Richter Belmont under the control of Shaft.")
                }
            };

        public GameMetadataProvider(ILogger<GameMetadataProvider> logger)
        {
            _logger = logger;
        }

        public string Name => "ROM Metadata Provider";

        public Task<MetadataResult<Game>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(info.Path) || !File.Exists(info.Path))
            {
                return Task.FromResult(new MetadataResult<Game> { HasMetadata = false });
            }

            var rawFileName = Path.GetFileNameWithoutExtension(info.Path);
            var cleanTitle = CleanGameTitle(rawFileName);

            var game = new Game
            {
                Name = cleanTitle,
                Path = info.Path,
                IsInMixedFolder = true
            };

            // Compute CRC32 checksum
            try
            {
                game.RomChecksum = CalculateCrc32(info.Path);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not compute CRC32 for {Path}", info.Path);
            }

            // Attempt curated title matching
            if (_knownTitles.TryGetValue(cleanTitle, out var entry) ||
                _knownTitles.TryGetValue(NormalizeTitleKey(cleanTitle), out entry))
            {
                game.Name = entry.Title;
                game.Developer = entry.Developer;
                game.Publisher = entry.Publisher;
                game.ProductionYear = entry.Year;
                game.Players = entry.Players;
                game.Overview = entry.Overview;

                foreach (var genre in entry.Genres)
                {
                    game.AddGenre(genre);
                }

                if (!string.IsNullOrEmpty(entry.Publisher))
                {
                    game.AddStudio(entry.Publisher);
                }
            }

            var result = new MetadataResult<Game>
            {
                Item = game,
                HasMetadata = true
            };

            return Task.FromResult(result);
        }

        public bool HasChanged(BaseItem item, IDirectoryService directoryService)
        {
            if (item.IsFileProtocol)
            {
                var file = directoryService.GetFile(item.Path);
                return file is not null && item.HasChanged(file.LastWriteTimeUtc);
            }

            return false;
        }

        [GeneratedRegex(@"\s*(\([^\)]*\)|\[[^\]]*\])")]
        private static partial Regex TagRegex();

        private static string CleanGameTitle(string rawName)
        {
            var cleaned = TagRegex().Replace(rawName, string.Empty);
            return string.IsNullOrWhiteSpace(cleaned) ? rawName : cleaned.Trim();
        }

        private static string NormalizeTitleKey(string title)
        {
            return title.Replace(":", " -", StringComparison.Ordinal)
                        .Replace("'", string.Empty, StringComparison.Ordinal)
                        .Trim();
        }

        private static string CalculateCrc32(string filePath)
        {
            const uint polynomial = 0xEDB88320;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var cur = i;
                for (var j = 0; j < 8; j++)
                {
                    cur = (cur & 1) != 0 ? (cur >> 1) ^ polynomial : cur >> 1;
                }

                table[i] = cur;
            }

            uint crc = 0xFFFFFFFF;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
            {
                var buffer = new byte[65536];
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (var i = 0; i < bytesRead; i++)
                    {
                        var tableIndex = (byte)((crc & 0xFF) ^ buffer[i]);
                        crc = (crc >> 8) ^ table[tableIndex];
                    }
                }
            }

            return (~crc).ToString("X8", CultureInfo.InvariantCulture);
        }

        private sealed record RomMetadataEntry(
            string Title,
            string Developer,
            string Publisher,
            int Year,
            int Players,
            string[] Genres,
            string Overview);
    }
}
