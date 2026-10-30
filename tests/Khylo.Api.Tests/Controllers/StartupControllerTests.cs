using System.Threading.Tasks;
using Khylo.Api.Controllers;
using Khylo.Api.Models.StartupDtos;
using Khylo.Controller.Configuration;
using Khylo.Controller.Library;
using Khylo.Database.Implementations.Entities;
using Khylo.Server.Implementations.Users;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Khylo.Api.Tests.Controllers;

public class StartupControllerTests
{
    private readonly StartupController _subject;
    private readonly Mock<IUserManager> _mockUserManager;
    private readonly Mock<IServerConfigurationManager> _mockConfig;

    public StartupControllerTests()
    {
        _mockUserManager = new Mock<IUserManager>();
        _mockConfig = new Mock<IServerConfigurationManager>();
        _subject = new StartupController(_mockConfig.Object, _mockUserManager.Object);
    }

    private static User CreateUser()
        => new User(
            "khylo",
            typeof(DefaultAuthenticationProvider).FullName!,
            typeof(DefaultPasswordResetProvider).FullName!);

    [Fact]
    public async Task UpdateStartupUser_WhenNoUserExists_ReturnsNotFound()
    {
        _mockUserManager.Setup(m => m.GetFirstUser()).Returns((User?)null);

        var result = await _subject.UpdateStartupUser(new StartupUserDto { Name = "admin", Password = "pw" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateStartupUser_WhenPasswordAlreadyConfigured_ReturnsForbidden()
    {
        var user = CreateUser();
        user.Password = "already-set-hash";
        _mockUserManager.Setup(m => m.GetFirstUser()).Returns(user);

        var result = await _subject.UpdateStartupUser(new StartupUserDto { Name = "attacker", Password = "new-pw" });

        // The startup wizard must never overwrite the password of an already-provisioned
        // account, even if IsStartupWizardCompleted has been cleared.
        Assert.IsType<ForbidResult>(result);
        _mockUserManager.Verify(m => m.ChangePassword(It.IsAny<System.Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStartupUser_WhenNoPasswordYet_SetsPassword()
    {
        var user = CreateUser();
        Assert.True(string.IsNullOrEmpty(user.Password));
        _mockUserManager.Setup(m => m.GetFirstUser()).Returns(user);

        var result = await _subject.UpdateStartupUser(new StartupUserDto { Name = "khylo", Password = "first-pw" });

        Assert.IsType<NoContentResult>(result);
        _mockUserManager.Verify(m => m.ChangePassword(user.Id, "first-pw"), Times.Once);
    }
}
