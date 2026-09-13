"""Drive an MTP app in server mode over TCP and dump the discovery notifications."""
import json, socket, subprocess, sys, threading, time, uuid

EXE = sys.argv[1]

listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
listener.bind(("127.0.0.1", 0))
listener.listen(1)
port = listener.getsockname()[1]

proc = subprocess.Popen([EXE, "--server", "jsonrpc", "--client-port", str(port),
                         "--client-host", "127.0.0.1"],
                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

conn, _ = listener.accept()
conn.settimeout(10)
messages = []
buf = b""


def pump():
    global buf
    while True:
        try:
            chunk = conn.recv(4096)
        except Exception:
            return
        if not chunk:
            return
        buf += chunk
        while True:
            sep = buf.find(b"\r\n\r\n")
            if sep < 0:
                break
            header = buf[:sep].decode()
            length = next(int(l.split(":")[1]) for l in header.split("\r\n")
                          if l.lower().startswith("content-length"))
            if len(buf) < sep + 4 + length:
                break
            messages.append(json.loads(buf[sep + 4: sep + 4 + length]))
            buf = buf[sep + 4 + length:]


threading.Thread(target=pump, daemon=True).start()


def send(obj):
    body = json.dumps(obj).encode()
    conn.sendall(b"Content-Length: %d\r\n\r\n" % len(body) + body)


send({"jsonrpc": "2.0", "id": 1, "method": "initialize",
      "params": {"processId": 1234,
                 "clientInfo": {"name": "probe", "version": "1.0"},
                 "capabilities": {"testing": {"debuggerProvider": False}}}})
time.sleep(2)
send({"jsonrpc": "2.0", "id": 2, "method": "testing/discoverTests",
      "params": {"runId": str(uuid.uuid4())}})
time.sleep(4)

for m in messages:
    if "testUpdates" in str(m.get("method", "")):
        for change in m.get("params", {}).get("changes", []):
            print(json.dumps(change, indent=1))
    else:
        print("---", m.get("method") or f"response id={m.get('id')}")

proc.kill()
