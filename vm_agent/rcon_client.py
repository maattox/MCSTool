"""Minimal Minecraft RCON client (localhost use on the VM)."""

from __future__ import annotations

import socket
import struct


class RconError(RuntimeError):
    pass


class RconClient:
    def __init__(self, host: str, port: int, password: str, timeout: float = 5.0) -> None:
        self.host = host
        self.port = port
        self.password = password
        self.timeout = timeout
        self._sock: socket.socket | None = None
        self._req_id = 0

    def __enter__(self) -> RconClient:
        self.connect()
        return self

    def __exit__(self, *args: object) -> None:
        self.close()

    def connect(self) -> None:
        if self._sock is not None:
            return
        sock = socket.create_connection((self.host, self.port), timeout=self.timeout)
        self._sock = sock
        resp_id, _ = self._send(3, self.password)
        if resp_id == -1:
            self.close()
            raise RconError("RCON authentication failed")

    def close(self) -> None:
        if self._sock is not None:
            try:
                self._sock.close()
            finally:
                self._sock = None

    def command(self, cmd: str) -> str:
        self.connect()
        _, payload = self._send(2, cmd)
        return payload

    def _send(self, req_type: int, payload: str) -> tuple[int, str]:
        assert self._sock is not None
        self._req_id += 1
        req_id = self._req_id
        # length = id(4) + type(4) + payload + 2 null terminators
        encoded = payload.encode("utf-8")
        length = 4 + 4 + len(encoded) + 2
        packet = struct.pack("<iii", length, req_id, req_type) + encoded + b"\x00\x00"
        self._sock.sendall(packet)
        return self._read()

    def _read(self) -> tuple[int, str]:
        assert self._sock is not None
        data = self._recv_exact(4)
        (length,) = struct.unpack("<i", data)
        data = self._recv_exact(length)
        req_id, req_type = struct.unpack("<ii", data[:8])
        payload = data[8:-2].decode("utf-8", errors="replace")
        return req_id, payload

    def _recv_exact(self, n: int) -> bytes:
        assert self._sock is not None
        buf = b""
        while len(buf) < n:
            chunk = self._sock.recv(n - len(buf))
            if not chunk:
                raise RconError("RCON connection closed")
            buf += chunk
        return buf


def parse_list_online_count(list_response: str) -> int:
    """
    Parse `list` output. Examples:
      There are 0 of a max of 20 players online:
      There are 2 of a max of 20 players online: Steve, Alex
    """
    text = (list_response or "").strip()
    lower = text.lower()
    marker = "there are "
    idx = lower.find(marker)
    if idx < 0:
        return 0
    rest = text[idx + len(marker) :]
    num = ""
    for ch in rest:
        if ch.isdigit():
            num += ch
        elif num:
            break
    return int(num) if num else 0
