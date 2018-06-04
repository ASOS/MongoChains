#r "paket: groupref FakeBuild //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.IO
open Fake.IO.Globbing.Operators //enables !! and globbing
open Fake.DotNet
open Fake.Core

// Properties
let outputDir = "./output/"

// *** Define Targets ***
Target.create "Clean" (fun _ ->
  Shell.cleanDir outputDir
)

Target.create "Package" (fun _ ->
  
  "src/MongoChains.sln"
  |> DotNet.build (fun opts -> {opts with Configuration = DotNet.BuildConfiguration.Release})

  "src/MongoChains.sln"    
  |> DotNet.pack (fun opts -> {opts with OutputPath = Some outputDir})
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
  ==> "Package"

// *** Start Build ***
Target.runOrDefault "Package"