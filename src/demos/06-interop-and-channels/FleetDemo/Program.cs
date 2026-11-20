// --------------------------------------------------------------------------
//  Ananke — FleetDemo
//  One device state machine on two nodes, 20 simulated devices, Valkey for state and locks, Mosquitto for
//  readings in and updates out. Started by run.sh, which gives each process its ports:
//
//    node <name>              a node, until Ctrl-C
//    devices                  the simulated devices, until Ctrl-C
//    fault <device> [reason]  fault one device
//    reset <device>           reset it
//    status                   nodes and devices as Valkey has them
//    snapshot <file>          the nodes' reports and the devices' state, to a file
//    await-flow <file> <s>    wait up to s seconds for the nodes and devices to move on from the snapshot
// --------------------------------------------------------------------------

using System.Runtime.InteropServices;
using FleetDemo;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    stop.Cancel();
});

try
{
    return args switch
    {
        ["node", var name] => await Node.RunAsync(name, stop.Token),
        ["devices"] => await Devices.RunAsync(stop.Token),
        ["fault", var device] => await Admin.FaultAsync(device, "fault from run.sh", stop.Token),
        ["fault", var device, .. var reason] => await Admin.FaultAsync(device, string.Join(' ', reason), stop.Token),
        ["reset", var device] => await Admin.ResetAsync(device, stop.Token),
        ["status"] => await Admin.StatusAsync(stop.Token),
        ["snapshot", var file] => await Admin.SnapshotAsync(file, stop.Token),
        ["await-flow", var file, var seconds] when int.TryParse(seconds, out var limit) =>
            await Admin.AwaitFlowAsync(file, TimeSpan.FromSeconds(limit), stop.Token),
        _ => Usage(),
    };
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    return 130;
}

static int Usage()
{
    Console.Error.WriteLine("usage: FleetDemo node <name> | devices | fault <device> [reason] | reset <device> | status | " +
                            "snapshot <file> | await-flow <file> <seconds>  (run it through ./run.sh)");
    return 2;
}
