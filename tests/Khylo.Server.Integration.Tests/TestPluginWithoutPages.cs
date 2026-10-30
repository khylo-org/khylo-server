#pragma warning disable CS1591

using System;
using Khylo.Common.Configuration;
using Khylo.Common.Plugins;
using Khylo.Model.Plugins;
using Khylo.Model.Serialization;

namespace Khylo.Server.Integration.Tests
{
    public class TestPluginWithoutPages : BasePlugin<BasePluginConfiguration>
    {
        public TestPluginWithoutPages(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static TestPluginWithoutPages? Instance { get; private set; }

        public override Guid Id => new Guid("ae95cbe6-bd3d-4d73-8596-490db334611e");

        public override string Name => nameof(TestPluginWithoutPages);

        public override string Description => "Server test Plugin without web pages.";
    }
}
