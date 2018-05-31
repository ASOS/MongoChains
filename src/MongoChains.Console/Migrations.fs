namespace MongoChains

module Console =

  open System
  open System.IO
  open MongoChains.Migrations
  open Chessie.ErrorHandling

  [<EntryPoint>]
  let main argv =
  
    let scriptDir = """C:\temp\migrations\"""
    let scripts = getMigrationScripts <| scriptDir

    let migrator = Migrator("mongodb://localhost:27017")
  
    let result = 
      async {
      let! result = migrator.ApplyMigrations scriptDir (["FOO", "BAR"]) BootstrapBehaviour.RunAllMigrations |> Async.ofAsyncResult
      return ()
      } |> Async.RunSynchronously

    printfn "%A" result
    
    0 // return an integer exit code
