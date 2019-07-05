module Neural

module Neuron =
    type Neuron<'a> = MailboxProcessor<'a>
    type Link<'a> = Neuron<'a> * float
    
    let feedForward (pack:float -> 'a) (links: Link<'a> list) msg =
        printfn "feeding forward %f" msg 
        for (n,w) in links do 
            msg * w
            |> pack
            |> n.Post

    type OutputMessage =
    | RegisterReplyChannel of AsyncReplyChannel<float>
    | Feed of float

    let output bufferCount = 
        Neuron<OutputMessage>.Start(fun inbox-> 
            let mutable reply : AsyncReplyChannel<_> option = None
            let rec loop buffer = async {
                let! msg = inbox.Receive()

                let feed v =
                    let newBuffer = v :: buffer
                    if newBuffer.Length = bufferCount then
                        let result = newBuffer |> List.sum
                        reply |> Option.iter (fun r -> r.Reply result)
                        []
                    else
                        newBuffer

                match msg with
                | RegisterReplyChannel c -> 
                    reply <- Some c
                    return! loop buffer
                | Feed v -> return! loop (feed v)
                }
            loop []
            )

    let private node feedForward bufferCount activate  =
        Neuron<float>.Start(fun inbox-> 
            let rec loop buffer = async {
                let! msg = inbox.Receive()
                
                let newBuffer = msg :: buffer

                if newBuffer.Length = bufferCount then
                   newBuffer |> activate |> feedForward
                   return! loop([])
                else
                   return! loop(newBuffer)
                }
            loop([]) 
            )

    let hidden pack links = node (feedForward pack links)
    let input links = node (feedForward id links) 1 List.exactlyOne 

type X = float
type Y = float

type Network = X * Y -> float

module Network =    
    open Neuron

    type MasterMessage = Ask of float * float * AsyncReplyChannel<float>

    let createRandom (x,y) =
        let activation = List.sum >> tanh
        
        let out = Neuron.output 2
        let h1 = Neuron.hidden (Feed) [(out,1.)] 2 activation
        let h2 = Neuron.hidden (Feed) [(out,1.)] 2 activation
        let xInput = Neuron.input [(h1,1.); (h2, 2.)]
        let yInput = Neuron.input [(h1,1.); (h2, 2.)]
        
        let master = MailboxProcessor<MasterMessage>.Start(fun inbox -> 
            let rec loop() = async {
                let! msg = inbox.Receive()
                
                match msg with
                | Ask (x,y,reply) -> 
                    out.Post (RegisterReplyChannel reply)
                    xInput.Post x
                    yInput.Post y
                    
                return! loop()
                }
            loop() 
            )

        master.PostAndReply(fun reply -> Ask (x,y,reply))