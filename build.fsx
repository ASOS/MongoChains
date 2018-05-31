#r "paket: groupref FakeBuild //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.IO
open Fake.IO.Globbing.Operators //enables !! and globbing
open Fake.DotNet
open Fake.Core

// Properties
let buildDir = "./build/"


// *** Define Targets ***
Target.create "Clean" (fun _ ->
  Shell.CleanDir buildDir
)

Target.create "Build" (fun _ ->
  !! "src/MongoMigrator.sln"
    |> MSBuild.runRelease (fun x -> { x with Targets = ["Build"]}) buildDir "Build"
    |> Trace.logItems "AppBuild-Output: "
)

Target.create "Package" (fun _ ->
  !! "src/MongoMigrator.sln"
    |> MSBuild.runRelease (fun x -> { x with Targets = ["Pack"]}) buildDir "Build"
    |> Trace.logItems "AppBuild-Output: "
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
  ==> "Build"
  ==> "Package"

// *** Start Build ***
Target.runOrDefault "Package"