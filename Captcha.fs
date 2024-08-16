module Captcha

        // driver.Navigate().GoToUrl("https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx?ID=1270813")

open System
open OpenQA.Selenium
open OpenQA.Selenium.Edge
open OpenQA.Selenium.Support.UI

let executeJavaScriptWithEdge () =
    let options = EdgeOptions()
    options.AddArguments("--headless")  // Run in headless mode
    options.AddArguments("--disable-gpu")  // Disable GPU hardware acceleration
    options.AddArguments("--no-sandbox")  // Disable the sandbox for all process types
    options.AddArguments("--disable-dev-shm-usage")  // Overcome limited resource problems
    options.AddArguments("--ignore-certificate-errors")  // Ignore SSL certificate errors
    options.AddArguments("--ignore-ssl-errors")  // Ignore SSL errors
    options.AddArguments("--disable-extensions")  // Disable extensions
    options.AddArguments("--disable-popup-blocking")  // Disable popup blocking
    options.PageLoadStrategy <- PageLoadStrategy.Eager  // Don't wait for all resources to download

    use service = EdgeDriverService.CreateDefaultService()
    service.HideCommandPromptWindow <- true  // Hide the command prompt window


    // use driver = new EdgeDriver()
    use driver = new EdgeDriver(service, options)
    
       

    try
        // Navigate to the page
        // driver.Navigate().GoToUrl("https://your-webpage-url.com")
        driver.Navigate().GoToUrl("https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx?ID=1270813")
        
        // Wait for the page to load and the reCAPTCHA to be ready
        let wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10))  // Reduced timeout
        wait.Until(fun d -> 
            (d :?> IJavaScriptExecutor).ExecuteScript("return typeof(grecaptcha) !== 'undefined'") :?> bool
        ) |> ignore
        
        // Execute the JavaScript to get the reCAPTCHA token
        let consoleScript = """
            var tokenPromise = new Promise((resolve) => {
                grecaptcha.execute(document.getElementById('hdnChaveSiteV3').value, { action: 'submit' })
                    .then(function (token) {
                        resolve(token);
                    });
            });
            return tokenPromise;
        """
        
        let token = 
            try
                (driver :> IJavaScriptExecutor).ExecuteAsyncScript(consoleScript) :?> string
            with
            | ex ->
                printfn "An error occurred while executing script: %s" ex.Message
                ""
        
        if String.IsNullOrEmpty(token) then
            printfn "Failed to retrieve reCAPTCHA token"
        else
            printfn "reCAPTCHA Token: %s" token
        
        token
    finally
        // Ensure the browser is closed even if an exception occurs
        driver.Quit()