# FleetDemo — one state machine, many devices, two nodes

One `AbstractStateMachine` serves 20 simulated devices from two node processes. Each device is a
context: its state, step and fault live in Valkey (`Ananke.Redis`), each transition runs under that
device's lock, and readings arrive and updates leave over Mosquitto (`Ananke.MQTT`). It shows that
the state machine holds across nodes and through a broker outage.

```
 devices ──readings──► Mosquitto ──shared subscription──► node-a, node-b ──► Valkey
    ▲                    (via the broker)                       │        state, step, fault and lock per device
    └───────────────────── updates ◄────────────────────────────┘
```

- **`devices`** sends each device's reading about every 2 s: on `fleet/devicetransition/move` while
  it drives, on `…/stop` while it is parked. It counts the updates the nodes send back.
- **`node-a`, `node-b`** run the same `DeviceMachine` (`Parked --Move--> Moving --Stop--> Parked`)
  behind a `StateMachineChannelWorker`. Their reader is a shared subscription (`GroupName = "nodes"`),
  so the broker hands each reading to one node, in turn; the devices' order shifts each round, so
  a device's readings go to node-a and node-b alternately, each run under the device's lock.
  A change of state is sent out on `fleet/devicenotification/statechanged`.
- Every 5 s each node prints the devices it moved, and the ones it refused.

## Needs

Docker, the .NET SDK the repository pins, and `devenv` (the workspace's `bin/devenv`), which gives
this checkout its container names and ports: Valkey on `DEVENV_PORT`, Mosquitto on `DEVENV_PORT + 1000`
(base 56400). Both listen on 127.0.0.1 only.

## Steps

From this folder:

1. **`./run.sh`** starts Valkey and Mosquitto, builds the demo, then runs `node-a`, `node-b` and the
   devices until Ctrl-C:

   ```
   node-a  moved 15: device-1 device-3 device-5 device-7 device-8 …
   node-b  moved 16: device-2 device-3 device-4 device-6 device-9 …
   devices  sent 50 readings, 12 updates back
   ```

   Ctrl-C stops the processes; the containers and the devices' state stay, so the next `./run.sh`
   carries on from the same steps.

2. In another terminal, **`./run.sh fault device-3`** faults device-3 in Valkey. Both nodes refuse
   its readings, every other device keeps moving:

   ```
   node-b  moved 17: device-1 device-2 device-4 …  | refused: device-3 (Faulted: fault from run.sh)
   node-a  moved 16: device-1 device-2 device-4 …  | refused: device-3 (Faulted: fault from run.sh)
   ```

   **`./run.sh reset device-3`** brings it back; it moves on from its stored state and step.

3. **`./run.sh broker-outage 60`** stops Mosquitto for 60 s and starts it again. Meanwhile the
   nodes print `moved none (not connected to the broker)` and their clients' reconnect attempts,
   each wait doubled up to the 30 s cap. Once the broker is back, it waits until both nodes are
   connected and moving devices again and every device has moved on from its step at the outage:

   ```
   node-b connected again 23 s after the broker returned (reconnects 1)
   node-a connected again 26 s after the broker returned (reconnects 1)
   Messages flow again: all 20 operative devices moved on from their step at the outage (device-1 63->66, …); no state lost.
   ```

   A device whose step went back would be reported as having lost its state, and the command
   fails. It also fails when the flow hasn't resumed 40 s after the broker returned.

4. **`./run.sh status`** prints both nodes' reports and every device's state, step and fault, as
   Valkey has them.

5. **`./run.sh down`** stops the processes and removes the containers and their data.
