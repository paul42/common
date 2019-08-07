// Copyright 2019 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Linq;

namespace Nuke.Common.BuildServers.Configuration
{
    public class TeamCityArtifactDependency : TeamCityDependency
    {
        public TeamCityBuildType BuildType { get; set; }
        public string[] ArtifactRules { get; set; }

        public override void Write(KotlinScriptWriter writer)
        {
            using (writer.WriteBlock($"artifacts({BuildType.Id})"))
            {
                writer.WriteLine("artifactRules = \"\"\"");
                foreach (var artifactRule in ArtifactRules)
                    writer.WriteLine(artifactRule);
                writer.WriteLine("\"\"\".trimIndent()");
            }
        }
    }
}
