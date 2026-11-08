using System.Collections.Generic;
using System.Text.Json;
using Khylo.Data.Enums;
using Khylo.Model.Session;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Devices;

public class ClientCapabilitiesPersistenceTests
{
    [Fact]
    public void Capabilities_SurviveBeingWrittenToDiskAndReadBack()
    {
        var saved = new Dictionary<string, ClientCapabilities>
        {
            ["tv"] = new()
            {
                PlayableMediaTypes = [MediaType.Video, MediaType.Audio],
                SupportedCommands = [GeneralCommandType.SendString, GeneralCommandType.DisplayContent],
                SupportsMediaControl = true
            }
        };

        var back = JsonSerializer.Deserialize<Dictionary<string, ClientCapabilities>>(JsonSerializer.Serialize(saved));

        Assert.NotNull(back);
        Assert.True(back["tv"].SupportsMediaControl);
        Assert.Equal([MediaType.Video, MediaType.Audio], back["tv"].PlayableMediaTypes);
        Assert.Equal([GeneralCommandType.SendString, GeneralCommandType.DisplayContent], back["tv"].SupportedCommands);
    }
}
