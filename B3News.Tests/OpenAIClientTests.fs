module B3News.Tests.OpenAIClientTests

open System
open Xunit

[<Fact>]
let ``parseChatContent reads choices message content`` () =
    let json = """{"choices":[{"message":{"content":"  resumo curto  "}}]}"""
    match OpenAIClient.parseChatContent json with
    | Ok text -> Assert.Equal("resumo curto", text)
    | Error err -> failwith err

[<Fact>]
let ``parseChatContent treats null content as empty response`` () =
    // OpenAI-compatible APIs intermittently return HTTP 200 with choices[0].message.content = null.
    // This must decode as an empty (retryable) response, not a decode failure.
    match OpenAIClient.parseChatContent """{"choices":[{"message":{"content":null}}]}""" with
    | Error msg -> Assert.Equal("resposta LLM vazia", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``parseChatContent treats blank content as empty response`` () =
    match OpenAIClient.parseChatContent """{"choices":[{"message":{"content":"   "}}]}""" with
    | Error msg -> Assert.Equal("resposta LLM vazia", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``isTransientEmptyResponse marks only empty responses retryable`` () =
    Assert.True(OpenAIClient.isTransientEmptyResponse "resposta LLM vazia")
    Assert.False(OpenAIClient.isTransientEmptyResponse "resposta LLM sem choices")
    Assert.False(OpenAIClient.isTransientEmptyResponse "resposta LLM inválida: boom")

[<Fact>]
let ``parseChatContent fails on empty choices`` () =
    match OpenAIClient.parseChatContent """{"choices":[]}""" with
    | Error msg -> Assert.Contains("choices", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``parseChatContent fails on invalid json`` () =
    match OpenAIClient.parseChatContent "not-json" with
    | Error msg -> Assert.Contains("inválida", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``delayFor429 uses exponential backoff without header`` () =
    Assert.Equal(TimeSpan.FromSeconds 1.0, OpenAIClient.delayFor429 None 0)
    Assert.Equal(TimeSpan.FromSeconds 2.0, OpenAIClient.delayFor429 None 1)
    Assert.Equal(TimeSpan.FromSeconds 4.0, OpenAIClient.delayFor429 None 2)

[<Fact>]
let ``delayFor429 prefers Retry-After header`` () =
    let delay = OpenAIClient.delayFor429 (Some(TimeSpan.FromSeconds 7.0)) 0
    Assert.Equal(TimeSpan.FromSeconds 7.0, delay)

[<Fact>]
let ``delayFor429 caps exponential backoff at 30s`` () =
    Assert.Equal(TimeSpan.FromSeconds 30.0, OpenAIClient.delayFor429 None 10)

[<Fact>]
let ``summarize fails fast on invalid endpoint`` () =
    let result =
        OpenAIClient.summarize "not-a-url" "key" "model" "sys" "content" "label" 3 false
        |> Async.RunSynchronously
    match result with
    | Error msg -> Assert.Contains("Endpoint inválido", msg)
    | Ok _ -> failwith "expected error"
