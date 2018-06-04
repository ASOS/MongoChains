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
    let migrator = Migrator(client)
    let bootstrapBehaviour = if cliArgs.Contains Safemode then BootstrapBehaviour.Abort else BootstrapBehaviour.RunAllMigrations
  
    let result = 
      migrator.ApplyMigrations migrationsPath tokens bootstrapBehaviour targetVersion
      |> Async.ofAsyncResult
      |> Async.RunSynchronously

    let printError (err:MigrationError) =
      match err with
      | MigrationError.CouldNotDetermineCurrentMigrationVersion -> "Could not determine the current migration version as the migration record does not exist in the admin database. Run without safemode to force migrations to run."
      | MigrationError.CouldNotGrantEvalPermission -> "The credentials specified in the connection string do not have sufficient permission to run Eval."
      | MigrationError.RunJavascriptError err -> sprintf "An error occured running the migration: %s" (string err)
      | MigrationError.SetMigrationVersionError -> "An error occured trying to update the migration version record."
      | MigrationError.TokenNotSpecified key -> sprintf "You did not specify a value for token: %s" key
    
    let printErrors errs = errs |> Seq.map printError |> Seq.iter (printfn "%s")

    match result with
    | Result.Ok (_,[]) -> printfn "Completed successfully"; 0
    | Result.Ok (_,warnings) -> printfn "Completed successfully with warnings:"; printErrors warnings; 0
    | Result.Bad errors -> printfn "Failed with errors:"; printErrors errors; -1
