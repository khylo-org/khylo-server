using System;

namespace Khylo.Controller.Library;

/// <summary>
/// A share couldn't be connected. The message is written for the admin.
/// </summary>
public class NetworkShareException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkShareException"/> class.
    /// </summary>
    public NetworkShareException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkShareException"/> class.
    /// </summary>
    /// <param name="message">What went wrong, in plain words.</param>
    public NetworkShareException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkShareException"/> class.
    /// </summary>
    /// <param name="message">What went wrong, in plain words.</param>
    /// <param name="innerException">The underlying error.</param>
    public NetworkShareException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
