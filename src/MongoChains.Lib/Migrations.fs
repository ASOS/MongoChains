namespace MongoChains

module Migrations =
  open System.IO  
  open MongoDB.Driver
  open MongoDB.Bson
  open MongoDB.Bson.Serialization.Attributes
  open Chessie.ErrorHandling
  open FSharp.Data

  type ILogger =
    abstract member Log : string -> unit

  type BootstrapBehaviour =
  | RunAllMigrations
  | Abort

  type MigrationError =
  | RunJavascriptError of BsonDocument
  | SetMigrationVersionError
  | CouldNotDetermineCurrentMigrationVersion
  | CouldNotGrantEvalPermission
  | TokenNotSpecified of key:string
  | WhitespaceUsername
    with
      member this.FriendlyError =
        match this with
        | CouldNotDetermineCurrentMigrationVersion -> "Could not determine the current migration version as the migration record does not exist in the admin database. Run without safemode to force migrations to run."
        | CouldNotGrantEvalPermission -> "The credentials specified in the connection string do not have sufficient permission to run Eval."
        | RunJavascriptError err -> sprintf "An error occured running the migration: %s" (string err)
        | SetMigrationVersionError -> "An error occured trying to update the migration version record."
        | TokenNotSpecified key -> sprintf "You did not specify a value for token: %s" key
        | WhitespaceUsername -> "MongoDB username cannot consist purely of whitespace"
    
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

  let replaceTokens (tokens:seq<string * string>) (javascript:string) : Result<string, MigrationError>=
    
    let regex = System.Text.RegularExpressions.Regex("{#(.*)}")
    let tokensMap = Map.ofSeq tokens
    let tokensInJs = [| for m in regex.Matches(javascript) -> m.Groups.[1].Value |]
    
    trial {
    do! tokensInJs |> Seq.map (fun key -> if tokensMap.ContainsKey key then ok () else fail (TokenNotSpecified key)) |> Trial.collect |> Trial.lift ignore

    let replace = 
      tokens
      |> Seq.map (fun (k,v) -> sprintf "{#%s}" k, v)
      |> Seq.map (fun (k,v) -> fun (str:string) -> str.Replace(k, v))
      |> Seq.fold (>>) id
    
    return replace javascript
    }
  
  let getMigrationScripts (rootPath:string) =    
    Seq.initInfinite (fun n -> (n, Path.Combine(rootPath, sprintf "%d%cup.js" n Path.DirectorySeparatorChar)))
    |> Seq.skip 1
    |> Seq.takeWhile (fun (_, path) -> File.Exists path)

  type Migrator(mongoClient:IMongoClient, logger:ILogger) =
    let mongoClient = mongoClient.WithWriteConcern(WriteConcern.Acknowledged)
    
    let mongoDb = mongoClient.GetDatabase("admin")

    let mongoSucceeded (response:BsonDocument) =
      match response.TryGetValue "ok" with
      | true, x when x.IsDouble && x.AsDouble = 1.0 -> true
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
      logger.Log <| sprintf "Applying migration %-4d: %s... " n path
      let! js = File.ReadAllText(path) |> replaceTokens tokens
      let! result = runMongoJavascript js
      let! succeeded = if mongoSucceeded result then ok () else fail (RunJavascriptError result)
      logger.Log <| "[OK]\n"
      logger.Log <| sprintf "Setting current migration version to %d... " n
      do! setCurrentVersion n
      logger.Log <| "[OK]\n"
      return succeeded
      }

    let grantEvalPermission () =
      match mongoClient.Settings.Credential with
      | null -> logger.Log "Using anonymous mongo connection\n"; asyncTrial.Return ()
      | credentials when not (System.String.IsNullOrWhiteSpace(credentials.Username)) ->
        logger.Log <| "Using authenticated mongo connection. Attempting to grant eval role to user... "
        let grantEvalRole = JsonCommand<BsonDocument>(sprintf """{ grantRolesToUser: "%s", roles: [ { role: "__system", db: "admin"} ] }""" credentials.Username)
        asyncTrial {
        let! grantedPermissionResult = mongoClient.GetDatabase("admin").RunCommandAsync<BsonDocument>(grantEvalRole) |> Async.AwaitTask
        let! result = if mongoSucceeded grantedPermissionResult then ok () else fail CouldNotGrantEvalPermission
        logger.Log <| "[OK]\n"
        return result
        }
      | _ -> AsyncTrial.fail WhitespaceUsername

    let applyMigrations (rootPath:string) (tokens:seq<string * string>) (bootstrapBehaviour:BootstrapBehaviour) (targetVersion:Option<int>) : AsyncResult<unit,MigrationError> =
      
      asyncTrial {      

      let! currentVersion = getCurrentVersion ()
      
      do!
        if currentVersion = 0 && bootstrapBehaviour = Abort
          then fail CouldNotDetermineCurrentMigrationVersion
          else ok ()
      
      logger.Log <| sprintf "MongoDB is currently at migration version: %d\n" currentVersion
      
      let migrations =
        getMigrationScripts rootPath
        |> Seq.toArray

      do
        match migrations with
        | [||] -> logger.Log <| sprintf "No migrations found in %s\n" rootPath
        | migrations ->
          logger.Log <| sprintf "Found the following following migrations:\n"
          migrations |> Seq.iter (fun (n,path) ->
            logger.Log <| sprintf "%-4d: %s\n" n path
            )

      let migrationsToApply =
        migrations
        |> Seq.filter (fun (n,_) -> match targetVersion with Some targetVersion -> n <= targetVersion | None -> true)
        |> Seq.filter (fun (n,_) -> n > currentVersion)
        |> Seq.toArray

      do
        match migrationsToApply with
        | [||] -> logger.Log "Database is already at latest version. No migrations will be applied.\n"
        | [|(n,_)|] -> logger.Log <| sprintf "Will attempt to apply migration %d\n" n
        | xs ->
          let (first,_) = Array.head xs
          let (last, _) = Array.last xs
          logger.Log <| sprintf "Will attempt to apply migrations %d through %d\n" first last
      
      let applyMigrations =
        seq {
          if migrationsToApply.Length > 0 then yield grantEvalPermission ()
          yield! (migrationsToApply |> Seq.map (fun (n,path) -> runMigration n path tokens))
        }
        |> AsyncTrial.sequence
        |> AsyncTrial.ignore
         
      do! applyMigrations      
      }
    
    member __.GetCurrentVersion = getCurrentVersion
    member __.ApplyMigrations rootPath migrations bootstrapBehaviour = applyMigrations rootPath migrations bootstrapBehaviour