using System;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// A problem the admin should see, in plain words.
/// </summary>
public class ImportException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImportException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public ImportException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportException"/> class.
    /// </summary>
    public ImportException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public ImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
