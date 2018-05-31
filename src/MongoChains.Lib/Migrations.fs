namespace MongoChains

module Migrations =
  open System.IO  
  open MongoDB.Driver
  open MongoDB.Bson
  open MongoDB.Bson.Serialization.Attributes
  open Chessie.ErrorHandling
  open FSharp.Data

  type BootstrapBehaviour =
  | RunAllMigrations
  | Abort

  type MigrationError =
  | RunJavascriptError of BsonDocument
  | SetMigrationVersionError
  | CouldNotDetermineCurrentMigrationVersion
  | CouldNotGrantEvalPermission

  type MigrationDocument =
    {
      [<BsonId>]
      Id : ObjectId
      [<BsonElement("CurrentVersion")>]
      CurrentVersion : int
    }

  let migrationMetadataCollection = "migrations"  
  let migrationDocumentId = ObjectId("5b042070982d96d2252ad46f")
  let migrationCollection (mongoDb:MongoDB.Driver.IMongoDatabase) = mongoDb.GetCollection(migrationMetadataCollection)

  let replaceTokens (tokens:seq<string * string>) (javascript:string) =
    
    let replace = 
      tokens
      |> Seq.map (fun (k,v) -> sprintf "{#%s}" k, v)
      |> Seq.map (fun (k,v) -> fun (str:string) -> str.Replace(k, v))
      |> Seq.fold (>>) id
    
    replace javascript
  
  let getMigrationScripts (rootPath:string) =    
    Seq.initInfinite (fun n -> (n, Path.Combine(rootPath, sprintf "%d%cup.js" n Path.DirectorySeparatorChar)))
    |> Seq.skip 1
    |> Seq.takeWhile (fun (_, path) -> File.Exists path)

  type Migrator(mongoClient:IMongoClient) =
    let mongoClient = mongoClient.WithWriteConcern(WriteConcern.Acknowledged)
    
    let mongoDb = mongoClient.GetDatabase("admin")

    let mongoSucceeded (response:BsonDocument) =
      match response.TryGetValue "ok" with
      | true, x -> x.IsDouble && x.AsDouble = 1.0
      | _ -> false

    let runMongoJavascript (js:string) =
      let jsonCmd = JsonValue.Record [|"eval", JsonValue.String js|]    
      let cmd = JsonCommand<BsonDocument>(string jsonCmd)
      mongoDb.RunCommandAsync(cmd) |> Async.AwaitTask

    let getCurrentVersion () : Async<int> =
      async {
      let migrationsCollection = migrationCollection mongoDb
      let! migrationDocs = migrationsCollection.FindAsync<MigrationDocument>(fun x -> x.Id = migrationDocumentId) |> Async.AwaitTask |> Async.bind (fun x -> x.ToListAsync() |> Async.AwaitTask)
      let migrationDoc = migrationDocs |> Seq.tryHead |> Option.map (fun x -> x.CurrentVersion) |> Option.defaultValue 0
      return migrationDoc
      }

    let setCurrentVersion (version:int) : AsyncResult<unit,MigrationError> =
      asyncTrial {
      let migrationsCollection = migrationCollection mongoDb
      let filter = Builders<MigrationDocument>.Filter.Eq((fun x -> x.Id), migrationDocumentId)
      let update = Builders<MigrationDocument>.Update.Set((fun x -> x.CurrentVersion), version)
      let! result = migrationsCollection.UpdateOneAsync(filter, update, UpdateOptions(IsUpsert = true)) |> Async.AwaitTask
      do! if result.IsAcknowledged then ok () else fail (SetMigrationVersionError)
      }
    
    let runMigration (n:int) (path:string) (tokens:seq<string * string>) =
      asyncTrial {
      printfn "Applying migration %4d: %s" n path
      let jsRaw = File.ReadAllText(path) |> replaceTokens tokens
      let! result = runMongoJavascript js
      let! succeeded = if mongoSucceeded result then ok () else fail (RunJavascriptError result)
      printfn "Seting current version to %d" n
      do! setCurrentVersion n
      return succeeded
      }

    let grantEvalPermission () =
      match mongoClient.Settings.Credential with
      | null -> asyncTrial.Return ()
      | credentials when not (System.String.IsNullOrWhiteSpace(credentials.Username)) ->
        let grantEvalRole = JsonCommand<BsonDocument>(sprintf """{ grantRolesToUser: "%s", roles: [ { role: "__system", db: "admin"} ] }""" credentials.Username)
        asyncTrial {
        let! grantedPermissionResult = mongoClient.GetDatabase("admin").RunCommandAsync<BsonDocument>(grantEvalRole) |> Async.AwaitTask
        let! result = if mongoSucceeded grantedPermissionResult then ok () else fail CouldNotGrantEvalPermission
        return result
        }
      | _ -> asyncTrial.Return ()

    let applyMigrations (rootPath:string) (tokens:seq<string * string>) (bootstrapBehaviour:BootstrapBehaviour) : AsyncResult<unit,MigrationError> =
      
      asyncTrial {      

      let! currentVersion = getCurrentVersion ()
      
      do!
        if currentVersion = 0 && bootstrapBehaviour = Abort
          then fail CouldNotDetermineCurrentMigrationVersion
          else ok ()
      
      printfn "MongoDB Current Version: %d" currentVersion
      
      let migrations =
        getMigrationScripts rootPath
        |> Seq.filter (fun (n,_) -> n > currentVersion)
        |> Seq.map (fun (n,path) -> runMigration n path tokens)
        |> AsyncTrial.sequence
        |> AsyncTrial.ignore
            
      do! migrations      
      }
    
    member __.GrantEvalPermission = grantEvalPermission
    member __.GetCurrentVersion = getCurrentVersion
    member __.ApplyMigrations rootPath migrations bootstrapBehaviour = applyMigrations rootPath migrations bootstrapBehaviour