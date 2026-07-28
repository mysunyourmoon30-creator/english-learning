using Microsoft.Playwright;

namespace EnglishMasterAI.E2E;

public sealed class LearnerJourneyTests
{
    [Fact]
    public async Task Registration_lesson_and_toeic_audio_journey()
    {
        var baseUrl = Environment.GetEnvironmentVariable("E2E_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return;
        }

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
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = true
            });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = baseUrl,
            IgnoreHTTPSErrors = true
        });
        await context.Tracing.StartAsync(new()
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
        var page = await context.NewPageAsync();

        try
        {
            var unique = Guid.NewGuid().ToString("N");
            await page.GotoAsync("/Account/Register");
            await page.GetByLabel("Email", new() { Exact = true })
                .FillAsync($"e2e-{unique}@example.test");
            await page.GetByLabel("Password", new() { Exact = true })
                .FillAsync($"E2e!Password-{unique}");
            await page.GetByLabel("Confirm Password", new() { Exact = true })
                .FillAsync($"E2e!Password-{unique}");
            await page.GetByRole(AriaRole.Button, new() { Name = "Register" })
                .ClickAsync();
            await page.WaitForURLAsync(
                url => !url.Contains("/Account/Register", StringComparison.OrdinalIgnoreCase));

            await page.GotoAsync("/learn");
            var firstModule = page.Locator("a.module-card").First;
            await Assertions.Expect(firstModule).ToBeVisibleAsync();
            await firstModule.ClickAsync();
            await page.WaitForURLAsync(
                url => url.Contains("/lessons/", StringComparison.OrdinalIgnoreCase));
            await Assertions.Expect(page.Locator("main h1").First).ToBeVisibleAsync();

            await page.GotoAsync("/toeic/mock");
            var audioButton = page.GetByRole(
                AriaRole.Button,
                new() { NameRegex = new System.Text.RegularExpressions.Regex("Play TOEIC audio") });
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
}
