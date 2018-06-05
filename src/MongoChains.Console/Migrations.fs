namespace MongoChains

module Console =

  open System
  open MongoChains.Migrations
  open Chessie.ErrorHandling
  open Argu

  type CLIArguments =
  | [<Mandatory>] [<Unique>] Target of connectionString:string
  | [<Mandatory>] [<Unique>] Path of path:string
  | [<Unique>] TargetVersion of version:int
  | [<EqualsAssignment>] Token of key:string * value:string
  | [<Unique>] Safemode
    interface IArgParserTemplate with
      member s.Usage =
        match s with
        | Target _ -> "connection string for the mongo database"
        | Path _ -> "path to migrations files"
        | TargetVersion _ -> "target a specific version"
        | Token _ -> "a token to be replaced in the migrations"
        | Safemode _ -> "abort without running scripts if current version of db cannot be determined"

  [<EntryPoint>]
  let main argv =
  
    let errorHandler = ProcessExiter(colorizer = function ErrorCode.HelpText -> None | _ -> Some ConsoleColor.Red)
    let parser = ArgumentParser.Create<CLIArguments>(programName = "mongochains", errorHandler = errorHandler)

    let cliArgs = parser.Parse argv

    let mongoConnectionString = cliArgs.GetResult Target
    let migrationsPath = cliArgs.GetResult Path
    let tokens = cliArgs.GetResults Token
    let targetVersion = cliArgs.TryGetResult TargetVersion

    let client = MongoDB.Driver.MongoClient(mongoConnectionString)
    let logger = { new Migrations.ILogger with member __.Log str = printf "%s" str }
    let migrator = Migrator(client, logger)
    let bootstrapBehaviour = if cliArgs.Contains Safemode then BootstrapBehaviour.Abort else BootstrapBehaviour.RunAllMigrations
  
    let result = 
      migrator.ApplyMigrations migrationsPath tokens bootstrapBehaviour targetVersion
      |> Async.ofAsyncResult
      |> Async.RunSynchronously
    
    let printErrors (errs:seq<MigrationError>) = errs |> Seq.iter (fun err -> printfn "%s" err.FriendlyError)

    match result with
    | Result.Ok (_,[]) -> printfn "\nCompleted successfully"; 0
    | Result.Ok (_,warnings) -> printfn "\nCompleted successfully with warnings:"; printErrors warnings; 0
    | Result.Bad errors -> printfn "\nFailed with errors:"; printErrors errors; -1
