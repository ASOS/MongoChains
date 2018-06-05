namespace MongoChains

module internal AsyncTrial =
  open Chessie.ErrorHandling
  
  let lift f = Async.ofAsyncResult >> Async.map f >> AR
  let mapSuccess f = lift (Trial.lift f)  
  let ignore comp = comp |> mapSuccess (fun _ -> ())
  let liftAsync (result:Result<'a,'b>) : AsyncResult<'a,'b> =
    asyncTrial {
    let! lifted = result
    return lifted
    }

  let ok x = liftAsync (Trial.ok x)
  let fail x = liftAsync (Trial.fail x)

  let sequence (computations:seq<AsyncResult<'a, 'b>>) : AsyncResult<'a list, 'b> =
    computations
    |> Seq.fold (fun (acc:AsyncResult<'a list, 'b>) (next:AsyncResult<'a,'b>) ->
      asyncTrial {
      let! current = acc
      return!
        asyncTrial {
        let! result = next
        return result :: current
        }
      }) (asyncTrial.Return [])
    |> mapSuccess (List.rev)