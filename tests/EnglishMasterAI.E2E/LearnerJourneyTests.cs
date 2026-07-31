using Microsoft.Playwright;

namespace EnglishMasterAI.E2E;

public sealed class LearnerJourneyTests
{
    [E2EFact]
    public async Task Registration_onboarding_placement_and_practice_journey()
    {
        var baseUrl = GetRequiredBaseUrl();
        var artifacts = GetArtifactsDirectory();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args =
                [
                    "--use-fake-device-for-media-stream",
                    "--use-fake-ui-for-media-stream"
                ]
            });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = baseUrl,
            IgnoreHTTPSErrors = true
        });
        await context.GrantPermissionsAsync(["microphone"]);
        await context.Tracing.StartAsync(new()
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
        var page = await context.NewPageAsync();

        try
        {
            await RegisterAsync(page);

            await page.GotoAsync("/onboarding");
            await page.Locator("#display-name").FillAsync("ผู้เรียน E2E");
            await page.Locator("main form button[type='submit']").First.ClickAsync();
            await page.WaitForURLAsync(
                url => url.Contains("/placement", StringComparison.OrdinalIgnoreCase));

            for (var question = 0; question < 12; question++)
            {
                var firstOption = page.Locator(".quiz-option").First;
                await Assertions.Expect(firstOption).ToBeVisibleAsync();
                await firstOption.ClickAsync();
                await page.Locator(".assessment-actions .btn-primary").ClickAsync();
            }
            await Assertions.Expect(page.Locator(".result-hero")).ToBeVisibleAsync(
                new() { Timeout = 30_000 });

            await page.GotoAsync("/practice/writing");
            var writingInput = page.Locator("#writing");
            var writingSubmit = page.Locator("section.surface button.btn-primary");
            await FillUntilEnabledAsync(
                writingInput,
                writingSubmit,
                "The API implementation is complete. However, the integration test is failing because the seed data is incomplete. I will update the data and rerun the tests.");
            await writingSubmit.ClickAsync();
            await Assertions.Expect(page.Locator(".feedback-score")).ToBeVisibleAsync(
                new() { Timeout = 30_000 });

            await page.GotoAsync("/practice/speaking");
            await page.Locator(".record-stage button.btn-primary").ClickAsync();
            await Assertions.Expect(page.Locator(".record-stage")).ToHaveClassAsync(
                new System.Text.RegularExpressions.Regex("is-recording"));
            await page.WaitForTimeoutAsync(1_500);
            await page.Locator(".record-stage button.btn-outline-primary").ClickAsync();
            await Assertions.Expect(page.Locator(".privacy-box")).ToBeVisibleAsync();
            await page.Locator(".privacy-box input[type='checkbox']").CheckAsync();
            await page.Locator(".privacy-box button.btn-primary").ClickAsync();
            await Assertions.Expect(page.Locator(".feedback-score")).ToBeVisibleAsync(
                new() { Timeout = 30_000 });

            await page.GotoAsync("/learn");
            var firstModule = page.Locator("a.module-card").First;
            await Assertions.Expect(firstModule).ToBeVisibleAsync();
            await firstModule.ClickAsync();
            await page.WaitForURLAsync(
                url => url.Contains("/lessons/", StringComparison.OrdinalIgnoreCase));
            await Assertions.Expect(page.Locator("main h1").First).ToBeVisibleAsync();

            // Visited last so the dashboard has real placement, practice and
            // lesson activity behind it rather than an empty profile.
            await page.GotoAsync("/dashboard");
            await Assertions.Expect(page.Locator(".page-header h1")).ToContainTextAsync(
                "สวัสดี");
            await Assertions.Expect(page.Locator("section.grid-4 .stat-card"))
                .ToHaveCountAsync(4);
            // GetWeeklyProgressAsync always projects exactly seven days, so a
            // different count means the chart stopped rendering the full week.
            await Assertions.Expect(page.Locator(".weekly-bars > div"))
                .ToHaveCountAsync(7);
            await Assertions.Expect(page.Locator("section.module-grid a.module-card").First)
                .ToBeVisibleAsync();

            await page.GotoAsync("/toeic/mock");
            var audioButton = page.GetByRole(
                AriaRole.Button,
                new()
                {
                    NameRegex = new System.Text.RegularExpressions.Regex("Play TOEIC audio")
                });
            await Assertions.Expect(audioButton).ToBeVisibleAsync(
                new() { Timeout = 30_000 });
            await audioButton.ClickAsync();
            await Assertions.Expect(page.Locator(".alert-warning")).ToContainTextAsync(
                "browser voice");
            await Assertions.Expect(page.Locator(".quiz-option")).ToHaveCountAsync(4);
        }
        catch
        {
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(artifacts, "learner-journey-failure.png"),
                FullPage = true
            });
            throw;
        }
        finally
        {
            await context.Tracing.StopAsync(new()
            {
                Path = Path.Combine(artifacts, "learner-journey-trace.zip")
            });
        }
    }

    [E2EFact]
    public async Task Public_account_pages_fit_a_mobile_viewport()
    {
        var baseUrl = GetRequiredBaseUrl();
        var artifacts = GetArtifactsDirectory();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = baseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        var page = await context.NewPageAsync();

        foreach (var path in new[] { "/", "/Account/Login", "/Account/Register", "/privacy", "/terms" })
        {
            await page.GotoAsync(path);
            await Assertions.Expect(page.Locator("main")).ToBeVisibleAsync();
            var overflows = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
            Assert.False(overflows, $"{path} overflows a 390px-wide viewport.");
        }

        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "mobile-register.png"),
            FullPage = true
        });
    }

    /// <summary>
    /// Types into a bound field until the control it gates becomes enabled.
    /// Interactive server components are not wired until the circuit connects,
    /// and anything written before that is discarded by Blazor's first render,
    /// so both the value and the event that commits it have to be repeated.
    /// </summary>
    private static async Task FillUntilEnabledAsync(
        ILocator boundInput,
        ILocator dependentButton,
        string text,
        int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline
            && await dependentButton.IsDisabledAsync())
        {
            await boundInput.FillAsync(text);
            // Fire both so this keeps working whether the field binds on input
            // or on the default change event.
            await boundInput.DispatchEventAsync("input");
            await boundInput.DispatchEventAsync("change");
            await boundInput.Page.WaitForTimeoutAsync(500);
        }

        await Assertions.Expect(dependentButton).ToBeEnabledAsync(
            new() { Timeout = 5_000 });
    }

    private static async Task RegisterAsync(IPage page)
    {
        var unique = Guid.NewGuid().ToString("N");
        var password = $"E2e!Password-{unique}";
        await page.GotoAsync("/Account/Register");
        await page.Locator("[id='Input.Email']")
            .FillAsync($"e2e-{unique}@example.test");
        await page.Locator("[id='Input.Password']").FillAsync(password);
        await page.Locator("[id='Input.ConfirmPassword']").FillAsync(password);
        await page.Locator("[id='Input.AcceptLegal']").CheckAsync();
        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "สมัครสมาชิก", Exact = true })
            .ClickAsync();
        await page.WaitForURLAsync(
            url => !url.Contains("/Account/Register", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRequiredBaseUrl()
    {
        return Environment.GetEnvironmentVariable("E2E_BASE_URL")
            ?? throw new InvalidOperationException(
                "E2E_BASE_URL must be configured for an E2E test run.");
    }

    private static string GetArtifactsDirectory()
    {
        var artifacts = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "artifacts",
            "e2e"));
        Directory.CreateDirectory(artifacts);
        return artifacts;
    }
}
