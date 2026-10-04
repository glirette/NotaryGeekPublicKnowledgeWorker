"""Disposable, durable byte authority for the socket-free W08 SDK harness.

The parent scheduler calls ``handle`` only when a request may linearize. The
method commits before returning; retaining its result without sending it to the
child models a committed operation whose reply was lost. SQLite is owned by the
parent, never shared as an in-memory object with a worker process. This module
knows HTTP conditions and bytes, not worker admission or publication rules.
"""

from __future__ import annotations

import base64
from contextlib import contextmanager
from dataclasses import dataclass
import json
import os
from pathlib import Path
import sqlite3
from typing import Any, Iterator, Mapping
from urllib.parse import parse_qsl, unquote, urlsplit
import xml.etree.ElementTree as ET


STAMP = "Mon, 01 Jan 2024 00:00:00 GMT"
AMBIENT_IDENTITY_VARIABLES = (
    "AZURE_CLIENT_ID", "AZURE_TENANT_ID", "AZURE_CLIENT_SECRET",
    "AZURE_CLIENT_CERTIFICATE_PATH", "AZURE_FEDERATED_TOKEN_FILE",
    "MSI_ENDPOINT", "IDENTITY_ENDPOINT", "AZURE_STORAGE_CONNECTION_STRING",
    "AZURE_STORAGE_KEY", "AZURE_STORAGE_SAS_TOKEN", "OPENAI_API_KEY",
    "AZURE_OPENAI_API_KEY",
)


def require_no_ambient_identity() -> None:
    if any(os.environ.get(name, "").strip() for name in AMBIENT_IDENTITY_VARIABLES):
        raise ValueError("Ambient credentials are forbidden in the synthetic authority.")


@dataclass(frozen=True)
class Blob:
    body: bytes
    etag: str
    content_type: str = "application/json"


class Authority:
    """SQLite-backed Blob/Queue primitives; every call is one atomic operation.

    ``broken_cas`` is an explicit negative control: conditional writes ignore
    their preconditions. It must never be enabled in passing model runs.
    """

    def __init__(self, path: str | Path, page_size: int = 2,
                 broken_cas: bool = False) -> None:
        require_no_ambient_identity()
        if str(path) == ":memory:" or page_size < 1:
            raise ValueError("A durable database path and positive page size are required.")
        self.path = Path(path)
        self.page_size = page_size
        self.broken_cas = broken_cas
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self._transaction() as db:
            db.execute("CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value INTEGER NOT NULL)")
            db.execute("INSERT OR IGNORE INTO state VALUES ('version', 0)")
            db.execute("CREATE TABLE IF NOT EXISTS resources (name TEXT PRIMARY KEY)")
            db.execute("CREATE TABLE IF NOT EXISTS blobs (name TEXT PRIMARY KEY, body BLOB NOT NULL, "
                       "etag TEXT NOT NULL, content_type TEXT NOT NULL)")
            db.execute("CREATE TABLE IF NOT EXISTS queue_messages (sequence INTEGER PRIMARY KEY AUTOINCREMENT, "
                       "body BLOB NOT NULL)")
            db.execute("CREATE TABLE IF NOT EXISTS effects (sequence INTEGER PRIMARY KEY AUTOINCREMENT, "
                       "identity TEXT NOT NULL)")

    def close(self) -> None:
        """No handles survive an operation; retained for scheduler cleanup symmetry."""

    @contextmanager
    def _transaction(self) -> Iterator[sqlite3.Connection]:
        # No worker lifetime owns this connection. Full synchronous commits and
        # the persistent counter keep bytes and ETags stable across process death.
        db = sqlite3.connect(str(self.path), isolation_level=None, timeout=30)
        try:
            db.execute("PRAGMA synchronous=FULL")
            db.execute("BEGIN IMMEDIATE")
            yield db
            db.commit()
        except BaseException:
            db.rollback()
            raise
        finally:
            db.close()

    @staticmethod
    def _read(db: sqlite3.Connection, name: str) -> Blob | None:
        row = db.execute("SELECT body, etag, content_type FROM blobs WHERE name=?", (name,)).fetchone()
        return Blob(bytes(row[0]), row[1], row[2]) if row else None

    @staticmethod
    def _write(db: sqlite3.Connection, name: str, body: bytes, content_type: str) -> Blob:
        db.execute("UPDATE state SET value=value+1 WHERE key='version'")
        version = db.execute("SELECT value FROM state WHERE key='version'").fetchone()[0]
        blob = Blob(body, f'"{version}"', content_type)
        db.execute("INSERT INTO blobs VALUES (?, ?, ?, ?) ON CONFLICT(name) DO UPDATE "
                   "SET body=excluded.body, etag=excluded.etag, content_type=excluded.content_type",
                   (name, blob.body, blob.etag, blob.content_type))
        return blob

    def get(self, name: str) -> Blob | None:
        with self._transaction() as db:
            return self._read(db, name)

    def snapshot(self) -> dict[str, Blob]:
        with self._transaction() as db:
            return {name: Blob(bytes(body), etag, content_type)
                    for name, body, etag, content_type in db.execute(
                        "SELECT name, body, etag, content_type FROM blobs ORDER BY name")}

    def seed(self, name: str, body: bytes | str,
             content_type: str = "application/json") -> Blob:
        if not isinstance(name, str) or not name:
            raise ValueError("A nonempty opaque object name is required.")
        data = body.encode("utf-8") if isinstance(body, str) else bytes(body)
        with self._transaction() as db:
            return self._write(db, name, data, content_type)

    def queue_bodies(self) -> list[bytes]:
        with self._transaction() as db:
            return [bytes(row[0]) for row in db.execute(
                "SELECT body FROM queue_messages ORDER BY sequence")]

    def effect(self, identity: Any) -> int:
        """Append an opaque external-effect observation, including duplicates.

        This method neither consults stored blobs nor authorizes an operation.
        The scheduler invokes it at the synthetic provider's effect boundary.
        """
        encoded = json.dumps(identity, sort_keys=True, separators=(",", ":"), allow_nan=False)
        with self._transaction() as db:
            cursor = db.execute("INSERT INTO effects(identity) VALUES (?)", (encoded,))
            return int(cursor.lastrowid)

    def effects(self) -> list[Any]:
        with self._transaction() as db:
            return [json.loads(row[0]) for row in db.execute("SELECT identity FROM effects ORDER BY sequence")]

    @staticmethod
    def _response(request_id: Any, status: int, body: bytes = b"",
                  content_type: str = "application/xml", **headers: str) -> dict[str, Any]:
        response_headers = {
            "Content-Type": content_type,
            "Content-Length": str(len(body)),
            "Last-Modified": STAMP,
            "x-ms-request-id": "w08-synthetic-request",
            "x-ms-version": "2025-11-05",
        }
        response_headers.update(headers)
        return {"id": request_id, "status": status, "headers": response_headers,
                "body": base64.b64encode(body).decode("ascii")}

    @classmethod
    def _error(cls, request_id: Any, status: int, code: str) -> dict[str, Any]:
        root = ET.Element("Error")
        ET.SubElement(root, "Code").text = code
        ET.SubElement(root, "Message").text = "Synthetic storage response"
        return cls._response(request_id, status, ET.tostring(root, encoding="utf-8"),
                             **{"x-ms-error-code": code})

    @staticmethod
    def _parse(request: Mapping[str, Any]) -> tuple[str, str, str, dict[str, str], dict[str, str], bytes]:
        if request.get("kind", "http") not in {"http", "storage", "queue"}:
            raise ValueError("Only storage and queue HTTP requests enter this authority.")
        uri = urlsplit(request["url"])
        if (uri.scheme != "https" or uri.netloc not in {"storage.invalid", "queue.invalid"}
                or uri.fragment):
            raise ValueError("Synthetic authority rejects external endpoints and credentials.")
        raw_headers = request.get("headers", {})
        headers: dict[str, str] = {}
        for key, value in raw_headers.items():
            if not isinstance(key, str) or not isinstance(value, str) or key.lower() in headers:
                raise ValueError("Malformed or duplicate HTTP header.")
            headers[key.lower()] = value
        if any(key in headers for key in ("authorization", "proxy-authorization", "cookie")):
            raise ValueError("Synthetic authority rejects authentication headers.")
        query: dict[str, str] = {}
        for key, value in parse_qsl(uri.query, keep_blank_values=True, strict_parsing=False):
            if key in query:
                raise ValueError("Duplicate query parameter.")
            if key.lower() in {"sig", "sv", "se", "sp", "sr", "spr", "st", "sip", "si",
                               "skoid", "sktid", "skt", "ske", "sks", "skv", "ss", "srt"}:
                raise ValueError("Synthetic authority rejects SAS credentials.")
            query[key] = value
        path = unquote(uri.path, errors="strict")
        if path != "/fixture" and not path.startswith("/fixture/"):
            raise ValueError("Unexpected fixture path.")
        body = base64.b64decode(request.get("body", ""), validate=True)
        return uri.netloc, request["method"], path[9:] if len(path) > 8 else "", query, headers, body

    def handle(self, request: Mapping[str, Any]) -> dict[str, Any]:
        """Commit one request and return a reply; the caller controls delivery."""
        host, method, name, query, headers, body = self._parse(request)
        request_id = request.get("id")
        with self._transaction() as db:
            if host == "queue.invalid":
                response = self._queue(db, request_id, method, name, query, body)
            else:
                response = self._blob(db, request_id, method, name, query, headers, body)
        return response

    def _queue(self, db: sqlite3.Connection, request_id: Any, method: str,
               name: str, query: dict[str, str], body: bytes) -> dict[str, Any]:
        if method == "PUT" and not name and not query:
            exists = db.execute("SELECT 1 FROM resources WHERE name='queue'").fetchone()
            db.execute("INSERT OR IGNORE INTO resources VALUES ('queue')")
            return self._response(request_id, 204 if exists else 201)
        if method == "POST" and name == "messages" and not query:
            root = ET.fromstring(body)
            text = root.find("MessageText")
            if root.tag != "QueueMessage" or text is None:
                raise ValueError("Expected SDK QueueMessage XML.")
            decoded = base64.b64decode(text.text or "", validate=True)
            cursor = db.execute("INSERT INTO queue_messages(body) VALUES (?)", (decoded,))
            result = ET.Element("QueueMessagesList")
            message = ET.SubElement(result, "QueueMessage")
            for key, value in {
                "MessageId": f"synthetic-{cursor.lastrowid}", "InsertionTime": STAMP,
                "ExpirationTime": "Mon, 08 Jan 2024 00:00:00 GMT",
                "PopReceipt": f"synthetic-{cursor.lastrowid}", "TimeNextVisible": STAMP,
            }.items():
                ET.SubElement(message, key).text = value
            return self._response(request_id, 201, ET.tostring(result, encoding="utf-8"))
        if method == "GET" and not name and query == {"comp": "metadata"}:
            count = db.execute("SELECT COUNT(*) FROM queue_messages").fetchone()[0]
            return self._response(request_id, 200, **{"x-ms-approximate-messages-count": str(count)})
        raise ValueError(f"Unexpected queue primitive: {method} {name}")

    def _blob(self, db: sqlite3.Connection, request_id: Any, method: str, name: str,
              query: dict[str, str], headers: dict[str, str], body: bytes) -> dict[str, Any]:
        if query.get("restype") == "container" and not name:
            if method == "PUT" and query == {"restype": "container"}:
                if db.execute("SELECT 1 FROM resources WHERE name='container'").fetchone():
                    return self._error(request_id, 409, "ContainerAlreadyExists")
                db.execute("INSERT INTO resources VALUES ('container')")
                return self._response(request_id, 201)
            if (method == "GET" and query.get("comp") == "list"
                    and query.keys() <= {"restype", "comp", "prefix", "marker", "maxresults", "include"}):
                return self._list(db, request_id, query)
        if query or not name:
            raise ValueError("Unexpected Blob primitive.")
        current = self._read(db, name)
        if method == "PUT":
            if headers.get("x-ms-blob-type") != "BlockBlob":
                raise ValueError("Expected SDK BlockBlob upload.")
            if not self.broken_cas:
                if headers.get("if-none-match") == "*" and current is not None:
                    return self._error(request_id, 412, "ConditionNotMet")
                if "if-match" in headers and (current is None or current.etag != headers["if-match"]):
                    return self._error(request_id, 412, "ConditionNotMet")
            content_type = headers.get("x-ms-blob-content-type", "application/json")
            blob = self._write(db, name, body, content_type)
            return self._response(request_id, 201, content_type=content_type, ETag=blob.etag)
        if method in {"GET", "HEAD"}:
            if current is None:
                return self._error(request_id, 404, "BlobNotFound")
            if "if-match" in headers and headers["if-match"] != current.etag:
                return self._error(request_id, 412, "ConditionNotMet")
            # DownloadContent can ask for a range. Like the existing protocol
            # fixture, return a valid 200 response containing the entire object.
            return self._response(request_id, 200, current.body if method == "GET" else b"",
                                  content_type=current.content_type, **{
                                      "ETag": current.etag, "Content-Length": str(len(current.body)),
                                      "x-ms-blob-type": "BlockBlob", "x-ms-creation-time": STAMP,
                                  })
        raise ValueError(f"Unexpected Blob primitive: {method}")

    def _list(self, db: sqlite3.Connection, request_id: Any,
              query: dict[str, str]) -> dict[str, Any]:
        prefix, marker = query.get("prefix", ""), query.get("marker", "")
        limit = min(self.page_size, int(query.get("maxresults", self.page_size)))
        if limit < 1:
            raise ValueError("List page size must be positive.")
        rows = [row for row in db.execute("SELECT name, body, etag, content_type FROM blobs WHERE name>? "
                                         "ORDER BY name", (marker,)) if row[0].startswith(prefix)][:limit + 1]
        root = ET.Element("EnumerationResults", {
            "ServiceEndpoint": "https://storage.invalid/", "ContainerName": "fixture",
        })
        ET.SubElement(root, "Prefix").text = prefix
        ET.SubElement(root, "Marker").text = marker
        ET.SubElement(root, "MaxResults").text = str(limit)
        blobs = ET.SubElement(root, "Blobs")
        for name, body, etag, content_type in rows[:limit]:
            blob = ET.SubElement(blobs, "Blob")
            ET.SubElement(blob, "Name").text = name
            properties = ET.SubElement(blob, "Properties")
            for key, value in {
                "Creation-Time": STAMP, "Last-Modified": STAMP, "Etag": etag,
                "Content-Length": str(len(body)), "Content-Type": content_type,
                "BlobType": "BlockBlob", "LeaseStatus": "unlocked", "LeaseState": "available",
            }.items():
                ET.SubElement(properties, key).text = value
        ET.SubElement(root, "NextMarker").text = rows[limit - 1][0] if len(rows) > limit else ""
        return self._response(request_id, 200, ET.tostring(root, encoding="utf-8"))
