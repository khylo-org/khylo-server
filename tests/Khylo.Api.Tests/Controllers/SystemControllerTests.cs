using Khylo.Api.Controllers;
using Khylo.Common.Net;
using Khylo.Controller;
using Khylo.Controller.Session;
using Khylo.Model.IO;
using Khylo.Server.Implementations.SystemBackupService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Khylo.Api.Tests.Controllers
{
    public class SystemControllerTests
    {
        [Fact]
        public void GetLogFile_FileDoesNotExist_ReturnsNotFound()
        {
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem
                .Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns([new() { Name = "file1.txt" }, new() { Name = "file2.txt" }]);

            var controller = new SystemController(
                Mock.Of<ILogger<SystemController>>(),
                Mock.Of<IServerApplicationHost>(),
                Mock.Of<IServerApplicationPaths>(),
                mockFileSystem.Object,
                Mock.Of<INetworkManager>(),
                Mock.Of<ISystemManager>(),
                Mock.Of<ISessionManager>());

            var result = controller.GetLogFile("DOES_NOT_EXIST.txt");

            Assert.IsType<NotFoundObjectResult>(result);
        }
    }
}
