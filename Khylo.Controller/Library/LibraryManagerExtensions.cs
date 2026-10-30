#pragma warning disable CS1591

using System;
using Khylo.Controller.Entities;

namespace Khylo.Controller.Library
{
    public static class LibraryManagerExtensions
    {
        public static BaseItem? GetItemById(this ILibraryManager manager, string id)
        {
            return manager.GetItemById(new Guid(id));
        }
    }
}
