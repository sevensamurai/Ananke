#!/usr/bin/env bash
# Fleet demo: one device state machine on two nodes, 20 simulated devices, Valkey for state and locks, Mosquitto for
# readings in and updates out. The containers' project name and ports come from devenv: Valkey on DEVENV_PORT,
# Mosquitto on DEVENV_PORT + 1000.
#   ./run.sh                          Valkey and Mosquitto up, then node-a, node-b and the devices, until Ctrl-C
#                                     (the containers, and the devices' state in Valkey, stay until ./run.sh down)
#   ./run.sh fault <device> [reason]  fault one device: both nodes refuse its readings
#   ./run.sh reset <device>           the device moves again, from its stored state
#   ./run.sh status                   the nodes and devices as Valkey has them
#   ./run.sh broker-outage <seconds>  stop Mosquitto for that long and start it again, then wait for both nodes to
#                                     reconnect and every device to move on from its state at the outage
#   ./run.sh down                     stop the nodes and devices, remove the containers and their data
set -euo pipefail
cd "$(dirname "$0")"

# devenv is the workspace's (bin/devenv); outside a shell that has it on PATH, take it from there.
command -v devenv >/dev/null || PATH="${WORKSPACE:-$HOME/workspace}/bin:$PATH"
eval "$(devenv --base 56400 || echo exit 2)"
export FLEET_VALKEY_PORT=$DEVENV_PORT
export FLEET_MOSQUITTO_PORT=$((DEVENV_PORT + 1000))

if [ -x "$HOME/.dotnet/dotnet" ]; then export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"; fi
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

RUN=.run
PIDS=$RUN/pids
DLL=bin/Release/net10.0/FleetDemo.dll
# The reconnect delay's cap in Ananke.MQTT is 30 s. On top of it: a device's next reading, up to 4 s away (each
# round of 2 s moves each device one tick earlier), and the node's report, written every second.
FLOW_LIMIT=40

compose() { docker compose -f compose.yml "$@"; }

usage() {
  echo "usage: ./run.sh [up] | fault <device> [reason] | reset <device> | status | broker-outage <seconds> | down" >&2
  exit 2
}

# A pid from the file that is still this demo's process (not a pid reused since).
ours() { { tr '\0' ' ' < "/proc/$1/cmdline"; } 2>/dev/null | grep -q "FleetDemo.dll"; }

running() {
  local pid
  [ -s "$PIDS" ] || return 1
  while read -r pid; do ours "$pid" && return 0; done < "$PIDS"
  return 1
}

stop_processes() {
  local pid
  [ -s "$PIDS" ] || return 0
  while read -r pid; do ours "$pid" && kill "$pid" 2>/dev/null || true; done < "$PIDS"
  for _ in $(seq 50); do running || break; sleep 0.2; done
  while read -r pid; do ours "$pid" && kill -9 "$pid" 2>/dev/null || true; done < "$PIDS"
  : > "$PIDS"
}

build() {
  mkdir -p "$RUN"
  dotnet build FleetDemo.csproj -c Release -nologo -v q -clp:ErrorsOnly > "$RUN/build.log" 2>&1 \
    || { cat "$RUN/build.log" >&2; exit 1; }
}

need_containers() {
  compose ps --status running --services 2>/dev/null | grep -qx valkey \
    || { echo "Valkey is not running: start the demo with ./run.sh" >&2; exit 1; }
  [ -f "$DLL" ] || build
}

up() {
  if running; then echo "Already running: ./run.sh down stops it" >&2; exit 1; fi
  mkdir -p "$RUN"
  compose up -d --wait --quiet-pull
  echo "Valkey on 127.0.0.1:$FLEET_VALKEY_PORT, Mosquitto on 127.0.0.1:$FLEET_MOSQUITTO_PORT ($COMPOSE_PROJECT_NAME)"
  build
  : > "$PIDS"
  # Ctrl-C reaches this script; the processes, started in the background, are stopped here on every way out.
  trap stop_processes EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM
  # dotnet itself in the background (not a function), so $! is its pid.
  for node in node-a node-b; do
    dotnet "$DLL" node "$node" &
    echo $! >> "$PIDS"
  done
  dotnet "$DLL" devices &
  echo $! >> "$PIDS"
  echo "node-a, node-b and the devices are running; Ctrl-C stops them. In another terminal: ./run.sh fault device-3"
  wait
}

down() {
  stop_processes
  compose down --volumes
  rm -rf "$RUN"
}

outage() {
  local seconds=${1:-} started
  [[ "$seconds" =~ ^[0-9]+$ ]] && [ "$seconds" -ge 1 ] || usage
  running || { echo "The nodes are not running: start them with ./run.sh (in another terminal)" >&2; exit 1; }
  need_containers
  started=$SECONDS
  # A failed snapshot or Ctrl-C before the restart below still starts Mosquitto again.
  trap 'compose start mosquitto >/dev/null 2>&1 || true' EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM
  compose stop mosquitto 2>/dev/null
  echo "Mosquitto stopped for $seconds s"
  # Once the readings received before the stop are handled and in a node report: the state at the outage.
  sleep 2
  dotnet "$DLL" snapshot "$RUN/outage.json"
  local left=$((seconds - (SECONDS - started)))
  [ "$left" -gt 0 ] && sleep "$left"
  compose start mosquitto 2>/dev/null
  trap - EXIT
  echo "Mosquitto started again"
  dotnet "$DLL" await-flow "$RUN/outage.json" "$FLOW_LIMIT"
}

case "${1:-up}" in
  up) up ;;
  fault) [ $# -ge 2 ] || usage; shift; need_containers; dotnet "$DLL" fault "$@" ;;
  reset) [ $# -eq 2 ] || usage; need_containers; dotnet "$DLL" reset "$2" ;;
  status) need_containers; dotnet "$DLL" status ;;
  broker-outage) [ $# -eq 2 ] || usage; outage "$2" ;;
  down) down ;;
  *) usage ;;
esac
