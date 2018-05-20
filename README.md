# Newtonsoft.Json.FSharp.Idiomatic

A set of Converters for Json.Net that (de)serialize unions in a nicer format than the out-of-box converters.

The provided converters are:
* `OptionConverter`
  * allows for use of options instead of nullables, treats Null as None
* `SingleCaseDuConverter`
  * reads and writes enum-style DU's (ie `type Foo = | Bar | Baz | Qux`) as their string name
* `MultiCaseDuConverter`
  * reads and writes DUs with data attached as an object with a `kind` property equal to the case name, and whose property names and values are drawn from the DU property names and value. This Converter is fairly fragile and requires that the `kind` property (or a customizable property name) is first in order on the JSON.
* `OutOfOrderMultiCaseDuConverter`
  * This converter reads/buffers the object properties of a JSON object and uses the property names to infer the correct DU case to deserialize into. It requires that your DU cases have property names that are set-distinct (that is no 100% overlap), and that the JSON contains all properties of the DU, even if some of them are `null`.

---

## Builds

MacOS/Linux | Windows
--- | ---
[![Travis Badge](https://travis-ci.org/baronfel/Newtonsoft.Json.FSharp.Idiomatic.svg?branch=master)](https://travis-ci.org/baronfel/Newtonsoft.Json.FSharp.Idiomatic) | [![Build status](https://ci.appveyor.com/api/projects/status/github/baronfel/Newtonsoft.Json.FSharp.Idiomatic?svg=true)](https://ci.appveyor.com/project/baronfel/Newtonsoft.Json.FSharp.Idiomatic)
[![Build History](https://buildstats.info/travisci/chart/baronfel/Newtonsoft.Json.FSharp.Idiomatic)](https://travis-ci.org/baronfel/Newtonsoft.Json.FSharp.Idiomatic/builds) | [![Build History](https://buildstats.info/appveyor/chart/baronfel/Newtonsoft.Json.FSharp.Idiomatic)](https://ci.appveyor.com/project/baronfel/Newtonsoft.Json.FSharp.Idiomatic)  


## Nuget 

Stable | Prerelease
--- | ---
[![NuGet Badge](https://buildstats.info/nuget/Newtonsoft.Json.FSharp.Idiomatic)](https://www.nuget.org/packages/Newtonsoft.Json.FSharp.Idiomatic/) | [![NuGet Badge](https://buildstats.info/nuget/Newtonsoft.Json.FSharp.Idiomatic?includePreReleases=true)](https://www.nuget.org/packages/Newtonsoft.Json.FSharp.Idiomatic/)

---

### Building


Make sure the following **requirements** are installed in your system:

* [dotnet SDK](https://www.microsoft.com/net/download/core) 2.0 or higher
* [Mono](http://www.mono-project.com/) if you're on Linux or macOS.

```
> build.cmd // on windows
$ ./build.sh  // on unix
```

#### Environment Variables

* `CONFIGURATION` will set the [configuration](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-build?tabs=netcore2x#options) of the dotnet commands.  If not set it will default to Release.
  * `CONFIGURATION=Debug ./build.sh` will result in things like `dotnet build -c Debug`
* `GITHUB_TOKEN` will be used to upload release notes and nuget packages to github.
  * Be sure to set this before releasing

### Watch Tests

The `WatchTests` target will use [dotnet-watch](https://github.com/aspnet/Docs/blob/master/aspnetcore/tutorials/dotnet-watch.md) to watch for changes in your lib or tests and re-run your tests on all `TargetFrameworks`

```
./build.sh WatchTests
```

### Releasing
* [Start a git repo with a remote](https://help.github.com/articles/adding-an-existing-project-to-github-using-the-command-line/)

```
git add .
git commit -m "Scaffold"
git remote add origin origin https://github.com/user/MyCoolNewLib.git
git push -u origin master
```

* [Add your nuget API key to paket](https://fsprojects.github.io/Paket/paket-config.html#Adding-a-NuGet-API-key)

```
paket config add-token "https://www.nuget.org" 4003d786-cc37-4004-bfdf-c4f3e8ef9b3a
```

* [Create a GitHub OAuth Token](https://help.github.com/articles/creating-a-personal-access-token-for-the-command-line/)
    * You can then set the `GITHUB_TOKEN` to upload release notes and artifacts to github
    * Otherwise it will fallback to username/password


* Then update the `RELEASE_NOTES.md` with a new version, date, and release notes [ReleaseNotesHelper](https://fsharp.github.io/FAKE/apidocs/fake-releasenoteshelper.html)

```
#### 0.2.0 - 2017-04-20
* FEATURE: Does cool stuff!
* BUGFIX: Fixes that silly oversight
```

* You can then use the `Release` target.  This will:
    * make a commit bumping the version:  `Bump version to 0.2.0` and add the release notes to the commit
    * publish the package to nuget
    * push a git tag

```
./build.sh Release
```
