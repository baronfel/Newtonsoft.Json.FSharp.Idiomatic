#r @"packages/build/FAKE/tools/FakeLib.dll"
open Fake.Core
open Fake.DotNet
open Fake.UserInputHelper
open Fake.IO
open Fake.Tools.Git
open System

open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators

let release = ReleaseNotes.load "RELEASE_NOTES.md"
let productName = "Newtonsoft.Json.FSharp.Idiomatic"
let sln = "Newtonsoft.Json.FSharp.Idiomatic.sln"
let srcGlob =__SOURCE_DIRECTORY__  @@ "src/**/*.??proj"
let testsGlob = __SOURCE_DIRECTORY__  @@ "tests/**/*.??proj"
let distDir = __SOURCE_DIRECTORY__  @@ "dist"
let distGlob = distDir @@ "*.nupkg"
let toolsDir = __SOURCE_DIRECTORY__  @@ "tools"

let coverageReportDir =  __SOURCE_DIRECTORY__  @@ "docs" @@ "coverage"

let gitOwner = "baronfel"
let gitRepoName = "Newtonsoft.Json.FSharp.Idiomatic"

let configuration =
    Environment.environVarOrDefault "CONFIGURATION" "Release"
    |> function | "Release" -> DotNet.BuildConfiguration.Release
                | "Debug"   -> DotNet.BuildConfiguration.Debug
                | c         -> DotNet.BuildConfiguration.Custom c

module dotnet =
    let watch program cmdParam args =
        let argConcat =
            args
            |> String.concat " "
        DotNet.exec id "watch" (sprintf "%s %s" program argConcat)


let isRelease (ctx: TargetParameter) =
    ctx.Context.AllExecutingTargets
    |> Seq.map (fun t -> t.Name)
    |> Seq.exists ((=) "Release")

Target.create "Clean" (fun _ ->
    ["bin"; "temp" ; distDir; coverageReportDir]
    |> Shell.cleanDirs

    !! srcGlob
    ++ testsGlob
    |> Seq.collect(fun p ->
        ["bin";"obj"]
        |> Seq.map(fun sp ->
             IO.Path.GetDirectoryName p @@ sp)
        )
    |> Shell.cleanDirs

    )

//AdditionalArgs = [sprintf "/p:PackageVersion=%s" release.NugetVersion]
Target.create "DotnetRestore" (fun _ ->
    [sln ; toolsDir]
    |> Seq.iter(DotNet.restore id)
)

Target.create "DotnetBuild" (fun ctx ->
    DotNet.build (fun c ->
        { c with
            Configuration = configuration
            //This makes sure that Proj2 references the correct version of Proj1
            Common = { c.Common with
                        CustomParams =
                          [ sprintf "/p:PackageVersion=%s" release.NugetVersion
                            sprintf "/p:SourceLinkCreate=%b" (isRelease ctx)
                            "--no-restore" ] |> String.concat " " |> Some }
        }) sln
)

let invokeAsync f = async { return f () }

Target.create "DotnetTest" (fun _ ->
    !! testsGlob
    |> Seq.iter (fun proj ->
        DotNet.test (fun c ->
            { c with
                Configuration = configuration
                Common = { c.Common with
                            CustomParams = Some "--no-build" }
                }) proj
    )
)

Target.create "GenerateCoverageReport" (fun _ ->
    let reportGenerator = "packages/build/ReportGenerator/tools/ReportGenerator.exe"
    let coverageReports =
        !!"tests/**/_Reports/MSBuildTest.xml"
        |> String.concat ";"
    let sourceDirs =
        !! srcGlob
        |> Seq.map Path.getDirectory
        |> String.concat ";"

    let executable = if Environment.isWindows then reportGenerator else "mono"
    let independentArgs =
            [
                sprintf "-reports:%s"  coverageReports
                sprintf "-targetdir:%s" coverageReportDir
                // Add source dir
                sprintf "-sourcedirs:%s" sourceDirs
                // Ignore Tests and if AltCover.Recorder.g sneaks in
                sprintf "-assemblyfilters:\"%s\"" "-*.Tests;-AltCover.Recorder.g"
                sprintf "-Reporttypes:%s" "Html"
            ]

    let args =
      (if Environment.isWindows
      then independentArgs
      else reportGenerator :: independentArgs)
      |> String.concat " "

    Trace.tracefn "%s %s" executable args
    let exitCode = Shell.Exec(executable, args = args)
    if exitCode <> 0 then
        failwithf "%s failed with exit code: %d" reportGenerator exitCode
)

Target.create "WatchTests" (fun _ ->
    !! testsGlob
    |> Seq.map(fun proj -> fun () ->
        dotnet.watch "test"
            (fun cmd ->
                { cmd with
                     WorkingDir = IO.Path.GetDirectoryName proj
                })
            []
    )
    |> Seq.iter (invokeAsync >> Async.Catch >> Async.Ignore >> Async.Start)

    printfn "Press Ctrl+C (or Ctrl+Break) to stop..."
    let cancelEvent = Console.CancelKeyPress |> Async.AwaitEvent |> Async.RunSynchronously
    cancelEvent.Cancel <- true
)

Target.create "AssemblyInfo" (fun _ ->
    let releaseChannel =
        match release.SemVer.PreRelease with
        | Some pr -> pr.Name
        | _ -> "release"
    let getAssemblyInfoAttributes projectName =
        [ AssemblyInfo.Title (projectName)
          AssemblyInfo.Product productName
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.Metadata("ReleaseDate", release.Date.Value.ToString("o"))
          AssemblyInfo.FileVersion release.AssemblyVersion
          AssemblyInfo.InformationalVersion release.AssemblyVersion
          AssemblyInfo.Metadata("ReleaseChannel", releaseChannel)
          AssemblyInfo.Metadata("GitHash", Information.getCurrentSHA1(null))
        ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath,
          projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName)
        )

    !! srcGlob
    ++ testsGlob
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
        match projFileName with
        | p when p.EndsWith("fsproj") -> AssemblyInfoFile.createFSharp (folderName @@ "AssemblyInfo.fs") attributes
        | p when p.EndsWith("csproj") -> AssemblyInfoFile.createCSharp ((folderName @@ "Properties") @@ "AssemblyInfo.cs") attributes
        | p when p.EndsWith("vbproj") -> AssemblyInfoFile.createVisualBasic ((folderName @@ "My Project") @@ "AssemblyInfo.vb") attributes
        | _ -> ()
        )
)

Target.create "DotnetPack" (fun ctx ->
    !! srcGlob
    |> Seq.iter (fun proj ->
        DotNet.pack (fun c ->
            { c with
                Configuration = configuration
                OutputPath = Some distDir
                Common = { c.Common with
                            CustomParams = [ sprintf "/p:PackageVersion=%s" release.NugetVersion
                                             sprintf "/p:PackageReleaseNotes=\"%s\"" (String.Join("\n",release.Notes))
                                             sprintf "/p:SourceLinkCreate=%b" (isRelease ctx) ] |> String.concat " " |> Some
                }
            }) proj
    )
)

Target.create "SourcelinkTest" (fun _ ->
    !! distGlob
    |> Seq.iter (fun nupkg ->
        DotNet.exec (fun p -> { p with WorkingDirectory = toolsDir} ) "sourcelink" (sprintf "test %s" nupkg)
        |> ignore
    )
)

let isReleaseBranchCheck () =
    let releaseBranch = "master"
    if Information.getBranchName "" <> releaseBranch then failwithf "Not on %s.  If you want to release please switch to this branch." releaseBranch

Target.create "Publish" (fun _ ->
    isReleaseBranchCheck ()

    Paket.push(fun c ->
            { c with
                PublishUrl = "https://www.nuget.org"
                WorkingDir = "dist"
            }
        )
)

Target.create "GitRelease" (fun _ ->
    isReleaseBranchCheck ()

    let releaseNotesGitCommitFormat = ("",release.Notes |> Seq.map(sprintf "* %s\n")) |> String.Join

    Staging.stageAll ""
    Commit.exec "" (sprintf "Bump version to %s \n%s" release.NugetVersion releaseNotesGitCommitFormat)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion
)

#load "./paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

Target.create "GitHubRelease" (fun _ ->
    let client =
        match Environment.GetEnvironmentVariable "GITHUB_TOKEN" with
        | null -> failwithf "set the GITHUB_TOKEN env variable to release"
        | token -> createClientWithToken token

    client
    |> createDraft gitOwner gitRepoName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    |> fun draft ->
        !! distGlob
        |> Seq.fold (fun draft pkg -> draft |> uploadFile pkg) draft
    |> releaseDraft
    |> Async.RunSynchronously

)

Target.create "Release" ignore

open Fake.Core.TargetOperators

// Only call Clean if DotnetPack was in the call chain
// Ensure Clean is called before DotnetRestore
"Clean" ?=> "DotnetRestore"
"Clean" ==> "DotnetPack"

// Only call AssemblyInfo if Publish was in the call chain
// Ensure AssemblyInfo is called after DotnetRestore and before DotnetBuild
"DotnetRestore" ?=> "AssemblyInfo"
"AssemblyInfo" ?=> "DotnetBuild"
"AssemblyInfo" ==> "Publish"

"DotnetRestore"
  ==> "DotnetBuild"
  ==> "DotnetTest"
  ==> "GenerateCoverageReport"
  ==> "DotnetPack"
  ==> "SourcelinkTest"
  ==> "Publish"
  ==> "GitRelease"
  ==> "GitHubRelease"
  ==> "Release"

"DotnetRestore"
 ==> "WatchTests"

Target.runOrDefault "DotnetPack"
