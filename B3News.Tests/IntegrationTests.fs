module B3News.Tests.IntegrationTests

open System
open System.IO
open System.Net.Http
open System.Text.Json
open System.Diagnostics
open System.Threading
open Xunit
open Details

// Helper to deserialize the API response
type NewsApiResponse =
    { news: NewsDetail list
      totalPages: int
      totalRecords: int }

[<Fact>]
let ``Simulate pagination and data update flow`` () =
    let reportsJsonPath = "reports.json"
    let backupPath = reportsJsonPath + ".bak"

    // Ensure clean state before starting
    if File.Exists(backupPath) then
        File.Delete(backupPath)
    if File.Exists(reportsJsonPath) then
        File.Move(reportsJsonPath, backupPath)

    try
        // Create initial data
        let initialNews =
            [ for i in 1..5 ->
                  { id = i
                    ticker = sprintf "TICK%d" i
                    dateTime = sprintf "2024-01-01 10:00:0%d" i
                    reportType = "Fato Relevante"
                    filename = sprintf "file%d.pdf" i
                    url = sprintf "http://example.com/%d" i } ]
        let initialJson = JsonSerializer.Serialize(initialNews)
        File.WriteAllText(reportsJsonPath, initialJson)

        // Copy settings file to test output directory
        let settingsJson =
            """
        {
            "Kestrel": {
                "Endpoints": {
                    "Http": {
                        "Url": "http://localhost:8082"
                    }
                }
            }
        }
        """
        let settingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "b3news.settings.json")
        File.WriteAllText(settingsPath, settingsJson)

        let serverThread = new Thread(fun () -> Api.startApiServer [||] |> ignore)
        serverThread.IsBackground <- true
        serverThread.Start()
        Thread.Sleep(5000) // Give server time to start

        use client = new HttpClient()
        client.BaseAddress <- new Uri("http://localhost:8082") // Assuming default port

        // --- 2. Initial Client Load & Pagination ---
        // Step 1: Client requests first page with updateRecords=true. Should get 3 items, 2 pages, 5 total records.
        let mutable localRecordCount = 0

        // First load, updateRecords=true
        let resp1 = client.GetAsync("/api/news?pageSize=3&page=1&updateRecords=true").Result
        Assert.True((resp1.IsSuccessStatusCode), "[Step 1] Initial request should succeed.")
        let data1 =
            JsonSerializer.Deserialize<NewsApiResponse>(resp1.Content.ReadAsStringAsync().Result)
        Assert.True(
            (data1.totalRecords = 5),
            (sprintf "[Step 1] Expected 5 total records after initial load, got %d" data1.totalRecords)
        )
        Assert.True(
            (data1.totalPages = 2),
            (sprintf "[Step 1] Expected 2 total pages after initial load, got %d" data1.totalPages)
        )
        Assert.True(
            (data1.news.Length = 3),
            (sprintf "[Step 1] Expected 3 news items on first page, got %d" data1.news.Length)
        )
        localRecordCount <- data1.totalRecords

        // Step 2: Client paginates to page 2. Should get 2 items, total records unchanged.
        let resp2 =
            client
                .GetAsync($"/api/news?pageSize=3&page=2&clientRecordCount={localRecordCount}")
                .Result
        Assert.True((resp2.IsSuccessStatusCode), "[Step 2] Pagination request should succeed.")
        let data2 =
            JsonSerializer.Deserialize<NewsApiResponse>(resp2.Content.ReadAsStringAsync().Result)
        Assert.True(
            (data2.totalRecords = 5),
            (sprintf "[Step 2] Total records should remain 5 after pagination, got %d" data2.totalRecords)
        )
        Assert.True(
            (data2.news.Length = 2),
            (sprintf "[Step 2] Expected 2 news items on second page, got %d" data2.news.Length)
        )

        // --- 3. Simulate Data Update ---
        // Step 3: Simulate new data being added to the backend (2 new news items).
        let newNews =
            [ for i in 6..7 ->
                  { id = i
                    ticker = sprintf "TICK%d" i
                    dateTime = sprintf "2024-01-02 11:00:0%d" i
                    reportType = "Aviso"
                    filename = sprintf "file%d.pdf" i
                    url = sprintf "http://example.com/%d" i } ]
        let allNews = initialNews @ newNews
        let updatedJson = JsonSerializer.Serialize(allNews)
        File.WriteAllText(reportsJsonPath, updatedJson)
        Thread.Sleep(1000) // Give file watcher time to notice

        // --- 4. Client Detects New Data ---
        // Step 4: Client checks /api/news/count and should see increased totalRecords.
        let respCount = client.GetAsync("/api/news/count").Result
        Assert.True((respCount.IsSuccessStatusCode), "[Step 4] Count request should succeed.")
        let countData = JsonDocument.Parse(respCount.Content.ReadAsStringAsync().Result)
        let serverRecordCount = countData.RootElement.GetProperty("totalRecords").GetInt32()
        Assert.True(
            (serverRecordCount > localRecordCount),
            (sprintf
                "[Step 4] Server should have more records after update. Old: %d, New: %d"
                localRecordCount
                serverRecordCount)
        )
        Assert.True(
            (serverRecordCount = 7),
            (sprintf "[Step 4] Expected 7 total records after update, got %d" serverRecordCount)
        )

        // --- 5. Client Fetches Updated Data ---
        // Step 5: Client fetches first page again with updateRecords=true. Should see 7 total records, 3 pages.
        let resp3 = client.GetAsync("/api/news?pageSize=3&page=1&updateRecords=true").Result
        Assert.True((resp3.IsSuccessStatusCode), "[Step 5] Update request should succeed.")
        let data3 =
            JsonSerializer.Deserialize<NewsApiResponse>(resp3.Content.ReadAsStringAsync().Result)
        Assert.True(
            (data3.totalRecords = 7),
            (sprintf "[Step 5] Expected 7 total records after update fetch, got %d" data3.totalRecords)
        )
        Assert.True(
            (data3.totalPages = 3),
            (sprintf "[Step 5] Expected 3 total pages after update fetch, got %d" data3.totalPages)
        )
        localRecordCount <- data3.totalRecords // Client updates its count

        // --- 6. Final Pagination Check ---
        // Step 6: Client paginates to last page. Should see only 1 item, with id 7.
        let resp4 =
            client
                .GetAsync($"/api/news?pageSize=3&page=3&clientRecordCount={localRecordCount}")
                .Result
        Assert.True((resp4.IsSuccessStatusCode), "[Step 6] Final pagination request should succeed.")
        let data4 =
            JsonSerializer.Deserialize<NewsApiResponse>(resp4.Content.ReadAsStringAsync().Result)
        Assert.True(
            (data4.totalRecords = 7),
            (sprintf "[Step 6] Expected 7 total records on final page, got %d" data4.totalRecords)
        )
        Assert.True(
            (data4.news.Length = 1),
            (sprintf "[Step 6] Expected 1 news item on last page, got %d" data4.news.Length)
        )
        Assert.True(
            (data4.news |> List.last).id = 1,
            sprintf "[Step 6] Expected last news item to have id 1, got %d" (data4.news |> List.last).id
        )
    finally
        // --- Teardown ---
        // Restore original file
        if File.Exists(reportsJsonPath) then
            File.Delete(reportsJsonPath)
        if File.Exists(backupPath) then
            File.Move(backupPath, reportsJsonPath)
