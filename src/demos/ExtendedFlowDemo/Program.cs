using ExtendedFlowDemo.Examples;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║       Ananke — Extended Flow Demo            ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine();

await ParallelResearchExample.RunAsync();
await BestEffortIngestExample.RunAsync();
await MultiStepBranchesExample.RunAsync();
await NestedSubFlowExample.RunAsync();
await InterruptApprovalExample.RunAsync();
await ForkWithSubFlowExample.RunAsync();
await WorkflowStreamingExample.RunAsync();

Console.WriteLine("All scenarios completed.");
