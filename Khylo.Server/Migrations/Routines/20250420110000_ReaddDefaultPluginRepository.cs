using Khylo.Controller.Configuration;

namespace Khylo.Server.Migrations.Routines;

/// <summary>
/// Used to put another project's plugin catalogue back in the default plugin repositories. Khylo has no default plugin
/// repository now, so this does nothing; it stays so that servers that already ran it keep their migration history.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
[KhyloMigration("2025-04-20T11:00:00", nameof(ReaddDefaultPluginRepository), "5F86E7F6-D966-4C77-849D-7A7B40B68C4E", RunMigrationOnSetup = true)]
public class ReaddDefaultPluginRepository : IMigrationRoutine
#pragma warning restore CS0618 // Type or member is obsolete
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReaddDefaultPluginRepository"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public ReaddDefaultPluginRepository(IServerConfigurationManager serverConfigurationManager)
    {
    }

    /// <inheritdoc/>
    public void Perform()
    {
    }
}
