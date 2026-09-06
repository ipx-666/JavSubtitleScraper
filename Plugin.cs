using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;
using System.Collections.Generic;
using MediaBrowser.Model.Logging;

namespace JavSubtitleScraper;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string PluginName = "JavSubtitleScraper";
    public static Plugin Instance { get; private set; } = null!;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override Guid Id => new("1fbd6b47-0fa2-4f6e-bc3c-8e8c9b3d0f4d");
    public override string Name => PluginName;
    public override string Description => "Download Chinese subtitles for JAV videos";

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = "config.html",
            DisplayName = PluginName,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html",
            EnableInMainMenu = false,
            EnableInUserMenu = false,
            IsMainConfigPage = true
        }
    };
}
