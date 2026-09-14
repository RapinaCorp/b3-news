module B3News.Tests.NvidiaClientTests

open System
open Xunit

[<Fact>]
let ``parseChatContent reads choices message content`` () =
    let json = """{"choices":[{"message":{"content":"  resumo curto  "}}]}"""
    match NvidiaClient.parseChatContent json with
    | Ok text -> Assert.Equal("resumo curto", text)
    | Error err -> failwith err

[<Fact>]
let ``parseChatContent treats null content as empty response`` () =
    // NVIDIA intermittently returns HTTP 200 with choices[0].message.content = null.
    // This must decode as an empty (retryable) response, not a decode failure.
    match NvidiaClient.parseChatContent """{"choices":[{"message":{"content":null}}]}""" with
    | Error msg -> Assert.Equal("resposta NVIDIA vazia", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``parseChatContent treats blank content as empty response`` () =
    match NvidiaClient.parseChatContent """{"choices":[{"message":{"content":"   "}}]}""" with
    | Error msg -> Assert.Equal("resposta NVIDIA vazia", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``isTransientEmptyResponse marks only empty responses retryable`` () =
    Assert.True(NvidiaClient.isTransientEmptyResponse "resposta NVIDIA vazia")
    Assert.False(NvidiaClient.isTransientEmptyResponse "resposta NVIDIA sem choices")
    Assert.False(NvidiaClient.isTransientEmptyResponse "resposta NVIDIA inválida: boom")

[<Fact>]
let ``parseChatContent fails on empty choices`` () =
    match NvidiaClient.parseChatContent """{"choices":[]}""" with
    | Error msg -> Assert.Contains("choices", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``parseChatContent fails on invalid json`` () =
    match NvidiaClient.parseChatContent "not-json" with
    | Error msg -> Assert.Contains("inválida", msg)
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``delayFor429 uses exponential backoff without header`` () =
    Assert.Equal(TimeSpan.FromSeconds 1.0, NvidiaClient.delayFor429 None 0)
    Assert.Equal(TimeSpan.FromSeconds 2.0, NvidiaClient.delayFor429 None 1)
    Assert.Equal(TimeSpan.FromSeconds 4.0, NvidiaClient.delayFor429 None 2)

[<Fact>]
let ``delayFor429 prefers Retry-After header`` () =
    let delay = NvidiaClient.delayFor429 (Some(TimeSpan.FromSeconds 7.0)) 0
    Assert.Equal(TimeSpan.FromSeconds 7.0, delay)

[<Fact>]
let ``delayFor429 caps exponential backoff at 30s`` () =
    Assert.Equal(TimeSpan.FromSeconds 30.0, NvidiaClient.delayFor429 None 10)
