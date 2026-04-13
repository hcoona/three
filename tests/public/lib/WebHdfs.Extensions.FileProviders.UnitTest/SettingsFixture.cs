using System;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace WebHdfs.Extensions.FileProviders.UnitTest
{
    public class SettingsFixture
    {
        public SettingsFixture()
        {
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddIniFile("config.ini", true)
                .AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());

            var root = builder.Build();

            var settings = new Settings();
            root.Bind(settings);

            this.Settings = settings;
        }

        internal Settings Settings { get; }
    }
}
