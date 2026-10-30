using System;
using System.IO;
using System.Text.RegularExpressions;
using Khylo.Controller;
using Khylo.Controller.Entities;
using Khylo.Controller.Resolvers;
using Khylo.Model.IO;
using Khylo.Naming.Audio;
using Khylo.Naming.Common;

namespace Khylo.Server.Core.Library
{
    /// <summary>
    /// Provides the core resolver ignore rules.
    /// </summary>
    public class CoreResolutionIgnoreRule : IResolverIgnoreRule
    {
        // A folder whose name has "extras" as a word: "DVD Extras", "Vol. 5 Extras", "Bonus Extras"
        private static readonly Regex _bonusFolder = new(@"\bextras\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly NamingOptions _namingOptions;
        private readonly IServerApplicationPaths _serverApplicationPaths;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoreResolutionIgnoreRule"/> class.
        /// </summary>
        /// <param name="namingOptions">The naming options.</param>
        /// <param name="serverApplicationPaths">The server application paths.</param>
        public CoreResolutionIgnoreRule(NamingOptions namingOptions, IServerApplicationPaths serverApplicationPaths)
        {
            _namingOptions = namingOptions;
            _serverApplicationPaths = serverApplicationPaths;
        }

        /// <inheritdoc />
        public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem? parent)
        {
            // Don't ignore application folders
            if (fileInfo.FullName.Contains(_serverApplicationPaths.RootFolderPath, StringComparison.InvariantCulture))
            {
                return false;
            }

            if (IgnorePatterns.ShouldIgnore(fileInfo.FullName))
            {
                return true;
            }

            // Don't ignore top level folders
            if (fileInfo.IsDirectory
                && (parent is AggregateFolder || (parent?.IsTopParent ?? false)))
            {
                return false;
            }

            if (parent is null)
            {
                return false;
            }

            if (fileInfo.IsDirectory)
            {
                // Bonus-material folders inside a show or movie ("DVD Extras", "Vol. 5 Extras", ...) are
                // not episodes. Library-level folders were let through above, so a show named "Extras"
                // is unaffected.
                if (parent is not UserRootFolder && _bonusFolder.IsMatch(fileInfo.Name))
                {
                    return true;
                }

                // Ignore extras for unsupported types
                return _namingOptions.AllExtrasTypesFolderNames.ContainsKey(fileInfo.Name)
                    && parent is not UserRootFolder;
            }

            // Don't resolve theme songs
            return Path.GetFileNameWithoutExtension(fileInfo.Name.AsSpan()).Equals(BaseItem.ThemeSongFileName, StringComparison.Ordinal)
                && AudioFileParser.IsAudioFile(fileInfo.Name, _namingOptions);
        }
    }
}
