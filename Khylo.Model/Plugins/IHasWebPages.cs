#pragma warning disable CS1591

using System.Collections.Generic;

namespace Khylo.Model.Plugins
{
    public interface IHasWebPages
    {
        IEnumerable<PluginPageInfo> GetPages();
    }
}
