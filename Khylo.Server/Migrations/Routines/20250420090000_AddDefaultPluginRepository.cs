using Khylo.Controller.Configuration;

namespace Khylo.Server.Migrations.Routines
{
    /// <summary>
    /// Used to put another project's plugin catalogue in the default plugin repositories. Khylo has no default plugin repository
    /// now, so this does nothing; it stays so that servers that already ran it keep their migration history.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
    [KhyloMigration("2025-04-20T09:00:00", nameof(AddDefaultPluginRepository), "EB58EBEE-9514-4B9B-8225-12E1A40020DF", RunMigrationOnSetup = true)]
    public class AddDefaultPluginRepository : IMigrationRoutine
#pragma warning restore CS0618 // Type or member is obsolete
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AddDefaultPluginRepository"/> class.
        /// </summary>
        /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        public AddDefaultPluginRepository(IServerConfigurationManager serverConfigurationManager)
        {
        }

        /// <inheritdoc/>
        public void Perform()
        {
        }
    }
}
