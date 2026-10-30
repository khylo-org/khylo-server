#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Library;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Configuration;
using Khylo.Model.Invitations;
using Khylo.Model.Users;
using Khylo.Server.Implementations.Invitations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Invitations
{
    public sealed class KhyloInvitationManagerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Mock<IUserManager> _userManagerMock;
        private readonly Mock<IApplicationPaths> _appPathsMock;

        public KhyloInvitationManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "KhyloTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _userManagerMock = new Mock<IUserManager>();
            _appPathsMock = new Mock<IApplicationPaths>();
            _appPathsMock.Setup(a => a.ConfigurationDirectoryPath).Returns(_tempDir);
        }

        [Fact]
        public void CreateInvitation_ValidRequest_GeneratesKhyloTokenAndStoresMetadata()
        {
            using var manager = new InvitationManager(
                NullLogger<InvitationManager>.Instance,
                _userManagerMock.Object,
                _appPathsMock.Object);

            var folderId = Guid.NewGuid();
            var request = new CreateInvitationRequest
            {
                LifetimeHours = 24,
                MaxUses = 2,
                EnabledFolders = new[] { folderId }
            };

            var adminId = Guid.NewGuid();
            var result = manager.CreateInvitation(adminId, request);

            Assert.NotNull(result);
            Assert.StartsWith("khylo-", result.Code, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(adminId, result.CreatedByUserId);
            Assert.Equal(2, result.MaxUses);
            Assert.Equal(0, result.UseCount);
            Assert.True(result.IsActive);
            Assert.True(result.IsValid);
            Assert.Contains(folderId, result.EnabledFolders);

            // Fetch by code
            var fetched = manager.GetInvitationByCode(result.Code);
            Assert.NotNull(fetched);
            Assert.Equal(result.Id, fetched.Id);

            // Verify list
            var all = manager.GetInvitations();
            Assert.Single(all);
        }

        [Fact]
        public async Task RedeemInvitation_ValidCode_CreatesUserAndConfiguresPolicy()
        {
            using var manager = new InvitationManager(
                NullLogger<InvitationManager>.Instance,
                _userManagerMock.Object,
                _appPathsMock.Object);

            var folderId = Guid.NewGuid();
            var invite = manager.CreateInvitation(Guid.NewGuid(), new CreateInvitationRequest
            {
                LifetimeHours = 12,
                MaxUses = 1,
                EnabledFolders = new[] { folderId }
            });

            var createdUser = new User("tester", "DefaultAuth", "DefaultReset");

            _userManagerMock.Setup(u => u.CreateUserAsync("tester")).ReturnsAsync(createdUser);
            _userManagerMock.Setup(u => u.ChangePassword(createdUser.Id, "Password123!")).Returns(Task.CompletedTask);
            _userManagerMock.Setup(u => u.UpdatePolicyAsync(createdUser.Id, It.IsAny<UserPolicy>())).Returns(Task.CompletedTask);
            _userManagerMock.Setup(u => u.GetUserDto(createdUser, It.IsAny<string>())).Returns(new Khylo.Model.Dto.UserDto { Policy = new UserPolicy { AuthenticationProviderId = "default", PasswordResetProviderId = "default" } });

            var redeemedUserId = await manager.RedeemInvitationAsync(invite.Code, "tester", "Password123!");

            Assert.Equal(createdUser.Id, redeemedUserId);

            // Verify password was set
            _userManagerMock.Verify(u => u.ChangePassword(createdUser.Id, "Password123!"), Times.Once);

            // Verify policy was configured with folders
            _userManagerMock.Verify(u => u.UpdatePolicyAsync(createdUser.Id, It.Is<UserPolicy>(p => p.EnableAllFolders == false && p.EnabledFolders.Contains(folderId))), Times.Once);

            // Verify invite is exhausted
            var updatedInvite = manager.GetInvitationByCode(invite.Code);
            Assert.NotNull(updatedInvite);
            Assert.Equal(1, updatedInvite.UseCount);
            Assert.False(updatedInvite.IsValid);
        }

        [Fact]
        public async Task RedeemInvitation_InvalidOrExhaustedCode_ThrowsInvalidOperationException()
        {
            using var manager = new InvitationManager(
                NullLogger<InvitationManager>.Instance,
                _userManagerMock.Object,
                _appPathsMock.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.RedeemInvitationAsync("invalid-token", "hacker", "BadPassword"));
        }

        [Fact]
        public void RevokeInvitation_RevokesAndMarksInactive()
        {
            using var manager = new InvitationManager(
                NullLogger<InvitationManager>.Instance,
                _userManagerMock.Object,
                _appPathsMock.Object);

            var invite = manager.CreateInvitation(Guid.NewGuid(), new CreateInvitationRequest
            {
                LifetimeHours = 48,
                MaxUses = 1
            });

            Assert.True(invite.IsActive);

            var revoked = manager.RevokeInvitation(invite.Id);
            Assert.True(revoked);

            var fetched = manager.GetInvitationByCode(invite.Code);
            Assert.NotNull(fetched);
            Assert.False(fetched.IsActive);
            Assert.False(fetched.IsValid);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
