using System;
using System.IO;
using Khylo.Api.Helpers;
using Xunit;

namespace Khylo.Api.Tests.Helpers;

public class LibraryFolderAccessTests
{
    [Fact]
    public void TheMessage_NamesTheFolderAndTheAccount_AndSaysWhatToDo()
    {
        var message = LibraryFolderAccess.Message(@"C:\Users\someone\Videos", @"NT AUTHORITY\LOCAL SERVICE");

        Assert.Contains(@"C:\Users\someone\Videos", message, StringComparison.Ordinal);
        Assert.Contains(@"NT AUTHORITY\LOCAL SERVICE", message, StringComparison.Ordinal);
        Assert.Contains("read access", message, StringComparison.Ordinal);
        Assert.Contains("run Khylo's setup again", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatCanBeOpened_AMissingOne_AndNothingAtAll_AreNotProblems()
    {
        var folder = Path.Combine(Path.GetTempPath(), "khylo-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Null(LibraryFolderAccess.ProblemOpening(folder));
            Assert.Null(LibraryFolderAccess.ProblemOpening(Path.Combine(folder, "not-there")));
            Assert.Null(LibraryFolderAccess.ProblemOpening(null));
            Assert.Null(LibraryFolderAccess.ProblemOpening("  "));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
