// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using NuGet.Packaging;
using NuGet.ProjectModel;

var database = new ConcurrentDictionary<
    Tuple<string /* target */, string /* id */>,
    LockFileDependency>();

foreach (var filename in Directory.EnumerateFiles(
    args[0],
    "packages.lock.json",
    new EnumerationOptions()
    {
        MatchType = MatchType.Simple,
        RecurseSubdirectories = true,
    }))
{
    var packagesLockFileFormat = PackagesLockFileFormat.Read(filename);
    foreach (var target in packagesLockFileFormat.Targets)
    {
        foreach (var dependency in target.Dependencies)
        {
            if (dependency.Type == PackageDependencyType.Project)
            {
                continue;
            }

            database.AddOrUpdate(Tuple.Create(target.Name, dependency.Id), dependency, (_, exist) =>
            {
                if (exist.Type == PackageDependencyType.Direct)
                {
                    return exist;
                }

                if (dependency.Type == PackageDependencyType.Direct)
                {
                    return dependency;
                }

                if (exist.Type == PackageDependencyType.CentralTransitive)
                {
                    return exist;
                }

                if (exist.ResolvedVersion >= dependency.ResolvedVersion)
                {
                    return exist;
                }

                return dependency;
            });
        }
    }
}

var targetTypeDependencies = (from kvp in database
                            select new
                            {
                                TargetName = kvp.Key.Item1,
                                kvp.Value.Type,
                                Dependency = kvp.Value,
                            }).ToList();

XDocument centralPackageVersionsFile;
using (var reader = XmlReader.Create(
    $"{args[0]}/Directory.Packages.props",
    new XmlReaderSettings { Async = true }))
{
    centralPackageVersionsFile =
        await XDocument.LoadAsync(reader, LoadOptions.SetLineInfo, CancellationToken.None);
}

var packageVersions = centralPackageVersionsFile
    .Root
    .ElementsNoNamespace("ItemGroup")
    .SelectMany(itemGroup => itemGroup.ElementsNoNamespace("PackageVersion"))
    .ToDictionary(
        elem => elem.Attribute("Include")?.Value ?? string.Empty,
        elem => elem.Attribute("Version")?.Value);

Console.WriteLine("Can remove these dependencies");
var dependenciesHashSet =
    targetTypeDependencies.Select(obj => obj.Dependency.Id).ToImmutableHashSet();
foreach (var packageVersion in packageVersions)
{
    if (dependenciesHashSet.Contains(packageVersion.Key))
    {
        continue;
    }

    Console.WriteLine($"\t{packageVersion.Key}");
}

Console.WriteLine();

Console.WriteLine("Suggest pin these dependencies");
var packageVersionsHashSet =
    packageVersions.Select(packageVersion => packageVersion.Key).ToImmutableHashSet();
foreach (var targetGroup in from ttd in targetTypeDependencies
                            where ttd.Type == PackageDependencyType.Transitive
                                && !ttd.Dependency.Id.StartsWith(
                                    "runtime.",
                                    StringComparison.InvariantCulture)
                            where !packageVersionsHashSet.Contains(ttd.Dependency.Id)
                            where !ttd.Dependency.Id.Contains(".runtime.")
                            group ttd.Dependency by ttd.TargetName into g
                            orderby g.Key
                            select g)
{
    Console.WriteLine($"\tTargetName={targetGroup.Key}");
    foreach (var dependency in targetGroup.OrderBy(d => d.Id))
    {
        Console.WriteLine(
            $"    <PackageVersion Include=\"{dependency.Id}\"" +
            $" Version=\"{dependency.ResolvedVersion}\" />");
    }
}
