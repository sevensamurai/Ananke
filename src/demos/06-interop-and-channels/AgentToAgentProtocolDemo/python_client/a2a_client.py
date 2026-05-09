"""
Ananke — A2A Python Client Demo
================================

Demonstrates calling an A2A-compliant agent from Python using only
the standard library (no Ananke dependency, no third-party packages).

This is the whole point of the Agent-to-Agent Protocol: any language
that speaks HTTP + JSON-RPC can discover, inspect, and call any A2A
agent — regardless of the framework that hosts it.

Usage:
    1. Start the .NET A2A server:
         dotnet run -- --server

    2. Run this script (Python 3.9+):
         python a2a_client.py

    Optionally pass a custom base URL:
         python a2a_client.py http://localhost:5120
"""

from __future__ import annotations

import json
import sys
import urllib.request
import uuid

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5120"
A2A_PATH = "/a2a"
AGENT_CARD_URL = f"{BASE_URL}/.well-known/agent-card.json"
AGENT_ENDPOINT = f"{BASE_URL}{A2A_PATH}"


# ── Helpers ───────────────────────────────────────────────────────────

def json_rpc_request(method: str, params: dict) -> dict:
    """Build a JSON-RPC 2.0 request envelope."""
    return {
        "jsonrpc": "2.0",
        "id": str(uuid.uuid4()),
        "method": method,
        "params": params,
    }


def post_json(url: str, payload: dict) -> dict:
    """POST a JSON body and return the parsed response."""
    data = json.dumps(payload).encode()
    req = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())


def get_json(url: str) -> dict:
    """GET a JSON resource."""
    req = urllib.request.Request(url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())


def send_message(text: str) -> str:
    """Send a message/send JSON-RPC call and extract the text response."""
    payload = json_rpc_request("message/send", {
        "message": {
            "role": "user",
            "messageId": str(uuid.uuid4()),
            "parts": [{"kind": "text", "text": text}],
        }
    })
    response = post_json(AGENT_ENDPOINT, payload)

    if "error" in response:
        return f"ERROR: {response['error']}"

    result = response.get("result", {})

    # The result is typically an AgentTask with artifacts or a status message.
    # Try artifacts first, then status message, then raw text.
    artifacts = result.get("artifacts") or []
    for artifact in artifacts:
        for part in artifact.get("parts") or []:
            if part.get("kind") == "text" or "text" in part:
                return part["text"]

    # Fall back to status message
    status = result.get("status", {})
    for part in (status.get("message") or {}).get("parts") or []:
        if part.get("kind") == "text" or "text" in part:
            return part["text"]

    return json.dumps(result, indent=2)


# ── Main ──────────────────────────────────────────────────────────────

def main() -> None:
    banner = "Ananke — A2A Python Client"
    print("=" * 58)
    print(f"  {banner}")
    print("=" * 58)
    print()

    # ── 1. Discover the agent ─────────────────────────────────────

    print("── Step 1: Agent Discovery (GET /.well-known/agent-card.json) ──")
    print()

    card = get_json(AGENT_CARD_URL)

    print(f"  Name:        {card.get('name')}")
    print(f"  Description: {card.get('description')}")
    print(f"  Version:     {card.get('version')}")
    skills = card.get("skills") or []
    print(f"  Skills:      {len(skills)}")
    for skill in skills:
        print(f"    • {skill.get('name')} — {skill.get('description')}")
    print()

    # ── 2. Send messages via JSON-RPC ─────────────────────────────

    print("── Step 2: Send messages via JSON-RPC (message/send) ──")
    print()

    messages = [
        ("Pipeline",        "Hello from Python!"),
        ("Tool: word_count", "word_count: The quick brown fox jumps over the lazy dog"),
        ("Tool: reverse",   "reverse: Ananke"),
        ("Tool: uppercase", "uppercase: distributed state machines"),
    ]

    for label, text in messages:
        print(f"  [{label}] Sending: \"{text}\"")
        result = send_message(text)
        print(f"  [{label}] Response: {result}")
        print()

    # ── 3. Highlight interoperability ─────────────────────────────

    print("── Step 3: Interoperability ──")
    print()
    print("  This Python script uses ONLY the standard library.")
    print("  No Ananke SDK, no pip install — just HTTP + JSON-RPC.")
    print()
    print("  The same A2A agent can be called from any language:")
    print("    • Python  (this script)")
    print("    • C#      (Ananke A2AAgentModel)")
    print("    • Node.js, Go, Rust, Java, curl …")
    print()
    print("=" * 58)
    print("  Done.")
    print("=" * 58)


if __name__ == "__main__":
    main()
