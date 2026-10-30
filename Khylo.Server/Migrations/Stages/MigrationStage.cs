using System.Collections.ObjectModel;

namespace Khylo.Server.Migrations.Stages;

/// <summary>
/// Defines a Stage that can be Invoked and Handled at different times from the code.
/// </summary>
internal class MigrationStage : Collection<CodeMigration>
{
    public MigrationStage(KhyloMigrationStageTypes stage)
    {
        Stage = stage;
    }

    public KhyloMigrationStageTypes Stage { get; }
}
