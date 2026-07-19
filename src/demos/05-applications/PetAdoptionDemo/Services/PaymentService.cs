using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using System.Text;


/// <summary>
/// Standalone payment service using MQTT — runs as a second process to handle
/// payment handoff requests from the main web application.
/// <para>
/// Each incoming request is processed through a <see cref="Workflow{TState}"/> pipeline:
/// <c>validate → [valid?] → charge → invoice → End</c>
/// </para>
/// <para>
/// Start with: <c>dotnet run -- --payment-service</c>
/// </para>
/// </summary>
internal static class PaymentService
{
    internal static async Task RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var host = Host.CreateApplicationBuilder(args);
        host.Configuration
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("secrets.json", optional: true)
            .AddEnvironmentVariables();

        using var app = host.Build();
        var config = app.Services.GetRequiredService<IConfiguration>();

        var mqttHost = config["Mqtt:Host"];
        var mqttPort = int.TryParse(config["Mqtt:Port"], out var p) ? p : 1883;
        var mqttNs = config["Mqtt:Namespace"] ?? "handoff";

        Console.WriteLine("━━━ Happy Tails — Payment Service ━━━");

        if (string.IsNullOrWhiteSpace(mqttHost))
        {
            Console.Error.WriteLine("  ✗ MQTT is not configured. Set Mqtt:Host in appsettings.json or via environment.");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine($"  Connecting to MQTT broker at {mqttHost}:{mqttPort}...");

        IHandoffChannel channel;
        try
        {
            channel = await HandoffChannel.ConnectAsync(new ChannelConfig
            {
                Host = mqttHost,
                Port = mqttPort,
                Namespace = mqttNs
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ✗ {ex.Message}");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine("  ✓ Connected to MQTT broker");
        Console.WriteLine($"  Listening for payment requests on '{PaymentConstants.QueueName}'...");
        Console.WriteLine("  Press Ctrl+C to stop.");
        Console.WriteLine();

        // ── Payment processing workflow ──────────────────────────────
        //
        //   validate → [valid?] → charge → invoice → End
        //                ↓ invalid
        //               End
        //
        var workflow = new Workflow<PaymentState>("payment-processing")

            .Job("validate", async (state, ct) =>
            {
                Console.WriteLine($"    ▶ Validating card ending {state.Last4}...");
                await Task.Delay(500, ct);
                var valid = state.Last4.Length == 4 && state.Last4.All(char.IsDigit);
                Console.WriteLine(valid ? "    ✓ Card validated" : "    ✗ Card invalid");
                return state with { CardValid = valid };
            })
            .Then("validate", Workflow.Decide<PaymentState>(state =>
                state.CardValid ? "charge" : Workflow.End))

            .Job("charge", async (state, ct) =>
            {
                Console.WriteLine("    ▶ Processing payment...");
                await Task.Delay(1000, ct);
                var txnId = $"TXN-{Random.Shared.Next(100000, 999999)}";
                Console.WriteLine($"    ✓ Payment approved — {txnId}");
                return state with { TransactionId = txnId, Success = true };
            })
            .Then("charge", "invoice")

            .Job("invoice", async (state, ct) =>
            {
                Console.WriteLine("    ▶ Generating invoice...");
                await Task.Delay(500, ct);
                var invoiceId = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
                Console.WriteLine($"    ✓ Invoice {invoiceId} created");
                return state with
                {
                    InvoiceId = invoiceId,
                    Message = "Your adoption is complete. Welcome to the Happy Tails family!"
                };
            })
            .Then("invoice", Workflow.End)
            .Validate();

        Console.WriteLine("  ✓ Workflow validated");
        Console.WriteLine();

        await channel.SubscribeAsync<PaymentHandoff, PaymentResult>(
            PaymentConstants.QueueName,
            async payment =>
            {
                Console.WriteLine($"  💳 Payment received — session {payment.SessionId} (card ending {payment.Last4})");

                var execution = await workflow.RunAsync(new PaymentState
                {
                    SessionId = payment.SessionId,
                    Last4 = payment.Last4
                });

                var final = execution.State;

                Console.WriteLine(final.Success
                    ? $"  ✅ Complete — {final.TransactionId} / {final.InvoiceId}"
                    : "  ❌ Payment declined");
                Console.WriteLine();

                return new PaymentResult
                {
                    Success = final.Success,
                    Message = final.Message ?? "Invalid card number. Please try again.",
                    TransactionId = final.TransactionId,
                    InvoiceId = final.InvoiceId
                };
            });

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("  Payment service stopped.");
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }
}
