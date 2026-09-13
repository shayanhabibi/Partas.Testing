module Partas.Testing.Tests.Say

open Expecto
open Partas.Testing

[<Tests>]
let tests =
    testList "Say" [test "hello greets by name"
                        { Expect.equal (Say.hello "world") "Hello, world!" "greeting should address the name" }]