using Khylo.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Khylo.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the PlaylistPlaybackState entity.
/// </summary>
public class PlaylistPlaybackStateConfiguration : IEntityTypeConfiguration<PlaylistPlaybackState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PlaylistPlaybackState> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PlaylistId, e.UserId }).IsUnique();
        builder.HasIndex(e => e.LastActive);
        builder.Property(e => e.QueueStateJson).HasMaxLength(65536);
    }
}
